namespace JoJot.Services
{
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
            long count = await DatabaseService.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM notes;");
            if (count > 0)
                return;

            LogService.Info("First launch detected — creating welcome tab");

            const string welcomeContent =
                "Welcome to JoJot!\n\n" +
                "JoJot is a lightweight note-taking app that ties your notes to your virtual desktops. " +
                "Switch to a different desktop and JoJot automatically shows the notes you left there — " +
                "zero friction, always in context.\n\n" +
                "A few things to get you started:\n" +
                "  • Ctrl+T  — open a new tab\n" +
                "  • Ctrl+W  — close the current tab\n" +
                "  • Closing the window hides JoJot (the process stays running in the background).\n\n" +
                "Feel free to delete this tab once you're ready.";

            await DatabaseService.ExecuteNonQueryAsync(
                $"INSERT INTO notes (desktop_guid, name, content, pinned, sort_order) " +
                $"VALUES ('default', 'Welcome to JoJot', '{EscapeSql(welcomeContent)}', 0, 0);");
        }

        /// <summary>
        /// Runs pending schema migrations in the background.
        /// Never throws — failures are logged and silently swallowed so they never block or crash the app.
        /// Per design decision: log and continue; retry will happen on next launch.
        /// </summary>
        public static async Task RunBackgroundMigrationsAsync()
        {
            try
            {
                await DatabaseService.RunPendingMigrationsAsync();
            }
            catch (Exception ex)
            {
                LogService.Error("Background migration failed", ex);
                // Never rethrow — per design decision: log and continue, retry next launch
            }
        }

        // ─── Private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Escapes single-quotes for inline SQL string literals.
        /// Note: prefer parameterized queries in future callers — this helper is only
        /// used here for the one-time welcome content insertion.
        /// </summary>
        private static string EscapeSql(string value) => value.Replace("'", "''");
    }
}
