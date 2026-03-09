using System.IO;
using JoJot.Services;

namespace JoJot.Tests.Services;

/// <summary>
/// Additional FileDropService tests targeting uncovered branches:
/// IsBinaryContent edge cases, ProcessDroppedFilesAsync with empty array.
/// </summary>
public class FileDropServiceCoverageTests
{
    public FileDropServiceCoverageTests()
    {
        LogService.InitializeNoop();
    }

    // ─── IsBinaryContent ────────────────────────────────────────────

    [Fact]
    public void IsBinaryContent_DetectsControlBytesInMiddleRange()
    {
        // Bytes 0x0E-0x1F (excluding 0x1B) should be detected as binary
        var buffer = new byte[] { 0x0E };
        FileDropService.IsBinaryContent(buffer, 1).Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContent_AllowsByte0x1F_IsBinary()
    {
        // 0x1F (unit separator) is > 0x0D and < 0x20 and != 0x1B → binary
        var buffer = new byte[] { 0x1F };
        FileDropService.IsBinaryContent(buffer, 1).Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContent_AllowsFormFeed()
    {
        // 0x0C (form feed) is between 0x08 and 0x0D inclusive → allowed
        var buffer = new byte[] { 0x0C };
        FileDropService.IsBinaryContent(buffer, 0x0C == 0 ? 1 : 1).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContent_ZeroBytesRead_ReturnsFalse()
    {
        var buffer = new byte[10];
        FileDropService.IsBinaryContent(buffer, 0).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContent_HighAscii_Allowed()
    {
        // Bytes >= 0x20 are always allowed
        var buffer = new byte[] { 0x20, 0x7E, 0x80, 0xFF };
        FileDropService.IsBinaryContent(buffer, 4).Should().BeFalse();
    }

    // ─── ProcessDroppedFilesAsync ───────────────────────────────────

    [Fact]
    public async Task ProcessDroppedFiles_EmptyArray_ReturnsEmptySummary()
    {
        var summary = await FileDropService.ProcessDroppedFilesAsync([]);

        summary.ValidFiles.Should().BeEmpty();
        summary.ErrorCount.Should().Be(0);
        summary.CombinedErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ProcessDroppedFiles_SingleInvalid_ReturnsErrorDirectly()
    {
        // Nonexistent file
        var summary = await FileDropService.ProcessDroppedFilesAsync(
            ["C:\\nonexistent\\path\\fakefile.txt"]);

        summary.ValidFiles.Should().BeEmpty();
        summary.ErrorCount.Should().Be(1);
        // When no valid files, error message is the last error directly
        summary.CombinedErrorMessage.Should().NotBeNull();
        summary.CombinedErrorMessage.Should().NotContain("file(s) opened");
    }

    [Fact]
    public async Task ValidateFile_EmptyFile_IsValid()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // Empty file (0 bytes) — should pass binary check
            var result = await FileDropService.ValidateFileAsync(tempFile);
            result.IsValid.Should().BeTrue();
            result.Content.Should().Be("");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
