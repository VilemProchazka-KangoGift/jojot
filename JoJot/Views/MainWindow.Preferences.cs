using System.Windows.Input;
using JoJot.Controls;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── Preferences Panel ──────────────

    public async Task InitializePreferencesAsync()
    {
        var savedFontSize = await PreferenceStore.GetPreferenceAsync("font_size");
        _currentFontSize = ViewModels.MainWindowViewModel.ParseFontSize(savedFontSize);
        ContentEditor.FontSize = _currentFontSize;
        PreferencesPanel.RefreshValues(_currentFontSize, ThemeService.CurrentSetting, HotkeyService.GetHotkeyDisplayString());
    }

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
        if (_recoveryPanelOpen) HideRecoveryPanel();
        if (_cleanupPanelOpen) HideCleanupPanel();
        if (_findPanelOpen) HideFindPanel();

        _preferencesOpen = true;
        PreferencesPanel.RefreshValues(_currentFontSize, ThemeService.CurrentSetting, HotkeyService.GetHotkeyDisplayString());
        PreferencesPanel.Show();
    }

    private void HidePreferencesPanel()
    {
        _preferencesOpen = false;
        if (PreferencesPanel.IsRecordingHotkey)
            HotkeyService.ResumeHotkey();
        PreferencesPanel.Hide();
    }

    // ── Font size logic ──

    private async Task ChangeFontSizeAsync(int delta)
    {
        int newSize = ViewModels.MainWindowViewModel.ClampFontSize(_currentFontSize, delta);
        await SetFontSizeAsync(newSize);
    }

    private async Task SetFontSizeAsync(int size)
    {
        _currentFontSize = size;
        ContentEditor.FontSize = size;
        PreferencesPanel.UpdateFontSizeDisplay(size);
        await PreferenceStore.SetPreferenceAsync("font_size", size.ToString());
        ShowFontSizeTooltip(size);
    }

    private void ShowFontSizeTooltip(int size)
    {
        FontSizeTooltipText.Text = PreferencesPanel.FontSizeToPercent(size);
        FontSizeTooltip.Visibility = System.Windows.Visibility.Visible;

        if (_fontSizeTooltipTimer is null)
        {
            _fontSizeTooltipTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _fontSizeTooltipTimer.Tick += (_, _) =>
            {
                FontSizeTooltip.Visibility = System.Windows.Visibility.Collapsed;
                _fontSizeTooltipTimer.Stop();
            };
        }
        else
        {
            _fontSizeTooltipTimer.Stop();
        }

        _fontSizeTooltipTimer.Start();
    }
}
