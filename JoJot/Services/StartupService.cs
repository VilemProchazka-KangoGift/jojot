using JoJot.Resources;

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

        string welcomeContent = Strings.Welcome_Content;

        await DatabaseCore.ExecuteNonQueryAsync(
            "INSERT INTO notes (desktop_guid, name, content, pinned, sort_order) VALUES (@guid, @name, @content, 0, 0);",
            ("@guid", VirtualDesktopService.CurrentDesktopGuid),
            ("@name", Strings.Welcome_Title),
            ("@content", welcomeContent)).ConfigureAwait(false);
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
}
