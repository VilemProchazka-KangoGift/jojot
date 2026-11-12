using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using JoJot.Models;
using JoJot.Services;
using Microsoft.Data.Sqlite;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(JoJot.AvaloniaExperiment.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ExperimentApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public async Task EditSwitchCloseAndReopenPersistTheSelectedNotes()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        var editor = Find<TextBox>(window, "Editor");
        var list = Find<ListBox>(window, "NoteList");
        var add = Find<Button>(window, "AddButton");
        await WaitForAsync(() => add.IsEnabled, "storage initialized");
        Assert.False(editor.IsEnabled);

        Click(add);
        await WaitForAsync(() => list.ItemCount == 1 && editor.IsEnabled, "first note created");
        var first = Assert.IsType<NoteTab>(list.SelectedItem);
        Assert.Equal(JoJot.Resources.Strings.Date_NewNote, first.DisplayLabel);
        Assert.Equal(first.DisplayLabel, Find<TextBlock>(window, "NoteTitle").Text);
        editor.Text = "Debounced <first> & note";
        await WaitForAsync(() => ReadContent(fixture.Directory, first.Id) == editor.Text, "debounced database write");

        Click(add);
        await WaitForAsync(() => list.ItemCount == 2 && editor.IsEnabled, "second note created");
        var second = Assert.IsType<NoteTab>(list.SelectedItem);
        editor.Text = "Saved by switching tabs";
        list.SelectedItem = first;
        await WaitForAsync(() => editor.Text == "Debounced <first> & note" && add.IsEnabled, "first note selected");
        Assert.Equal("Saved by switching tabs", ReadContent(fixture.Directory, second.Id));

        Click(Find<Button>(window, "RenameButton"));
        await WaitForAsync(() => window.OwnedWindows.Count == 1, "rename dialog");
        var rename = Assert.Single(window.OwnedWindows);
        var renamePanel = Assert.IsType<StackPanel>(rename.Content);
        Assert.IsType<TextBox>(renamePanel.Children[0]).Text = "Renamed note";
        Click(Assert.IsType<Button>(Assert.IsType<StackPanel>(renamePanel.Children[1]).Children[1]));
        await WaitForAsync(() => first.Name == "Renamed note" && add.IsEnabled, "rename saved");

        Click(Find<Button>(window, "PinButton"));
        await WaitForAsync(() => first.Pinned && add.IsEnabled, "pin saved");
        Assert.Equal("Unpin", Find<Button>(window, "PinButton").Content);

        var search = Find<TextBox>(window, "SearchBox");
        search.Text = "switching";
        await WaitForAsync(() => list.ItemCount == 1, "content filtered");
        Assert.Same(second, Assert.Single(list.Items));
        search.Text = "";
        await WaitForAsync(() => list.ItemCount == 2, "filter cleared");

        var theme = Find<ComboBox>(window, "ThemePicker");
        theme.SelectedIndex = 2;
        await WaitForAsync(() => window.RequestedThemeVariant == ThemeVariant.Dark && add.IsEnabled, "dark theme saved");
        editor.Text = "Saved by immediate close";
        window.Close();
        await WaitForAsync(() => !window.IsVisible, "window closed");
        Assert.Equal("Saved by immediate close", ReadContent(fixture.Directory, first.Id));

        window = fixture.Open();
        editor = Find<TextBox>(window, "Editor");
        list = Find<ListBox>(window, "NoteList");
        await WaitForAsync(() => editor.IsEffectivelyEnabled, "stored notes loaded");
        first = Assert.IsType<NoteTab>(list.SelectedItem);
        Assert.Equal("Renamed note", first.Name);
        Assert.True(first.Pinned);
        Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant);
        Assert.Equal("Saved by immediate close", editor.Text);

        list.SelectedItem = Assert.Single(list.Items.OfType<NoteTab>(), note => note.Id == second.Id);
        await WaitForAsync(() => editor.Text == "Saved by switching tabs", "second note selected after reopen");
        Click(Find<Button>(window, "DeleteButton"));
        await WaitForAsync(() => window.OwnedWindows.Count == 1, "delete dialog");
        var delete = Assert.Single(window.OwnedWindows);
        var deletePanel = Assert.IsType<StackPanel>(delete.Content);
        Click(Assert.IsType<Button>(Assert.IsType<StackPanel>(deletePanel.Children[1]).Children[1]));
        await WaitForAsync(() => list.ItemCount == 1, "note removed");
        Assert.Null(ReadContent(fixture.Directory, second.Id));
        window.Close();
        await WaitForAsync(() => !window.IsVisible, "reopened window closed");
    }

    [AvaloniaFact]
    public async Task CloseRequestedDuringStartupIsReplayedAfterStorageInitializes()
    {
        await using var fixture = new WindowFixture();
        var window = fixture.Open();
        Assert.False(Find<Button>(window, "AddButton").IsEnabled);
        window.Close();
        await WaitForAsync(() => !window.IsVisible, "queued startup close");
        Assert.True(File.Exists(Path.Combine(fixture.Directory, ".clean-shutdown")));
        using var file = File.Open(Path.Combine(fixture.Directory, ".experiment-lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static T Find<T>(Window window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control {name}.");

    private static void Click(Button button)
    {
        Assert.True(button.IsEffectivelyEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }

    private static string? ReadContent(string directory, long id)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "jojot.db"),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT content FROM notes WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() as string;
    }

    private static async Task WaitForAsync(Func<bool> predicate, string description)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            try { await Task.Delay(15, timeout.Token); }
            catch (OperationCanceledException) { Assert.Fail($"Timed out waiting for {description}."); }
        }
    }

    private sealed class WindowFixture : IAsyncDisposable
    {
        private MainWindow? _window;
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "jojot-avalonia-test-" + Guid.NewGuid().ToString("N"));

        public MainWindow Open()
        {
            ExperimentApp.Options = ExperimentOptions.Parse(["--data-dir", Directory]);
            ExperimentApp.Benchmark = null;
            _window = new MainWindow();
            _window.Show();
            return _window;
        }

        public async ValueTask DisposeAsync()
        {
            if (_window is { IsVisible: true } window)
            {
                foreach (var dialog in window.OwnedWindows.ToArray())
                    dialog.Close();
                window.Close();
                await WaitForAsync(() => !window.IsVisible, "test window cleanup");
            }
            SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}

public sealed class ExperimentOptionsTests
{
    [Theory]
    [InlineData("jojot.db")]
    [InlineData("JOJOT.DB-WAL")]
    [InlineData("jojot.db-shm")]
    [InlineData(".clean-shutdown")]
    [InlineData(".experiment-lock")]
    [InlineData("startup-cache.json")]
    public void RejectsBenchmarkOutputAtReservedDatabasePath(string fileName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "av-experiment");
        Assert.Throws<ArgumentException>(() =>
            ExperimentOptions.Parse(["--data-dir", directory, "--benchmark-output", Path.Combine(directory, fileName)]));
    }

    [Theory]
    [InlineData("JoJot")]
    [InlineData("JoJot.Dev")]
    public void RejectsInstalledJoJotData(string folder)
    {
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folder);
        Assert.Throws<ArgumentException>(() => ExperimentOptions.Parse(["--data-dir", installed]));
        Assert.Throws<ArgumentException>(() => ExperimentOptions.Parse(["--benchmark-output", Path.Combine(installed, "result.json")]));
    }
}
