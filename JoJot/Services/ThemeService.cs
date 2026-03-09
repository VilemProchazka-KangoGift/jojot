using System.Windows;

namespace JoJot.Services;

/// <summary>
/// Manages Light, Dark, and System theme switching via ResourceDictionary swap.
/// Reads/writes the "theme" key in the preferences table for persistence.
/// System mode auto-follows Windows dark/light via SystemEvents.UserPreferenceChanged.
/// </summary>
public static class ThemeService
{
    /// <summary>
    /// Available application theme settings.
    /// </summary>
    public enum AppTheme
    {
        /// <summary>Light theme.</summary>
        Light,
        /// <summary>Dark theme.</summary>
        Dark,
        /// <summary>Follows the Windows system setting.</summary>
        System
    }

    private static AppTheme _currentSetting = AppTheme.System;
    private static bool _initialized;

    /// <summary>The user's chosen theme setting (Light, Dark, or System).</summary>
    public static AppTheme CurrentSetting => _currentSetting;

    /// <summary>
    /// Parses a stored preference string into an AppTheme enum.
    /// Returns System for null, empty, or unrecognized values.
    /// </summary>
    internal static AppTheme ParseThemePreference(string? saved) => saved switch
    {
        "light" => AppTheme.Light,
        "dark" => AppTheme.Dark,
        _ => AppTheme.System
    };

    /// <summary>
    /// Converts an AppTheme enum to its preference string for storage.
    /// </summary>
    internal static string ThemeToPreferenceString(AppTheme theme) => theme switch
    {
        AppTheme.Light => "light",
        AppTheme.Dark => "dark",
        _ => "system"
    };

    /// <summary>
    /// Initializes the theme system. Called once during app startup after the database
    /// is open and schema is ensured. Reads the persisted theme preference, applies the
    /// initial theme, and wires the system theme change listener for auto-follow.
    /// </summary>
    public static async Task InitializeAsync()
    {
        var saved = await PreferenceStore.GetPreferenceAsync("theme").ConfigureAwait(false);
        _currentSetting = ParseThemePreference(saved);

        // Must apply theme on the UI thread
        ApplyTheme(_currentSetting);

        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        _initialized = true;
    }

    /// <summary>
    /// Applies the specified theme by swapping the first MergedDictionary entry.
    /// If theme is System, detects the current Windows dark/light setting.
    /// </summary>
    /// <param name="theme">The theme to apply.</param>
    public static void ApplyTheme(AppTheme theme)
    {
        _currentSetting = theme;
        var effective = theme == AppTheme.System ? DetectSystemTheme() : theme;

        var dictionaries = Application.Current.Resources.MergedDictionaries;

        if (dictionaries.Count > 0 &&
            dictionaries[0].Source?.OriginalString.Contains("Theme.xaml") == true)
        {
            dictionaries.RemoveAt(0);
        }

        var uri = effective == AppTheme.Dark
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

        dictionaries.Insert(0, new ResourceDictionary { Source = uri });
    }

    /// <summary>
    /// Sets the theme and persists the choice to the preferences table.
    /// </summary>
    /// <param name="theme">The theme to set and persist.</param>
    public static async Task SetThemeAsync(AppTheme theme)
    {
        ApplyTheme(theme);
        await PreferenceStore.SetPreferenceAsync("theme", ThemeToPreferenceString(theme)).ConfigureAwait(false);
    }

    /// <summary>
    /// Detects the current Windows dark/light mode by reading the registry.
    /// AppsUseLightTheme: 0 = dark, 1 = light.
    /// </summary>
    private static AppTheme DetectSystemTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch
        {
            // Intentional silent catch: registry read may fail on restricted accounts
            // or unusual Windows configurations. Default to light theme.
            return AppTheme.Light;
        }
    }

    /// <summary>
    /// Handles Windows system preference changes. Re-applies theme when in System mode
    /// and the user changes the Windows dark/light setting.
    /// Always dispatches to UI thread since the event may fire from a background thread.
    /// </summary>
    private static void OnSystemPreferenceChanged(
        object sender,
        Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General &&
            _currentSetting == AppTheme.System)
        {
            Application.Current.Dispatcher.InvokeAsync(() => ApplyTheme(AppTheme.System));
        }
    }

    /// <summary>
    /// Unsubscribes the system event handler. Called from App.OnExit to prevent
    /// a static event handler leak.
    /// </summary>
    public static void Shutdown()
    {
        if (_initialized)
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        }
    }
}
