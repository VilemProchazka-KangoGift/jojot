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
        string? createdOrphan = null;
        try
        {
            SetVisualLanguage("en");
            ApplyTheme("light");
            await CaptureWindowAsync(directory, "main-light.png");
            ApplyTheme("dark");
            await CaptureWindowAsync(directory, "main-dark.png");
            ApplyTheme("light");

            ShowFindPanel();
            if (_findQuery is { } query && Editor.Text is { Length: > 0 } editorText)
            {
                var word = editorText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (word is { Length: > 0 })
                    query.Text = word[..Math.Min(word.Length, 24)];
                _findTimer.Stop();
                await RunFindAsync();
            }
            await CaptureWindowAsync(directory, "find.png");
            HideFindPanel();

            await ShowPreferencesAsync();
            await CaptureWindowAsync(directory, "preferences.png");
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

            SetVisualLanguage("cs");
            if (Strings.Pref_Theme != "Motiv" || Strings.Menu_Preferences != "Nastavení" ||
                !Equals(AddButton.Content, "Nová poznámka"))
                throw new InvalidOperationException("The Czech JoJot resources did not resolve in this build.");
            await CaptureWindowAsync(directory, "main-cs.png");
            await ShowPreferencesAsync();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (!PanelHost.GetVisualDescendants().OfType<TextBlock>().Any(control => control.Text == "Motiv"))
                throw new InvalidOperationException("The Preferences controls did not use Czech resources.");
            await CaptureWindowAsync(directory, "preferences-cs.png");
        }
        finally
        {
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
        HelpMenuItem.Header = Strings.Help_Title;
        ExitMenuItem.Header = Strings.Menu_Exit;
        UpdateToolbarState();
        UpdateStatus();
    }

    private async Task CaptureWindowAsync(string directory, string filename)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        _options.ValidateDataPath();
        var scale = RenderScaling;
        var pixels = new PixelSize((int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        bitmap.Render(this);
        using var output = File.Open(Path.Combine(directory, filename), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        bitmap.Save(output, PngBitmapEncoderOptions.Default);
    }
}
