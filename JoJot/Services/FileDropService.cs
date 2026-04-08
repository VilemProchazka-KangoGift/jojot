using System.IO;
using JoJot.Resources;

namespace JoJot.Services;

/// <summary>
/// Validates and reads files dropped onto the application. Files are checked by size limit
/// and content inspection (binary detection), not by extension. Original files are never modified.
/// </summary>
public static class FileDropService
{
    /// <summary>Maximum file size in bytes (500 KB).</summary>
    private const long MaxFileSizeBytes = 500 * 1024;

    /// <summary>Number of bytes to read for binary content inspection.</summary>
    private const int InspectionBufferSize = 8192;

    /// <summary>
    /// Result of validating a single dropped file.
    /// </summary>
    /// <param name="IsValid">Whether the file passed validation.</param>
    /// <param name="FileName">The file name (without path).</param>
    /// <param name="FilePath">Absolute file path; <c>null</c> on error.</param>
    /// <param name="Content">The text content if valid; <c>null</c> otherwise.</param>
    /// <param name="ErrorMessage">Describes why validation failed; <c>null</c> if valid.</param>
    public record FileDropResult(bool IsValid, string FileName, string? FilePath, string? Content, string? ErrorMessage);

    /// <summary>
    /// Summary of processing multiple dropped files.
    /// </summary>
    /// <param name="ValidFiles">Files that passed validation.</param>
    /// <param name="ErrorCount">Number of files that failed validation.</param>
    /// <param name="CombinedErrorMessage">Aggregate error message for toast display; <c>null</c> if no errors.</param>
    public record FileDropSummary(List<FileDropResult> ValidFiles, int ErrorCount, string? CombinedErrorMessage);

    /// <summary>
    /// Checks a byte buffer for binary content indicators.
    /// Returns <c>true</c> if the content appears to be binary (non-text).
    /// Looks for null bytes and non-printable characters, excluding common text
    /// whitespace (tab, line feed, carriage return, escape).
    /// </summary>
    /// <param name="buffer">The byte buffer to inspect.</param>
    /// <param name="bytesRead">Number of valid bytes in the buffer.</param>
    public static bool IsBinaryContent(byte[] buffer, int bytesRead)
    {
        for (int i = 0; i < bytesRead; i++)
        {
            byte b = buffer[i];
            if (b == 0)
            {
                return true;
            }

            if (b < 0x08)
            {
                return true;
            }

            if (b > 0x0D && b < 0x20 && b != 0x1B)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Validates a single file for drop acceptance. Checks the size limit first,
    /// then inspects content for binary data.
    /// Returns a result with either valid content or an error message.
    /// </summary>
    /// <param name="filePath">Absolute path to the file to validate.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    public static async Task<FileDropResult> ValidateFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fileName = Path.GetFileName(filePath);

        try
        {
            var fileInfo = new FileInfo(filePath);

            if (fileInfo.Length > MaxFileSizeBytes)
            {
                return new FileDropResult(false, fileName, null, null, string.Format(Strings.Drop_TooLarge, fileName));
            }

            // Content inspection: read first 8 KB for binary check
            var buffer = new byte[Math.Min(InspectionBufferSize, (int)fileInfo.Length)];
            await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int bytesRead = await fs.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (IsBinaryContent(buffer, bytesRead))
                {
                    return new FileDropResult(false, fileName, null, null, string.Format(Strings.Drop_Binary, fileName));
                }
            }

            var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return new FileDropResult(true, fileName, filePath, content, null);
        }
        catch (UnauthorizedAccessException)
        {
            LogService.Error("File drop: access denied for {FilePath}", filePath);
            return new FileDropResult(false, fileName, null, null, string.Format(Strings.Drop_ReadFailed, fileName));
        }
        catch (IOException ex)
        {
            LogService.Error("File drop: IO error for {FilePath}", filePath, ex);
            return new FileDropResult(false, fileName, null, null, string.Format(Strings.Drop_ReadFailed, fileName));
        }
        catch (Exception ex)
        {
            LogService.Error("File drop: unexpected error for {FilePath}", filePath, ex);
            return new FileDropResult(false, fileName, null, null, string.Format(Strings.Drop_ReadFailed, fileName));
        }
    }

    /// <summary>
    /// Processes multiple dropped files, validating each independently. Invalid files do not
    /// block valid ones. Returns a summary with valid files, error count, and a combined
    /// error message suitable for toast display.
    /// </summary>
    /// <param name="filePaths">Array of file paths to process.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    public static async Task<FileDropSummary> ProcessDroppedFilesAsync(string[] filePaths, CancellationToken cancellationToken = default)
    {
        List<FileDropResult> validFiles = [];
        int errorCount = 0;
        string? lastError = null;

        foreach (var path in filePaths)
        {
            var result = await ValidateFileAsync(path, cancellationToken).ConfigureAwait(false);
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

        string? combinedMessage = null;
        if (errorCount > 0)
        {
            if (validFiles.Count > 0)
            {
                combinedMessage = string.Format(Strings.Drop_PartialSuccess, validFiles.Count, errorCount, lastError);
            }
            else
            {
                combinedMessage = lastError;
            }
        }

        return new FileDropSummary(validFiles, errorCount, combinedMessage);
    }
}
