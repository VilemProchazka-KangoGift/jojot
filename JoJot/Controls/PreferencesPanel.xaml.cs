using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JoJot.Services;
using JoJot.Themes;

namespace JoJot.Controls;

public partial class PreferencesPanel : UserControl
{
    private bool _recordingHotkey;

    public event EventHandler? CloseRequested;
    public event EventHandler<ThemeService.AppTheme>? ThemeChangeRequested;
    public event EventHandler<int>? FontSizeChangeRequested;
    public event EventHandler? FontSizeResetRequested;
    public event EventHandler<bool>? HotkeyRecordingChanged;
    public event EventHandler<string>? AutoDeleteDaysChanged;

    public bool IsRecordingHotkey => _recordingHotkey;

    public PreferencesPanel()
    {
        InitializeComponent();
    }

    public void Show()
    {
        Visibility = Visibility.Visible;
        var anim = new DoubleAnimation
        {
            From = 300, To = 0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        PanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    public void Hide()
    {
        if (_recordingHotkey)
        {
            _recordingHotkey = false;
            HotkeyRecordText.Text = "Record";
            HotkeyService.StopRecordingMode();
        }

        var anim = new DoubleAnimation
        {
            From = 0, To = 300,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            PanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
            PanelTransform.X = 300;
        };
        PanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    public void RefreshValues(int fontSize, ThemeService.AppTheme theme, string hotkeyDisplay, string? autoDeleteDays = null)
    {
        FontSizeDisplay.Text = FontSizeToPercent(fontSize);
        UpdateThemeToggleHighlight(theme);
        HotkeyDisplay.Text = hotkeyDisplay;
        _suppressAutoDeleteEvent = true;
        AutoDeleteDaysInput.Text = autoDeleteDays ?? "";
        _suppressAutoDeleteEvent = false;
    }

    private bool _suppressAutoDeleteEvent;

    public void UpdateFontSizeDisplay(int fontSize)
    {
        FontSizeDisplay.Text = FontSizeToPercent(fontSize);
    }

    public void UpdateHotkeyDisplay(string hotkeyDisplay)
    {
        HotkeyDisplay.Text = hotkeyDisplay;
    }

    /// <summary>
    /// Resets recording state without raising an event. Called externally
    /// after MainWindow.Keyboard captures a hotkey combination.
    /// </summary>
    public void StopRecording()
    {
        _recordingHotkey = false;
        HotkeyRecordText.Text = "Record";
        HotkeyService.StopRecordingMode();
    }

    internal static string FontSizeToPercent(int size) => $"{Math.Round(size * 100.0 / 13)}%";

    private void UpdateThemeToggleHighlight(ThemeService.AppTheme active)
    {
        var accentBrush = (SolidColorBrush)FindResource(ThemeKeys.Accent);
        var defaultBrush = new SolidColorBrush(Colors.Transparent);

        ThemeLightBtn.Background = active == ThemeService.AppTheme.Light ? accentBrush : defaultBrush;
        ThemeSystemBtn.Background = active == ThemeService.AppTheme.System ? accentBrush : defaultBrush;
        ThemeDarkBtn.Background = active == ThemeService.AppTheme.Dark ? accentBrush : defaultBrush;
    }

    // ── Click handlers ──

    private void Close_Click(object sender, MouseButtonEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ThemeLight_Click(object sender, MouseButtonEventArgs e)
    {
        UpdateThemeToggleHighlight(ThemeService.AppTheme.Light);
        ThemeChangeRequested?.Invoke(this, ThemeService.AppTheme.Light);
    }

    private void ThemeSystem_Click(object sender, MouseButtonEventArgs e)
    {
        UpdateThemeToggleHighlight(ThemeService.AppTheme.System);
        ThemeChangeRequested?.Invoke(this, ThemeService.AppTheme.System);
    }

    private void ThemeDark_Click(object sender, MouseButtonEventArgs e)
    {
        UpdateThemeToggleHighlight(ThemeService.AppTheme.Dark);
        ThemeChangeRequested?.Invoke(this, ThemeService.AppTheme.Dark);
    }

    private void FontSizeIncrease_Click(object sender, MouseButtonEventArgs e)
    {
        FontSizeChangeRequested?.Invoke(this, 1);
    }

    private void FontSizeDecrease_Click(object sender, MouseButtonEventArgs e)
    {
        FontSizeChangeRequested?.Invoke(this, -1);
    }

    private void FontSizeReset_Click(object sender, MouseButtonEventArgs e)
    {
        FontSizeResetRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HotkeyRecord_Click(object sender, MouseButtonEventArgs e)
    {
        if (_recordingHotkey)
        {
            _recordingHotkey = false;
            HotkeyRecordText.Text = "Record";
            HotkeyService.StopRecordingMode();
            HotkeyRecordingChanged?.Invoke(this, false);
        }
        else
        {
            _recordingHotkey = true;
            HotkeyRecordText.Text = "Press keys...";
            HotkeyService.StartRecordingMode();
            HotkeyRecordingChanged?.Invoke(this, true);
        }
    }

    private void AutoDeleteDays_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (char c in e.Text)
        {
            if (!char.IsDigit(c)) { e.Handled = true; return; }
        }
    }

    private void AutoDeleteDays_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressAutoDeleteEvent) return;
        AutoDeleteDaysChanged?.Invoke(this, AutoDeleteDaysInput.Text);
    }
}
