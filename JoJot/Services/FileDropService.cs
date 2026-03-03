using System.IO;

namespace JoJot.Services
{
    /// <summary>
    /// Phase 9: File drop content inspection and validation service (DROP-01 through DROP-07).
    /// Validates files by size limit and content inspection (binary detection),
    /// not by file extension. Original files are never modified.
    /// </summary>
    public static class FileDropService
    {
        /// <summary>Maximum file size in bytes (500KB).</summary>
        private const long MaxFileSizeBytes = 500 * 1024;

        /// <summary>Number of bytes to read for binary content inspection.</summary>
        private const int InspectionBufferSize = 8192;

        /// <summary>
        /// Result of validating a single dropped file.
        /// </summary>
        public record FileDropResult(bool IsValid, string FileName, string? Content, string? ErrorMessage);

        /// <summary>
        /// Summary of processing multiple dropped files.
        /// </summary>
        public record FileDropSummary(List<FileDropResult> ValidFiles, int ErrorCount, string? CombinedErrorMessage);

        /// <summary>
        /// Checks a byte buffer for binary content indicators (DROP-02).
        /// Returns true if the content appears to be binary (non-text).
        /// Checks for null bytes and non-printable characters, excluding common
        /// text whitespace (tab, line feed, carriage return, escape).
        /// </summary>
        public static bool IsBinaryContent(byte[] buffer, int bytesRead)
        {
            for (int i = 0; i < bytesRead; i++)
            {
                byte b = buffer[i];
                // Null byte is a definitive binary indicator
                if (b == 0) return true;
                // Non-printable characters below tab (0x09)
                if (b < 0x08) return true;
                // Non-printable characters between CR+1 and space, excluding ESC (0x1B)
                if (b > 0x0D && b < 0x20 && b != 0x1B) return true;
            }
            return false;
        }

        /// <summary>
        /// Validates a single file for drop acceptance (DROP-02, DROP-03).
        /// Checks size limit, then inspects content for binary data.
        /// Returns a result with either valid content or an error message.
        /// </summary>
        public static async Task<FileDropResult> ValidateFileAsync(string filePath)
        {
            string fileName = Path.GetFileName(filePath);

            try
            {
                var fileInfo = new FileInfo(filePath);

                // DROP-03: Size limit 500KB checked before content inspection
                if (fileInfo.Length > MaxFileSizeBytes)
                {
                    return new FileDropResult(false, fileName, null, $"'{fileName}' is too large (max 500KB)");
                }

                // DROP-02: Content inspection — read first 8KB for binary check
                byte[] buffer = new byte[Math.Min(InspectionBufferSize, (int)fileInfo.Length)];
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length);
                    if (IsBinaryContent(buffer, bytesRead))
                    {
                        return new FileDropResult(false, fileName, null, $"'{fileName}' contains binary content");
                    }
                }

                // File is valid text — read full content
                string content = await File.ReadAllTextAsync(filePath);
                return new FileDropResult(true, fileName, content, null);
            }
            catch (UnauthorizedAccessException)
            {
                LogService.Error($"File drop: access denied for '{filePath}'");
                return new FileDropResult(false, fileName, null, $"Failed to read '{fileName}'");
            }
            catch (IOException ex)
            {
                LogService.Error($"File drop: IO error for '{filePath}'", ex);
                return new FileDropResult(false, fileName, null, $"Failed to read '{fileName}'");
            }
            catch (Exception ex)
            {
                LogService.Error($"File drop: unexpected error for '{filePath}'", ex);
                return new FileDropResult(false, fileName, null, $"Failed to read '{fileName}'");
            }
        }

        /// <summary>
        /// Processes multiple dropped files, validating each independently (DROP-07).
        /// Invalid files do not block valid ones. Returns a summary with valid files,
        /// error count, and a combined error message for toast display.
        /// </summary>
        public static async Task<FileDropSummary> ProcessDroppedFilesAsync(string[] filePaths)
        {
            var validFiles = new List<FileDropResult>();
            int errorCount = 0;
            string? lastError = null;

            foreach (var path in filePaths)
            {
                var result = await ValidateFileAsync(path);
                if (result.IsValid)
                {
                    validFiles.Add(result);
                }
                else
                {
                    errorCount++;
                    lastError = result.ErrorMessage;
                }
            }

            // Build combined error message for toast display (DROP-06)
            string? combinedMessage = null;
            if (errorCount > 0)
            {
                if (validFiles.Count > 0)
                {
                    combinedMessage = $"{validFiles.Count} file(s) opened, {errorCount} skipped ({lastError})";
                }
                else
                {
                    combinedMessage = lastError;
                }
            }

            return new FileDropSummary(validFiles, errorCount, combinedMessage);
        }
    }
}
