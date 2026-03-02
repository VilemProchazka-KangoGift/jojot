using System.Diagnostics;
using System.IO;

namespace JoJot.Services
{
    /// <summary>
    /// Dual-output logging service: writes to both a log file and System.Diagnostics.Debug.
    /// Thread-safe via a file lock. Log file is rotated when it exceeds 5MB.
    /// </summary>
    public static class LogService
    {
        private static string _logPath = string.Empty;
        private static readonly object _fileLock = new();

        /// <summary>
        /// Initializes the log service, setting the log directory and rolling the file if > 5MB.
        /// Must be called before any logging methods.
        /// </summary>
        /// <param name="directory">Directory where jojot.log will be written.</param>
        public static void Initialize(string directory)
        {
            _logPath = Path.Combine(directory, "jojot.log");

            // Roll if > 5MB: rename to .old (delete previous .old first)
            if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 5 * 1024 * 1024)
            {
                string rolledPath = _logPath + ".old";
                if (File.Exists(rolledPath))
                    File.Delete(rolledPath);
                File.Move(_logPath, rolledPath);
            }
        }

        /// <summary>Logs an informational message.</summary>
        public static void Info(string message) => Write("INFO", message);

        /// <summary>Logs a warning message with optional exception details.</summary>
        public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

        /// <summary>Logs an error message with optional exception details.</summary>
        public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

        private static void Write(string level, string message, Exception? ex = null)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            if (ex is not null)
                line += $"\n  {ex}";

            // Always write to debug output — this is our fallback if file write fails
            Debug.WriteLine(line);

            // Write to file inside lock for thread safety; silent failure if file is unavailable
            lock (_fileLock)
            {
                try
                {
                    File.AppendAllText(_logPath, line + "\n");
                }
                catch
                {
                    // Silent failure — debug output already captured the message above
                }
            }
        }
    }
}
