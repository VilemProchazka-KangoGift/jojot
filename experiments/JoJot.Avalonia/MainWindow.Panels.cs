using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using JoJot.AvaloniaExperiment.Platform;
using JoJot.Resources;
using JoJot.Services;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow
{
    private string? _currentPanel;
    private StackPanel? _panelBody;
    private TextBlock? _fontSizeDisplay;
    private bool _recoveryBusy;

    internal static Func<IReadOnlyCollection<string>>? OrphanSessionGuids { get; set; }
    internal static Action<string>? OrphanSessionResolved { get; set; }
    internal static Func<Task>? RestartForLanguageAsync { get; set; }
    internal static Func<Task>? ExitFromMenuAsync { get; set; }
    internal static Func<string>? CurrentGlobalShortcut { get; set; }
    internal static Action<bool>? PauseGlobalShortcut { get; set; }
    internal static Func<Key, KeyModifiers, Task<bool>>? RecordGlobalShortcutAsync { get; set; }

    private bool _recordingGlobalShortcut;
    private Button? _recordShortcutButton;
    private TextBlock? _globalShortcutDisplay;

    private async Task InitializePanelsAsync()
    {
        PanelLanguage.Apply(await PreferenceStore.GetPreferenceAsync("language"));
        Editor.FontSize = ParsePanelFontSize(await PreferenceStore.GetPreferenceAsync("font_size"));
        SetEditorWrap(true);
        SetAutosaveInterval(500);
        ApplyTheme(await PreferenceStore.GetPreferenceAsync("theme"));
        PreferencesMenuItem.Header = Strings.Menu_Preferences;
        CleanupMenuItem.Header = Strings.Menu_Cleanup;
        RecoveryMenuItem.Header = Strings.Menu_Recover;
        HelpMenuItem.Header = Strings.Help_Title;
        ExitMenuItem.Header = Strings.Menu_Exit;
        AutomationProperties.SetName(HamburgerButton, Strings.Tooltip_Menu);
        UpdateOrphanBadge();
    }

    internal static int ParsePanelFontSize(string? value) =>
        int.TryParse(value, out var parsed) ? Math.Clamp(parsed, 8, 32) : 13;

    private async Task SetEditorFontAsync(double value)
    {
        var size = (int)Math.Clamp(value, 8, 32);
        Editor.FontSize = size;
        UpdateFontSizeDisplay();
        await PreferenceStore.SetPreferenceAsync("font_size", size.ToString(CultureInfo.InvariantCulture));
        ShowInfoToast($"{Math.Round(size * 100.0 / 13)}%");
    }

    private void UpdateFontSizeDisplay()
    {
        if (_fontSizeDisplay is not null)
            _fontSizeDisplay.Text = $"{Math.Round(Editor.FontSize * 100.0 / 13)}%";
    }

    private IEnumerable<MainWindow> PanelWindows() =>
        ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
            .OfType<MainWindow>() ?? []).Append(this).Distinct();

    private async Task ExitUsingMenuAsync()
    {
        if (ExitFromMenuAsync is { } exit)
        {
            await exit();
            return;
        }

        foreach (var window in PanelWindows().ToArray())
            window.Close();
    }

    private bool DismissSidePanel()
    {
        if (_currentPanel is null)
            return false;

        if (_currentPanel == "help")
            RemoveHandler(PointerPressedEvent, OnHelpOutsidePressed);
        StopShortcutRecording();
        _currentPanel = null;
        _panelBody = null;
        _fontSizeDisplay = null;
        _recordShortcutButton = null;
        _globalShortcutDisplay = null;
        PanelHost.Child = null;
        PanelHost.IsVisible = false;
        PanelHost.HorizontalAlignment = HorizontalAlignment.Right;
        PanelHost.VerticalAlignment = VerticalAlignment.Stretch;
        PanelHost.MaxHeight = double.PositiveInfinity;
        Editor.Focus();
        return true;
    }

    private StackPanel? TogglePanel(string panel, string title)
    {
        if (!_ready || _busy || _closing)
            return null;
        if (_currentPanel == panel)
        {
            DismissSidePanel();
            return null;
        }

        DismissSidePanel();
        if (FindHost.IsVisible)
            HideFindPanel();
        _currentPanel = panel;
        var close = PanelButton("\u00D7", () => DismissSidePanel());
        AutomationProperties.SetName(close, Strings.Common_Cancel);
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new TextBlock
        {
            Text = title, FontWeight = FontWeight.SemiBold, FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
        });
        _panelBody = new StackPanel { Spacing = 10 };
        var layout = new DockPanel { Margin = new Thickness(14), LastChildFill = true, Width = 288 };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(new ScrollViewer
        {
            Content = _panelBody, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        });
        PanelHost.Child = layout;
        PanelHost.IsVisible = true;
        close.Focus();
        return _panelBody;
    }

    private static Button PanelButton(string label, Action click)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => click();
        return button;
    }

    private Button PanelAsyncButton(string label, Func<Task> click)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) => await RunPanelActionAsync(click);
        return button;
    }

    private async Task RunPanelActionAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { ReportError(ex); }
    }

    private static StackPanel PanelRow(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }

    private static TextBlock PanelText(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    private static TextBlock PanelSection(string text) => new()
    {
        Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0),
    };

    private void ShowPreferences() => _ = RunPanelActionAsync(ShowPreferencesAsync);

    private async Task ShowPreferencesAsync()
    {
        var body = TogglePanel("preferences", Strings.Pref_Header);
        if (body is null)
            return;

        var themePreference = await PreferenceStore.GetPreferenceAsync("theme");
        if (!ReferenceEquals(body, _panelBody))
            return;

        body.Children.Add(PanelSection(Strings.Pref_SectionAppearance));
        body.Children.Add(PanelText(Strings.Pref_Theme));
        var themes = new ComboBox
        {
            ItemsSource = new[] { Strings.Pref_ThemeSystem, Strings.Pref_ThemeLight, Strings.Pref_ThemeDark },
            SelectedIndex = themePreference switch { "light" => 1, "dark" => 2, _ => 0 },
        };
        AutomationProperties.SetName(themes, Strings.Pref_Theme);
        themes.SelectionChanged += async (_, _) => await RunPanelActionAsync(async () =>
        {
            var choice = themes.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
            await PreferenceStore.SetPreferenceAsync("theme", choice);
            foreach (var window in PanelWindows())
                window.ApplyTheme(choice);
        });
        body.Children.Add(themes);
        body.Children.Add(PanelText(Strings.Pref_FontSize));
        _fontSizeDisplay = PanelText("");
        _fontSizeDisplay.VerticalAlignment = VerticalAlignment.Center;
        UpdateFontSizeDisplay();
        body.Children.Add(PanelRow(
            PanelAsyncButton("-", () => SetEditorFontAsync(Editor.FontSize - 1)),
            _fontSizeDisplay,
            PanelAsyncButton("+", () => SetEditorFontAsync(Editor.FontSize + 1)),
            PanelAsyncButton(Strings.Pref_FontReset, () => SetEditorFontAsync(13))));

        body.Children.Add(PanelSection(Strings.Pref_SectionLanguage));
        var english = PanelAsyncButton("English", () => ChangePanelLanguageAsync("en"));
        var czech = PanelAsyncButton("\u010Ce\u0161tina", () => ChangePanelLanguageAsync("cs"));
        english.IsEnabled = PanelLanguage.Current != "en" && RestartForLanguageAsync is not null;
        czech.IsEnabled = PanelLanguage.Current != "cs" && RestartForLanguageAsync is not null;
        body.Children.Add(PanelRow(english, czech));
        body.Children.Add(PanelText(Strings.Pref_LanguageHint));

        body.Children.Add(PanelSection(Strings.Pref_SectionShortcuts));
        body.Children.Add(PanelText(Strings.Pref_GlobalHotkey));
        _globalShortcutDisplay = PanelText(CurrentGlobalShortcut?.Invoke() ?? "Win+Shift+N");
        _globalShortcutDisplay.VerticalAlignment = VerticalAlignment.Center;
        _recordShortcutButton = PanelButton(Strings.Pref_HotkeyRecord, ToggleShortcutRecording);
        _recordShortcutButton.IsEnabled = RecordGlobalShortcutAsync is not null;
        body.Children.Add(PanelRow(_globalShortcutDisplay, _recordShortcutButton));
        body.Children.Add(PanelText(Strings.Pref_HotkeyHint));
        body.Children.Add(PanelSection(Strings.Pref_SectionAbout));
        body.Children.Add(PanelText(string.Format(Strings.Pref_Version, typeof(MainWindow).Assembly.GetName().Version?.ToString(3))));
    }

    private async Task ChangePanelLanguageAsync(string preference)
    {
        if (RestartForLanguageAsync is not { } restart || PanelLanguage.Current == preference)
            return;
        if (!await ConfirmAsync(Strings.Language_RestartTitle, PanelText(Strings.Language_RestartMessage),
                Strings.Language_RestartButton))
            return;

        await PreferenceStore.SetPreferenceAsync("language", preference);
        await restart();
    }

    private void ToggleShortcutRecording()
    {
        if (_recordingGlobalShortcut)
        {
            StopShortcutRecording();
            return;
        }

        if (_recordShortcutButton is null || RecordGlobalShortcutAsync is null)
            return;
        _recordingGlobalShortcut = true;
        _recordShortcutButton.Content = Strings.Pref_HotkeyPressKeys;
        PauseGlobalShortcut?.Invoke(true);
        _recordShortcutButton.Focus();
    }

    private void StopShortcutRecording()
    {
        if (!_recordingGlobalShortcut)
            return;
        _recordingGlobalShortcut = false;
        if (_recordShortcutButton is not null)
            _recordShortcutButton.Content = Strings.Pref_HotkeyRecord;
        PauseGlobalShortcut?.Invoke(false);
    }

    internal void ShowGlobalHotkeyConflict() =>
        ShowInfoToast(string.Format(Strings.Toast_HotkeyConflict, CurrentGlobalShortcut?.Invoke() ?? "Win+Shift+N"));

    private bool HandlePanelShortcutRecording(KeyEventArgs e)
    {
        if (!_recordingGlobalShortcut)
            return false;

        e.Handled = true;
        var modifiers = e.KeyModifiers;
        if (GlobalHotkey.IsWinHeld)
            modifiers |= KeyModifiers.Meta;
        if (e.Key == Key.Escape)
        {
            StopShortcutRecording();
        }
        else if (modifiers != KeyModifiers.None && e.Key is not (
                     Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
                     Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin))
        {
            var record = RecordGlobalShortcutAsync;
            StopShortcutRecording();
            if (record is not null)
            {
                _ = RunPanelActionAsync(async () =>
                {
                    if (!await record(e.Key, modifiers))
                        ShowInfoToast(Strings.Toast_HotkeyInUse);
                    if (_globalShortcutDisplay is not null)
                        _globalShortcutDisplay.Text = CurrentGlobalShortcut?.Invoke();
                });
            }
        }
        return true;
    }
}
