namespace JoJot.Services;

/// <summary>
/// Orchestrates startup-time operations: welcome tab creation on first launch,
/// and background schema migrations after the window is shown.
/// </summary>
public static class StartupService
{
    /// <summary>
    /// Inserts a "Welcome to JoJot" note on the very first launch (when the notes table is empty).
    /// Runs synchronously in the startup path so the welcome tab is visible immediately.
    /// </summary>
    public static async Task CreateWelcomeTabIfFirstLaunch()
    {
        long count = await DatabaseCore.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM notes;").ConfigureAwait(false);
        if (count > 0)
        {
            return;
        }

        LogService.Info("First launch detected — creating welcome tab");

        string welcomeContent =
            "Welcome to JoJot!\n\n" +
            "JoJot is a lightweight note-taking app that ties your notes to your virtual desktops. " +
            "Switch to a different desktop and JoJot automatically shows the notes you left there — " +
            "zero friction, always in context.\n\n" +
            "A few things to get you started:\n" +
            "  • Ctrl+T  — open a new tab\n" +
            "  • Ctrl+W  — close the current tab\n" +
            "  \u2022 Closing the window keeps JoJot running in the background \u2014 relaunch to get it back.\n\n" +
            "Feel free to delete this tab once you're ready.";

        await DatabaseCore.ExecuteNonQueryAsync(
            $"INSERT INTO notes (desktop_guid, name, content, pinned, sort_order) " +
            $"VALUES ('{EscapeSql(VirtualDesktopService.CurrentDesktopGuid)}', 'Welcome to JoJot', '{EscapeSql(welcomeContent)}', 0, 0);").ConfigureAwait(false);
    }

    /// <summary>
    /// Runs pending schema migrations in the background.
    /// Never throws — failures are logged and silently swallowed so they never block or crash the app.
    /// Retry will happen on next launch.
    /// </summary>
    public static async Task RunBackgroundMigrationsAsync()
    {
        try
        {
            await DatabaseCore.RunPendingMigrationsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("Background migration failed", ex);
        }
    }

    /// <summary>
    /// Escapes single-quotes for inline SQL string literals.
    /// Prefer parameterized queries in future callers — this helper is only
    /// used here for the one-time welcome content insertion.
    /// </summary>
    internal static string EscapeSql(string value) => value.Replace("'", "''");
}
