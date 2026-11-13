using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JoJot.Resources;
using JoJot.Services;
using JoJot.AvaloniaExperiment.Platform;

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
            await CaptureButtonStatesAsync(directory, "light");
            NativeFrameCapture.Save(this, Path.Combine(directory, "native-light.png"));
            ApplyTheme("dark");
            await CaptureWindowAsync(directory, "main-dark.png");
            await CaptureButtonStatesAsync(directory, "dark");
            NativeFrameCapture.Save(this, Path.Combine(directory, "native-dark.png"));
            BeginRename(_active!);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await CaptureWindowAsync(directory, "rename-dark.png");
            FinishRename(save: false);
            ApplyTheme("light");
            await CaptureWindowAsync(directory, "native-light-return-client.png");
            NativeFrameCapture.Save(this, Path.Combine(directory, "native-light-return.png"));
            BeginRename(_active!);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await CaptureWindowAsync(directory, "rename-light.png");
            FinishRename(save: false);

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
            ApplyTheme("dark");
            await CaptureWindowAsync(directory, "find-dark.png");
            ApplyTheme("light");
            _findWhole!.IsChecked = true;
            _findCase!.IsChecked = true;
            _findTimer.Stop();
            await RunFindAsync();
            ApplyTheme("dark");
            await CaptureWindowAsync(directory, "find-options-dark.png");
            ApplyTheme("light");
            _findWhole.IsChecked = false;
            _findCase.IsChecked = false;
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
                if (width == 320)
                {
                    BeginRename(_active!);
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    if (_renameBox is { } rename) rename.Text = new string('W', 120);
                    await CaptureWindowAsync(directory, "rename-320.png");
                    FinishRename(save: false);
                }
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

    private async Task CaptureButtonStatesAsync(string directory, string theme)
    {
        foreach (var (button, name) in new[] { (AddButton, "new"), (CopyButton, "toolbar") })
        {
            try
            {
                ((Avalonia.Controls.IPseudoClasses)button.Classes).Set(":pointerover", true);
                await CaptureWindowAsync(directory, $"{name}-hover-{theme}.png");
                // Diagnostic state injection avoids moving the physical cursor to an offscreen window.
                ((Avalonia.Controls.IPseudoClasses)button.Classes).Set(":pressed", true);
                await CaptureWindowAsync(directory, $"{name}-pressed-{theme}.png");

            }
            finally { ((Avalonia.Controls.IPseudoClasses)button.Classes).Set(":pressed", false); ((Avalonia.Controls.IPseudoClasses)button.Classes).Set(":pointerover", false); }
        }
    }

    private async Task CaptureWindowAsync(string directory, string filename)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var sidebarControls = new Control[] { HamburgerButton, SearchBox, AddButton };
        var sidebarBounds = sidebarControls.Select(control =>
            new Rect(control.TranslatePoint(default, this)!.Value, control.Bounds.Size)).ToArray();
        for (var index = 0; index < sidebarBounds.Length; index++)
        {
            if (sidebarBounds[index].Width <= 0 || sidebarBounds[index].Bottom > NoteList.TranslatePoint(default, this)!.Value.Y)
                throw new InvalidOperationException($"Sidebar header is clipped in {filename}.");
            if (index > 0 && (sidebarBounds[index - 1].Right + 3 > sidebarBounds[index].Left ||
                Math.Abs(sidebarBounds[index - 1].Center.Y - sidebarBounds[index].Center.Y) > 1))
                throw new InvalidOperationException($"Sidebar header is not inline in {filename}.");
        }
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
        var addBounds = new Rect(AddButton.TranslatePoint(default, this)!.Value, AddButton.Bounds.Size);
        File.WriteAllText(Path.Combine(directory, filename + ".button.txt"), FormattableString.Invariant($"X={addBounds.X * scale};Y={addBounds.Y * scale};Width={addBounds.Width * scale};Height={addBounds.Height * scale}"));
        var pixels = new PixelSize((int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        bitmap.Render(this);
        using var output = File.Open(Path.Combine(directory, filename), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        bitmap.Save(output, PngBitmapEncoderOptions.Default);
    }
}
