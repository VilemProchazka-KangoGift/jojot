using JoJot.Services;

namespace JoJot.AvaloniaExperiment.Platform;

// Same matching order and ambiguity rules as the WPF service. Enumerate COM before calling.
internal static class DesktopSessionMatcher
{
    internal static async Task<IReadOnlyList<string>> MatchAsync(IReadOnlyList<VirtualDesktop.Desktop> live)
    {
        var sessions = await SessionStore.GetAllSessionsAsync().ConfigureAwait(false);
        if (live.Count == 1 && live[0].Id == "default")
        {
            await SessionStore.CreateSessionAsync("default", null, null).ConfigureAwait(false);
            return await RemoveEmptyAsync(sessions.Where(s => !s.DesktopGuid.Equals("default", StringComparison.OrdinalIgnoreCase)).Select(s => s.DesktopGuid)).ConfigureAwait(false);
        }
        var remaining = live.Where(d => Guid.TryParse(d.Id, out var id) && id != Guid.Empty).ToList();
        var unmatched = sessions.ToList();
        foreach (var session in sessions)
        {
            var desktop = remaining.FirstOrDefault(d => d.Id.Equals(session.DesktopGuid, StringComparison.OrdinalIgnoreCase));
            if (desktop is null) continue;
            if (!string.Equals(session.DesktopName ?? "", desktop.Name, StringComparison.Ordinal) || session.DesktopIndex != desktop.Index)
                await SessionStore.UpdateSessionAsync(session.DesktopGuid, session.DesktopGuid, desktop.Name, desktop.Index).ConfigureAwait(false);
            unmatched.Remove(session);
            remaining.Remove(desktop);
        }
        foreach (var session in unmatched.ToArray())
        {
            if (string.IsNullOrEmpty(session.DesktopName)) continue;
            var matches = remaining.Where(d => d.Name == session.DesktopName).ToArray();
            if (matches.Length != 1 || unmatched.Count(s => s.DesktopName == session.DesktopName) != 1) continue;
            await SessionStore.UpdateSessionAsync(session.DesktopGuid, matches[0].Id, matches[0].Name, matches[0].Index).ConfigureAwait(false);
            unmatched.Remove(session);
            remaining.Remove(matches[0]);
        }
        foreach (var session in unmatched.ToArray())
        {
            if (session.DesktopIndex is null) continue;
            var matches = remaining.Where(d => d.Index == session.DesktopIndex).ToArray();
            if (matches.Length != 1 || unmatched.Count(s => s.DesktopIndex == session.DesktopIndex) != 1) continue;
            await SessionStore.UpdateSessionAsync(session.DesktopGuid, matches[0].Id, matches[0].Name, matches[0].Index).ConfigureAwait(false);
            unmatched.Remove(session);
            remaining.Remove(matches[0]);
        }
        foreach (var desktop in remaining)
            await SessionStore.CreateSessionAsync(desktop.Id, desktop.Name, desktop.Index).ConfigureAwait(false);
        return await RemoveEmptyAsync(unmatched.Select(s => s.DesktopGuid)).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> RemoveEmptyAsync(IEnumerable<string> candidates)
    {
        var ids = candidates.ToList();
        if (ids.Count == 0) return ids;
        var counts = await NoteStore.GetNoteCountsForDesktopsAsync(ids).ConfigureAwait(false);
        var orphans = new List<string>();
        foreach (var id in ids)
        {
            if (counts.GetValueOrDefault(id) == 0)
                await SessionStore.DeleteSessionAndNotesAsync(id).ConfigureAwait(false);
            else orphans.Add(id);
        }
        return orphans;
    }
}
