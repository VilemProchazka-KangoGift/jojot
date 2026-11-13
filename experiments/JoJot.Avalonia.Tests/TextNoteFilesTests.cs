using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using JoJot.Models;
using JoJot.Services;
using Xunit;

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class TextNoteFilesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("jojot-save-").FullName;

    [Fact]
    public async Task EncodingFailureDoesNotCreateDestination()
    {
        var path = Path.Combine(_directory, "new.txt");
        await Assert.ThrowsAsync<EncoderFallbackException>(() =>
            TextNoteFiles.WriteAsync(path, "🐇", TextNoteFiles.GetLegacyEncoding(1252)!, 100));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Fact]
    public async Task BinaryDestinationRemainsUntouched()
    {
        var path = Path.Combine(_directory, "existing.txt");
        byte[] original = [0, 1, 2, 3];
        await File.WriteAllBytesAsync(path, original, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            TextNoteFiles.WriteAsync(path, "replacement", TextNoteFiles.DefaultEncoding, 100));
        Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Single(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Fact]
    public async Task ReadOnlyDestinationRemainsUntouched()
    {
        var path = Path.Combine(_directory, "readonly.txt");
        await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var error = await Record.ExceptionAsync(() =>
                TextNoteFiles.WriteAsync(path, "replacement", TextNoteFiles.DefaultEncoding, 100));
            Assert.True(error is UnauthorizedAccessException or IOException);
            Assert.Equal("original", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Single(Directory.EnumerateFileSystemEntries(_directory));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    [Fact]
    public async Task AConcurrentCreationIsPreserved()
    {
        var path = Path.Combine(_directory, "new.txt");
        await Assert.ThrowsAnyAsync<IOException>(() =>
            TextNoteFiles.WriteAsync(path, "replacement", TextNoteFiles.DefaultEncoding, 100,
                beforeCommit: () => File.WriteAllText(path, "concurrent")));
        Assert.Equal("concurrent", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Single(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Fact]
    public async Task AConcurrentRenameAndReplacementArePreserved()
    {
        var path = Path.Combine(_directory, "existing.txt");
        var previous = Path.Combine(_directory, "previous.txt");
        await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<IOException>(() =>
            TextNoteFiles.WriteAsync(path, "replacement", TextNoteFiles.DefaultEncoding, 100,
                beforeCommit: () =>
                {
                    File.Move(path, previous);
                    File.WriteAllText(path, "concurrent");
                }));
        Assert.Equal("concurrent", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal("original", await File.ReadAllTextAsync(previous, TestContext.Current.CancellationToken));
        Assert.Equal(2, Directory.EnumerateFileSystemEntries(_directory).Count());
    }

    [Fact]
    public async Task ReplacementPreservesCreationTimeAndEncoding()
    {
        var path = Path.Combine(_directory, "existing.txt");
        var encoding = new UnicodeEncoding(false, true, true);
        await File.WriteAllTextAsync(path, "original", encoding, TestContext.Current.CancellationToken);
        File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-1));
        var creationTime = File.GetCreationTimeUtc(path);
        await TextNoteFiles.WriteAsync(path, "replacement", TextNoteFiles.DefaultEncoding, 100);
        Assert.Equal(creationTime, File.GetCreationTimeUtc(path));
        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes("replacement")), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Single(Directory.EnumerateFileSystemEntries(_directory));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

public sealed class FileBaselineStartupTests
{
    [AvaloniaFact]
    public async Task LockedSelectedFileDoesNotPreventInactiveDraftIndicators()
    {
        await using var fixture = new MainWindowTests.WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var selectedPath = Path.Combine(fixture.Directory, "selected.txt");
        var inactivePath = Path.Combine(fixture.Directory, "inactive.txt");
        await File.WriteAllTextAsync(selectedPath, "selected disk text", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(inactivePath, "inactive disk text", TestContext.Current.CancellationToken);
        await NoteStore.InsertNoteAsync("default", "selected", "selected draft", true, 0, selectedPath);
        await NoteStore.InsertNoteAsync("default", "inactive", "inactive draft", false, 1, inactivePath);
        window.Close();
        await WaitForAsync(() => !window.IsVisible);

        using var locked = File.Open(selectedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        window = fixture.Open();
        await window.Ready;
        var list = window.FindControl<ListBox>("NoteList")!;
        Assert.Equal("selected", Assert.IsType<NoteTab>(list.SelectedItem).Name);
        var inactive = Assert.Single(list.Items.OfType<NoteTab>(), note => note.Name == "inactive");
        await WaitForAsync(() => inactive.HasUnsavedFileChanges);
        Assert.Equal("inactive draft", inactive.Content);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }
}
