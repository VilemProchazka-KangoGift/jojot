using System.Globalization;
using JoJot.Resources;

namespace JoJot.Services;

/// <summary>
/// Manages application language selection. Sets CurrentUICulture at startup
/// based on the persisted "language" preference. Language changes require a restart.
/// </summary>
public static class LanguageService
{
    /// <summary>
    /// Available application languages.
    /// </summary>
    public enum AppLanguage
    {
        /// <summary>English (default).</summary>
        English,
        /// <summary>Czech (Cesky).</summary>
        Czech
    }

    /// <summary>The current language setting.</summary>
    public static AppLanguage Current { get; private set; } = AppLanguage.English;

    /// <summary>
    /// Parses a stored preference string into an AppLanguage enum.
    /// Returns English for null, empty, or unrecognized values.
    /// </summary>
    internal static AppLanguage ParsePreference(string? saved) => saved switch
    {
        "cs" => AppLanguage.Czech,
        _ => AppLanguage.English
    };

    /// <summary>
    /// Converts an AppLanguage enum to its preference string for storage.
    /// </summary>
    internal static string ToPreferenceString(AppLanguage lang) => lang switch
    {
        AppLanguage.Czech => "cs",
        _ => "en"
    };

    /// <summary>
    /// Initializes the language system. Called once during app startup after the database
    /// is open, before any UI is created. Sets CurrentUICulture so all subsequent
    /// resource lookups resolve to the correct language.
    /// </summary>
    public static async Task InitializeAsync()
    {
        var saved = await PreferenceStore.GetPreferenceAsync("language").ConfigureAwait(false);
        Current = ParsePreference(saved);

        var culture = Current == AppLanguage.Czech
            ? new CultureInfo("cs-CZ")
            : new CultureInfo("en-US");

        // Set the Strings resource class culture directly — this is used by
        // ResourceManager.GetString() regardless of which thread calls it,
        // bypassing thread-local CurrentUICulture entirely.
        Strings.Culture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
    }

    /// <summary>
    /// Persists the language choice and restarts the application to apply it.
    /// </summary>
    public static async Task SetLanguageAsync(AppLanguage lang)
    {
        await PreferenceStore.SetPreferenceAsync("language", ToPreferenceString(lang)).ConfigureAwait(false);
        RestartApp();
    }

    /// <summary>
    /// Restarts the application by launching a new process and exiting the current one.
    /// Releases the single-instance mutex before starting the new process so it can acquire it.
    /// </summary>
    internal static void RestartApp()
    {
        var exePath = Environment.ProcessPath;
        if (exePath is null) return;

        // Release the single-instance mutex so the new process can acquire it
        App.ReleaseSingleInstanceMutex();
        System.Diagnostics.Process.Start(exePath);
        Environment.Exit(0);
    }

    /// <summary>
    /// Selects the correct plural form for Czech (1 / 2-4 / 5+).
    /// English callers can pass the same string for <paramref name="few"/> and <paramref name="other"/>.
    /// </summary>
    /// <param name="one">Singular form (count == 1).</param>
    /// <param name="few">Paucal form (count 2-4, Czech).</param>
    /// <param name="other">General plural form (count 0 or 5+).</param>
    /// <param name="count">The count to select for.</param>
    public static string Plural(string one, string few, string other, int count) =>
        count == 1 ? one : count is >= 2 and <= 4 ? few : other;
}
