using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JoJot.Resources;
using JoJot.Services;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow
{
    private async Task CaptureVisualProbeAsync(string directory)
    {
        _options.ValidateDataPath();
        Directory.CreateDirectory(directory);
        var previousCulture = PanelLanguage.Current;
        var previousTheme = RequestedThemeVariant;
        var previousOrphans = OrphanSessionGuids;
        var previousWidth = Width;
        string? createdOrphan = null;
        try
        {
            SetVisualLanguage("en");
            if (_notes.Count == 0 || _active is null)
                throw new InvalidOperationException("Visual capture requires a populated disposable note fixture.");
            if (_notes.FirstOrDefault(note => note.Name == Strings.Welcome_Title) is { } welcome)
            {
                ShowNote(welcome);
                RefreshNoteList();
            }
            ApplyTheme("light");
            await CaptureWindowAsync(directory, "main-light.png");
            ApplyTheme("dark");
            await CaptureWindowAsync(directory, "main-dark.png");
            ApplyTheme("light");

            ShowFindPanel();
            if (_findQuery is { } query && Editor.Text is { Length: > 0 } editorText)
            {
                var word = editorText.Contains("note", StringComparison.OrdinalIgnoreCase)
                    ? "note"
                    : editorText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (word is { Length: > 0 })
                    query.Text = word[..Math.Min(word.Length, 24)];
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                _findTimer.Stop();
                await RunFindAsync();
                if (_findMatches.Count == 0)
                    throw new InvalidOperationException("The visual probe did not find its selected search term.");
            }
            await CaptureWindowAsync(directory, "find.png");
            HideFindPanel();

            await ShowPreferencesAsync();
            await CaptureWindowAsync(directory, "preferences.png");
            ApplyTheme("dark");
            await CaptureWindowAsync(directory, "preferences-dark.png");
            ApplyTheme("light");
            DismissSidePanel();

            ShowHelp();
            await CaptureWindowAsync(directory, "help.png");
            DismissSidePanel();

            await ShowCleanupAsync();
            await CaptureWindowAsync(directory, "cleanup.png");
            DismissSidePanel();

            // Supply one disposable orphan so this captures the real recovery rows
            // even on a machine with no removed virtual desktops.
            createdOrphan = "visual-probe-" + Guid.NewGuid().ToString("N");
            await SessionStore.CreateSessionAsync(createdOrphan, "Previous desktop", null);
            await NoteStore.InsertNoteAsync(createdOrphan, "Recovered note", "A note from a previous desktop.", false, 0);
            var probeOrphan = createdOrphan;
            OrphanSessionGuids = () => [probeOrphan];
            if (TogglePanel("recovery", Strings.Recovery_Header) is { } recovery)
                await LoadRecoveryPanelAsync(recovery);
            await CaptureWindowAsync(directory, "recovery.png");
            DismissSidePanel();

            foreach (var width in new[] { 720d, 510d, 320d })
            {
                Width = width;
                await CaptureWindowAsync(directory, $"main-{width:0}.png");
            }
            Width = previousWidth;

            await ShowPreferencesAsync();
            await ApplyPanelLanguageAsync("cs");
            if (Strings.Pref_Theme != "Motiv" || Strings.Menu_Preferences != "Nastavení" ||
                global::Avalonia.Automation.AutomationProperties.GetName(AddButton) != "Nová poznámka")
                throw new InvalidOperationException("The Czech JoJot resources did not resolve in this build.");
            DismissSidePanel();
            await CaptureWindowAsync(directory, "main-cs.png");
            await ShowPreferencesAsync();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (!PanelHost.GetVisualDescendants().OfType<TextBlock>().Any(control => control.Text == "Motiv"))
                throw new InvalidOperationException("The Preferences controls did not use Czech resources.");
            await CaptureWindowAsync(directory, "preferences-cs.png");
        }
        finally
        {
            Width = previousWidth;
            DismissSidePanel();
            OrphanSessionGuids = previousOrphans;
            if (createdOrphan is not null)
                await SessionStore.DeleteSessionAndNotesAsync(createdOrphan);
            SetVisualLanguage(previousCulture);
            RequestedThemeVariant = previousTheme;
        }
    }

    private void SetVisualLanguage(string culture)
    {
        PanelLanguage.Apply(culture);
        LocalizeCoreControls();
        PreferencesMenuItem.Header = Strings.Menu_Preferences;
        CleanupMenuItem.Header = Strings.Menu_Cleanup;
        RecoveryMenuItem.Header = Strings.Menu_Recover;
        ExitMenuItem.Header = Strings.Menu_Exit;
        UpdateToolbarState();
        UpdateStatus();
    }

    private async Task CaptureWindowAsync(string directory, string filename)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var toolbarButtons = new[] { UndoButton, RedoButton, CopyButton, PasteButton, PinButton,
            CloneButton, SaveButton, SaveAsButton, FindButton, DeleteButton }.Where(button => button.IsVisible).ToArray();
        var rectangles = toolbarButtons.Select(button =>
        {
            var origin = button.TranslatePoint(default, EditorToolbar) ?? throw new InvalidOperationException("Toolbar button is detached.");
            return new Rect(origin, button.Bounds.Size);
        }).ToArray();
        for (var index = 0; index < rectangles.Length; index++)
        {
            if (!new Rect(EditorToolbar.Bounds.Size).Contains(rectangles[index]))
                throw new InvalidOperationException($"Toolbar button is clipped in {filename}.");
            for (var other = index + 1; other < rectangles.Length; other++)
                if (rectangles[index].Intersects(rectangles[other]))
                    throw new InvalidOperationException($"Toolbar buttons overlap in {filename}.");
        }
        _options.ValidateDataPath();
        var scale = RenderScaling;
        var pixels = new PixelSize((int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        bitmap.Render(this);
        using var output = File.Open(Path.Combine(directory, filename), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        bitmap.Save(output, PngBitmapEncoderOptions.Default);
    }
}
