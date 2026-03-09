using System.IO;
using JoJot.Services;

namespace JoJot.Tests.Services;

/// <summary>
/// FileDropService edge case tests: IsBinaryContent byte boundaries, ValidateFileAsync edge cases.
/// </summary>
public class FileDropServiceEdgeCaseTests
{
    public FileDropServiceEdgeCaseTests()
    {
        LogService.InitializeNoop();
    }

    // ─── IsBinaryContent byte boundaries ───────────────────────────

    [Fact]
    public void IsBinaryContent_NullByte_IsBinary()
    {
        FileDropService.IsBinaryContent([0x00], 1).Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContent_Byte0x01_IsBinary()
    {
        // 0x01 < 0x08 → binary
        FileDropService.IsBinaryContent([0x01], 1).Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContent_Byte0x07_IsBinary()
    {
        // 0x07 < 0x08 → binary (BEL)
        FileDropService.IsBinaryContent([0x07], 1).Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContent_Byte0x08_Allowed()
    {
        // 0x08 (backspace) is >= 0x08 and <= 0x0D → allowed
        FileDropService.IsBinaryContent([0x08], 1).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContent_Byte0x09_Allowed()
    {
        // 0x09 (tab) → allowed
        FileDropService.IsBinaryContent([0x09], 1).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContent_Byte0x0A_Allowed()
    {
        // 0x0A (line feed) → allowed
        FileDropService.IsBinaryContent([0x0A], 1).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContent_Byte0x0D_Allowed()
    {
        // 0x0D (carriage return) → allowed
        FileDropService.IsBinaryContent([0x0D], 1).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContent_Byte0x0E_IsBinary()
    {
        // 0x0E (shift out) is > 0x0D and < 0x20 and != 0x1B → binary
        FileDropService.IsBinaryContent([0x0E], 1).Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContent_Byte0x1A_IsBinary()
    {
        // 0x1A (substitute) is > 0x0D and < 0x20 and != 0x1B → binary
        FileDropService.IsBinaryContent([0x1A], 1).Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContent_Byte0x1B_Allowed()
    {
        // 0x1B (escape) is explicitly excluded → allowed
        FileDropService.IsBinaryContent([0x1B], 1).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContent_Byte0x1C_IsBinary()
    {
        // 0x1C (file separator) is > 0x0D and < 0x20 and != 0x1B → binary
        FileDropService.IsBinaryContent([0x1C], 1).Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContent_Byte0x1F_IsBinary()
    {
        // 0x1F (unit separator) is > 0x0D and < 0x20 and != 0x1B → binary
        FileDropService.IsBinaryContent([0x1F], 1).Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContent_Byte0x20_Allowed()
    {
        // 0x20 (space) is >= 0x20 → allowed
        FileDropService.IsBinaryContent([0x20], 1).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContent_NullByteInMiddle_IsBinary()
    {
        FileDropService.IsBinaryContent([0x41, 0x42, 0x00, 0x43], 4).Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContent_AllPrintableAscii_Allowed()
    {
        var buffer = Enumerable.Range(0x20, 0x7F - 0x20).Select(i => (byte)i).ToArray();
        FileDropService.IsBinaryContent(buffer, buffer.Length).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContent_HighBytes_Allowed()
    {
        // Bytes 0x80-0xFF (extended ASCII / UTF-8 continuation) → allowed
        FileDropService.IsBinaryContent([0x80, 0xC0, 0xFE, 0xFF], 4).Should().BeFalse();
    }

    // ─── ValidateFileAsync ─────────────────────────────────────────

    [Fact]
    public async Task ValidateFile_TextFile_IsValid()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Hello, world!");
            var result = await FileDropService.ValidateFileAsync(tempFile);
            result.IsValid.Should().BeTrue();
            result.Content.Should().Be("Hello, world!");
            result.FileName.Should().Be(Path.GetFileName(tempFile));
            result.ErrorMessage.Should().BeNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ValidateFile_BinaryFile_IsInvalid()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, [0x00, 0x01, 0x02]);
            var result = await FileDropService.ValidateFileAsync(tempFile);
            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("binary content");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ValidateFile_TooLarge_IsInvalid()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // Write > 500KB
            var bigContent = new string('A', 501 * 1024);
            await File.WriteAllTextAsync(tempFile, bigContent);
            var result = await FileDropService.ValidateFileAsync(tempFile);
            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("too large");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ValidateFile_NonexistentFile_IsInvalid()
    {
        var result = await FileDropService.ValidateFileAsync(@"C:\nonexistent\fakefile.txt");
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Failed to read");
    }

    [Fact]
    public async Task ProcessDroppedFiles_MixedValidAndInvalid_ReturnsSummary()
    {
        var validFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(validFile, "Valid content");

            var summary = await FileDropService.ProcessDroppedFilesAsync(
                [validFile, @"C:\nonexistent\fakefile.txt"]);

            summary.ValidFiles.Should().HaveCount(1);
            summary.ErrorCount.Should().Be(1);
            summary.CombinedErrorMessage.Should().Contain("file(s) opened");
            summary.CombinedErrorMessage.Should().Contain("1 skipped");
        }
        finally
        {
            File.Delete(validFile);
        }
    }
}
