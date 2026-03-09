using JoJot.Services;
using System.IO;

namespace JoJot.Tests.Services;

public class FileDropServiceTests
{
    // ─── IsBinaryContent ───────────────────────────────────────────────

    [Fact]
    public void IsBinaryContent_True_WhenNullBytePresent()
    {
        byte[] buffer = [0x48, 0x65, 0x00, 0x6C]; // "He\0l"
        FileDropService.IsBinaryContent(buffer, buffer.Length).Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContent_True_WhenLowControlByte()
    {
        byte[] buffer = [0x48, 0x65, 0x03, 0x6C]; // "He\x03l"
        FileDropService.IsBinaryContent(buffer, buffer.Length).Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContent_False_ForCleanText()
    {
        byte[] buffer = "Hello, world!\n"u8.ToArray();
        FileDropService.IsBinaryContent(buffer, buffer.Length).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContent_False_ForTabsAndNewlines()
    {
        byte[] buffer = "col1\tcol2\nrow1\tcol2\r\n"u8.ToArray();
        FileDropService.IsBinaryContent(buffer, buffer.Length).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContent_False_WhenBufferEmpty()
    {
        FileDropService.IsBinaryContent([], 0).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContent_False_ForEscapeCharacter()
    {
        byte[] buffer = [0x1B, 0x5B, 0x31, 0x6D]; // ESC[1m (ANSI escape)
        FileDropService.IsBinaryContent(buffer, buffer.Length).Should().BeFalse();
    }

    // ─── ValidateFileAsync ─────────────────────────────────────────────

    [Fact]
    public async Task ValidateFileAsync_AcceptsValidTextFile()
    {
        LogService.InitializeNoop();
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "Hello, world!");
            var result = await FileDropService.ValidateFileAsync(path);

            result.IsValid.Should().BeTrue();
            result.Content.Should().Be("Hello, world!");
            result.FileName.Should().Be(Path.GetFileName(path));
            result.ErrorMessage.Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ValidateFileAsync_RejectsTooLargeFile()
    {
        LogService.InitializeNoop();
        var path = Path.GetTempFileName();
        try
        {
            // Write 600KB (over 500KB limit)
            await File.WriteAllBytesAsync(path, new byte[600 * 1024]);
            var result = await FileDropService.ValidateFileAsync(path);

            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("too large");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ValidateFileAsync_RejectsBinaryFile()
    {
        LogService.InitializeNoop();
        var path = Path.GetTempFileName();
        try
        {
            byte[] binary = new byte[100];
            binary[50] = 0x00; // null byte
            await File.WriteAllBytesAsync(path, binary);
            var result = await FileDropService.ValidateFileAsync(path);

            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("binary");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ValidateFileAsync_HandlesNonexistentFile()
    {
        LogService.InitializeNoop();
        var result = await FileDropService.ValidateFileAsync(@"C:\nonexistent_file_12345.txt");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Failed to read");
    }

    // ─── ProcessDroppedFilesAsync ──────────────────────────────────────

    [Fact]
    public async Task ProcessDroppedFiles_MixedResults()
    {
        LogService.InitializeNoop();
        var validPath = Path.GetTempFileName();
        var binaryPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(validPath, "valid content");
            byte[] binary = new byte[100];
            binary[0] = 0x00;
            await File.WriteAllBytesAsync(binaryPath, binary);

            var summary = await FileDropService.ProcessDroppedFilesAsync([validPath, binaryPath]);

            summary.ValidFiles.Should().HaveCount(1);
            summary.ErrorCount.Should().Be(1);
            summary.CombinedErrorMessage.Should().Contain("1 file(s) opened");
            summary.CombinedErrorMessage.Should().Contain("1 skipped");
        }
        finally
        {
            File.Delete(validPath);
            File.Delete(binaryPath);
        }
    }

    [Fact]
    public async Task ProcessDroppedFiles_AllInvalid()
    {
        LogService.InitializeNoop();
        var summary = await FileDropService.ProcessDroppedFilesAsync(
            [@"C:\nonexistent1.txt", @"C:\nonexistent2.txt"]);

        summary.ValidFiles.Should().BeEmpty();
        summary.ErrorCount.Should().Be(2);
        summary.CombinedErrorMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessDroppedFiles_AllValid()
    {
        LogService.InitializeNoop();
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path1, "content1");
            await File.WriteAllTextAsync(path2, "content2");

            var summary = await FileDropService.ProcessDroppedFilesAsync([path1, path2]);

            summary.ValidFiles.Should().HaveCount(2);
            summary.ErrorCount.Should().Be(0);
            summary.CombinedErrorMessage.Should().BeNull();
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }
}
