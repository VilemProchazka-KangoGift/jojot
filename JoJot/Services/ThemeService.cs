using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JoJot.Services;

/// <summary>
/// Manages Light, Dark, and System theme switching via ResourceDictionary swap.
/// Reads/writes the "theme" key in the preferences table for persistence.
/// System mode auto-follows Windows dark/light via SystemEvents.UserPreferenceChanged.
/// </summary>
public static partial class ThemeService
{
    // ─── DWM interop for title bar dark mode ─────────────────────────────
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private static readonly List<WeakReference<Window>> _trackedWindows = [];

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

    /// <summary>
    /// Raised after a theme is applied (user-initiated or system auto-follow).
    /// Subscribers should invalidate any cached theme-dependent visuals.
    /// </summary>
    public static event EventHandler? ThemeChanged;

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

        ApplyTitleBarToAllWindows();
        ThemeChanged?.Invoke(null, EventArgs.Empty);
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

    // ─── Title bar dark mode ────────────────────────────────────────────────

    /// <summary>
    /// Registers a window for title bar dark mode tracking. Applies the current theme
    /// to the title bar immediately (or deferred via SourceInitialized if the HWND
    /// is not yet available).
    /// </summary>
    public static void RegisterWindow(Window window)
    {
        _trackedWindows.Add(new WeakReference<Window>(window));

        var helper = new WindowInteropHelper(window);
        if (helper.Handle == IntPtr.Zero)
        {
            // HWND not yet created — defer until the native window is initialized
            window.SourceInitialized += (_, _) => ApplyTitleBarToWindow(window);
        }
        else
        {
            ApplyTitleBarToWindow(window);
        }
    }

    /// <summary>
    /// Applies the DWM immersive dark mode attribute to a single window's title bar.
    /// </summary>
    private static void ApplyTitleBarToWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var effective = _currentSetting == AppTheme.System ? DetectSystemTheme() : _currentSetting;
        int useDarkMode = effective == AppTheme.Dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
    }

    /// <summary>
    /// Applies the current title bar theme to all tracked windows, pruning dead references.
    /// </summary>
    private static void ApplyTitleBarToAllWindows()
    {
        for (int i = _trackedWindows.Count - 1; i >= 0; i--)
        {
            if (_trackedWindows[i].TryGetTarget(out var window))
            {
                ApplyTitleBarToWindow(window);
            }
            else
            {
                _trackedWindows.RemoveAt(i);
            }
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
