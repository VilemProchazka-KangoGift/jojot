using Avalonia.Controls;
using JoJot.Models;
using JoJot.Resources;
using JoJot.Services;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow
{
    internal static Func<IReadOnlyList<PendingMove>>? ReviewPendingMoves { get; set; }
    internal static Action<long>? PendingMoveResolved { get; set; }
    internal static Func<string, bool>? CanOpenRecoveryDesktop { get; set; }
    internal static Func<string, Task>? OpenRecoveryDesktopAsync { get; set; }

    private static string PendingMoveReviewTitle =>
        CoreLabel("Interrupted desktop move", "Přerušený přesun plochy");

    private static string ClearPendingMoveLabel =>
        CoreLabel("Clear pending move", "Vymazat záznam o přesunu");

    private static bool ReferencesDesktop(PendingMove move, string guid) =>
        string.Equals(move.FromDesktop, guid, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(move.ToDesktop, guid, StringComparison.OrdinalIgnoreCase);

    private async Task AddPendingMoveReviewsAsync(StackPanel body, IReadOnlyList<PendingMove> moves)
    {
        foreach (var move in moves)
        {
            var source = await PendingDesktopPreviewAsync(CoreLabel("Source desktop", "Zdrojová plocha"), move.FromDesktop);
            var target = await PendingDesktopPreviewAsync(CoreLabel("Destination desktop", "Cílová plocha"), move.ToDesktop);
            if (!ReferenceEquals(body, _panelBody))
                return;

            if (body.Children.Count > 0)
                body.Children.Add(new Separator());
            body.Children.Add(PanelSection(PendingMoveReviewTitle));
            body.Children.Add(PanelText(move.DetectedAt));
            body.Children.Add(PanelText(CoreLabel("These previews show where notes are currently stored.",
                "Náhledy ukazují, kde jsou poznámky právě uloženy.")));
            body.Children.Add(source);
            body.Children.Add(target);
            body.Children.Add(PanelAsyncButton(ClearPendingMoveLabel, () => ClearPendingMoveAsync(body, move)));
        }
    }

    private async Task<StackPanel> PendingDesktopPreviewAsync(string role, string? guid)
    {
        var preview = new StackPanel { Spacing = 5, Children = { PanelSection(role) } };
        if (string.IsNullOrWhiteSpace(guid))
        {
            preview.Children.Add(PanelText(CoreLabel("Destination was not recorded.", "Cílová plocha nebyla zaznamenána.")));
            return preview;
        }

        var session = (await SessionStore.GetOrphanedSessionInfoAsync([guid]))[0];
        var label = string.IsNullOrWhiteSpace(session.DesktopName) ? Strings.Recovery_UnknownDesktop : session.DesktopName;
        preview.Children.Add(PanelText($"{label} ({guid})"));
        var count = session.TabCount;
        preview.Children.Add(PanelText(string.Format(PanelLanguage.Plural(Strings.Recovery_TabCount_One,
            Strings.Recovery_TabCount_Few, Strings.Recovery_TabCount, count), count)));

        var notes = await NoteStore.GetNotePreviewsForDesktopAsync(guid, 5);
        foreach (var (name, content, created, updated) in notes)
        {
            var excerpt = Excerpt(content);
            var title = name ?? (string.IsNullOrWhiteSpace(excerpt) ? Strings.Recovery_EmptyNote : excerpt);
            preview.Children.Add(NotePreview(title, name is null ? "" : excerpt, created, updated));
        }
        if (count > notes.Count)
            preview.Children.Add(PanelText(string.Format(Strings.Recovery_More, count - notes.Count)));

        if (CanOpenRecoveryDesktop?.Invoke(guid) == true && OpenRecoveryDesktopAsync is { } open)
            preview.Children.Add(PanelAsyncButton(CoreLabel("Open desktop", "Otevřít plochu"), () => open(guid)));
        return preview;
    }

    private async Task ClearPendingMoveAsync(StackPanel body, PendingMove move)
    {
        if (_recoveryBusy)
            return;
        _recoveryBusy = true;
        body.IsEnabled = false;
        try
        {
            var prompt = PanelText(CoreLabel(
                "Clear this pending move record after reviewing the notes above? All notes and sessions will stay where they are.",
                "Vymazat záznam o přesunu po kontrole uvedených poznámek? Poznámky a relace zůstanou na svém místě."));
            if (!await ConfirmAsync(PendingMoveReviewTitle, prompt, ClearPendingMoveLabel))
                return;

            await OrphanRecoveryGate.WaitAsync();
            try
            {
                if (ReviewPendingMoves?.Invoke().Any(candidate => candidate.Id == move.Id) != true)
                    return;

                // The reviewed row is the only record being changed. Legacy window IDs are not unique.
                await DatabaseCore.ExecuteNonQueryAsync(
                    "DELETE FROM pending_moves WHERE id = @id AND window_id = @window AND detected_at = @detected;",
                    ("@id", move.Id), ("@window", move.WindowId), ("@detected", move.DetectedAt));
                PendingMoveResolved?.Invoke(move.Id);
                UpdateOrphanBadge();
                foreach (var window in PanelWindows().Where(window => window != this))
                    window.NotifyOrphansChanged();
                if (ReferenceEquals(body, _panelBody))
                    await LoadRecoveryPanelAsync(body);
            }
            finally { OrphanRecoveryGate.Release(); }
        }
        finally
        {
            body.IsEnabled = true;
            _recoveryBusy = false;
        }
    }
}
