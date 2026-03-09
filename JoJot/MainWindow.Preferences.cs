using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── Preferences Panel ──────────────

    /// <summary>
    /// Initializes preferences values from database. Called during tab loading.
    /// Loads font size, debounce interval, and updates UI elements.
    /// </summary>
    public async Task InitializePreferencesAsync()
    {
        // Load font size
        var savedFontSize = await DatabaseService.GetPreferenceAsync("font_size");
        _currentFontSize = int.TryParse(savedFontSize, out var fs) ? Math.Clamp(fs, 8, 32) : 13;
        ContentEditor.FontSize = _currentFontSize;
        FontSizeDisplay.Text = FontSizeToPercent(_currentFontSize);

        // Update theme toggle highlight
        UpdateThemeToggleHighlight(ThemeService.CurrentSetting);

        // Update hotkey display
        HotkeyDisplay.Text = HotkeyService.GetHotkeyDisplayString();
    }

    /// <summary>
    /// Preferences menu click — toggle the slide-in panel.
    /// </summary>
    private void MenuPreferences_Click(object sender, MouseButtonEventArgs e)
    {
        HamburgerMenu.IsOpen = false;
        if (_preferencesOpen)
            HidePreferencesPanel();
        else
            ShowPreferencesPanel();
    }

    private void ShowPreferencesPanel()
    {
        // One-panel-at-a-time — close recovery if open
        if (_recoveryPanelOpen) HideRecoveryPanel();
        if (_cleanupPanelOpen) HideCleanupPanel();

        _preferencesOpen = true;
        PreferencesPanel.Visibility = Visibility.Visible;

        // Refresh values
        FontSizeDisplay.Text = FontSizeToPercent(_currentFontSize);
        UpdateThemeToggleHighlight(ThemeService.CurrentSetting);
        HotkeyDisplay.Text = HotkeyService.GetHotkeyDisplayString();

        // Slide in from right
        var anim = new DoubleAnimation
        {
            From = 300, To = 0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        PrefPanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void HidePreferencesPanel()
    {
        _preferencesOpen = false;
        if (_recordingHotkey)
        {
            _recordingHotkey = false;
            HotkeyRecordText.Text = "Record";
            HotkeyService.ResumeHotkey(); // Re-register if closing during recording
        }

        var anim = new DoubleAnimation
        {
            From = 0, To = 300,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) =>
        {
            PreferencesPanel.Visibility = Visibility.Collapsed;
            PrefPanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
            PrefPanelTransform.X = 300;
        };
        PrefPanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void ClosePreferences_Click(object sender, MouseButtonEventArgs e)
    {
        HidePreferencesPanel();
    }

    // ── Theme toggle handlers ──

    private void ThemeLight_Click(object sender, MouseButtonEventArgs e)
    {
        _ = ThemeService.SetThemeAsync(ThemeService.AppTheme.Light);
        UpdateThemeToggleHighlight(ThemeService.AppTheme.Light);
    }

    private void ThemeSystem_Click(object sender, MouseButtonEventArgs e)
    {
        _ = ThemeService.SetThemeAsync(ThemeService.AppTheme.System);
        UpdateThemeToggleHighlight(ThemeService.AppTheme.System);
    }

    private void ThemeDark_Click(object sender, MouseButtonEventArgs e)
    {
        _ = ThemeService.SetThemeAsync(ThemeService.AppTheme.Dark);
        UpdateThemeToggleHighlight(ThemeService.AppTheme.Dark);
    }

    private void UpdateThemeToggleHighlight(ThemeService.AppTheme active)
    {
        var accentBrush = GetBrush("c-accent");
        var defaultBrush = new SolidColorBrush(System.Windows.Media.Colors.Transparent);

        ThemeLightBtn.Background = active == ThemeService.AppTheme.Light ? accentBrush : defaultBrush;
        ThemeSystemBtn.Background = active == ThemeService.AppTheme.System ? accentBrush : defaultBrush;
        ThemeDarkBtn.Background = active == ThemeService.AppTheme.Dark ? accentBrush : defaultBrush;
    }

    // ── Font size handlers ──

    private void FontSizeIncrease_Click(object sender, MouseButtonEventArgs e) => _ = ChangeFontSizeAsync(1);
    private void FontSizeDecrease_Click(object sender, MouseButtonEventArgs e) => _ = ChangeFontSizeAsync(-1);
    private void FontSizeReset_Click(object sender, MouseButtonEventArgs e) => _ = SetFontSizeAsync(13);

    private async Task ChangeFontSizeAsync(int delta)
    {
        int newSize = Math.Clamp(_currentFontSize + delta, 8, 32);
        await SetFontSizeAsync(newSize);
    }

    private static string FontSizeToPercent(int size) => $"{Math.Round(size * 100.0 / 13)}%";

    private async Task SetFontSizeAsync(int size)
    {
        _currentFontSize = size;
        ContentEditor.FontSize = size;
        FontSizeDisplay.Text = FontSizeToPercent(size);
        await DatabaseService.SetPreferenceAsync("font_size", size.ToString());
        ShowFontSizeTooltip(size);
        // RebuildTabList removed — tab labels use fixed sizes, no rebuild needed
    }

    private void ShowFontSizeTooltip(int size)
    {
        FontSizeTooltipText.Text = FontSizeToPercent(size);
        FontSizeTooltip.Visibility = Visibility.Visible;

        _fontSizeTooltipTimer?.Stop();
        _fontSizeTooltipTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _fontSizeTooltipTimer.Tick += (_, _) =>
        {
            FontSizeTooltip.Visibility = Visibility.Collapsed;
            _fontSizeTooltipTimer.Stop();
        };
        _fontSizeTooltipTimer.Start();
    }

    // DebounceInput_TextChanged removed (autosave delay no longer user-configurable)

    // ── Hotkey picker ──

    private void HotkeyRecord_Click(object sender, MouseButtonEventArgs e)
    {
        if (_recordingHotkey)
        {
            // Cancel recording — re-register the original hotkey
            _recordingHotkey = false;
            HotkeyRecordText.Text = "Record";
            HotkeyService.ResumeHotkey();
        }
        else
        {
            // Start recording — unregister so the key combo doesn't trigger the hotkey
            HotkeyService.PauseHotkey();
            _recordingHotkey = true;
            HotkeyRecordText.Text = "Press keys...";
        }
    }
}
