using JoJot.Interop;
using JoJot.Models;

namespace JoJot.Services
{
    /// <summary>
    /// Public API for virtual desktop detection and session management.
    /// All COM interop is isolated behind this boundary — no COM types appear in the public API.
    /// Falls back silently to single-notepad mode when the COM API is unavailable.
    /// </summary>
    public static class VirtualDesktopService
    {
        private static bool _isAvailable;
        private static string _currentDesktopGuid = "default";
        private static string _currentDesktopName = "";
        private static int _currentDesktopIndex;

        /// <summary>
        /// Whether the virtual desktop COM API is available and functioning.
        /// When false, the app runs in single-notepad fallback mode.
        /// </summary>
        public static bool IsAvailable => _isAvailable;

        /// <summary>
        /// The GUID of the current virtual desktop as a string.
        /// Returns "default" in fallback mode.
        /// </summary>
        public static string CurrentDesktopGuid => _currentDesktopGuid;

        /// <summary>
        /// The name of the current virtual desktop.
        /// Returns empty string if name is not set or in fallback mode.
        /// </summary>
        public static string CurrentDesktopName => _currentDesktopName;

        /// <summary>
        /// Initializes the virtual desktop service.
        /// Must be called on the WPF UI thread (STA requirement for shell COM objects).
        /// On failure, sets IsAvailable=false and uses 'default' GUID — never throws.
        /// </summary>
        public static Task InitializeAsync()
        {
            try
            {
                VirtualDesktopInterop.Initialize();

                var (id, name, index) = VirtualDesktopInterop.GetCurrentDesktop();
                _currentDesktopGuid = id.ToString();
                _currentDesktopName = name;
                _currentDesktopIndex = index;
                _isAvailable = true;

                LogService.Info(
                    $"VirtualDesktopService: available=true, desktop={_currentDesktopGuid}, " +
                    $"name=\"{_currentDesktopName}\", index={_currentDesktopIndex}");
            }
            catch (Exception ex)
            {
                // Silent fallback — no UI indication per user decision
                LogService.Warn($"Virtual desktop API unavailable — fallback mode: {ex.Message}");
                _isAvailable = false;
                _currentDesktopGuid = "default";
                _currentDesktopName = "";
                _currentDesktopIndex = 0;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns information about all live virtual desktops.
        /// In fallback mode, returns a single-element list with a default desktop.
        /// </summary>
        public static IReadOnlyList<DesktopInfo> GetAllDesktops()
        {
            if (!_isAvailable)
                return new[] { new DesktopInfo(Guid.Empty, "", 0) };

            try
            {
                var desktops = VirtualDesktopInterop.GetAllDesktopsInternal();
                return desktops
                    .Select(d => new DesktopInfo(d.Id, d.Name, d.Index))
                    .ToList()
                    .AsReadOnly();
            }
            catch (Exception ex)
            {
                LogService.Warn($"Failed to enumerate desktops: {ex.Message}");
                return new[] { new DesktopInfo(Guid.Empty, "", 0) };
            }
        }

        /// <summary>
        /// Returns information about the current virtual desktop.
        /// In fallback mode, returns a default DesktopInfo.
        /// </summary>
        public static DesktopInfo GetCurrentDesktopInfo()
        {
            if (!_isAvailable)
                return new DesktopInfo(Guid.Empty, "", 0);

            try
            {
                var (id, name, index) = VirtualDesktopInterop.GetCurrentDesktop();
                return new DesktopInfo(id, name, index);
            }
            catch (Exception ex)
            {
                LogService.Warn($"Failed to get current desktop: {ex.Message}");
                return new DesktopInfo(Guid.Empty, "", 0);
            }
        }

        // ─── Session Matching (VDSK-03, VDSK-04, VDSK-05) ────────────────────

        /// <summary>
        /// Three-tier session matching: reconnects stored desktop sessions to live desktops.
        /// Tier 1: GUID match (exact)
        /// Tier 2: Name match (skip if ambiguous — multiple desktops share name)
        /// Tier 3: Index match (strict: exactly one unmatched session + one unmatched desktop at index)
        /// Sessions that fail all tiers are preserved as orphaned (Phase 8 recovery).
        /// New desktops with no stored session get a fresh app_state row.
        /// </summary>
        public static async Task MatchSessionsAsync()
        {
            if (!_isAvailable)
            {
                LogService.Info("Session matching skipped (fallback mode)");
                await DatabaseService.CreateSessionAsync("default", null, null);
                return;
            }

            var liveDesktops = GetAllDesktops();
            var storedSessions = await DatabaseService.GetAllSessionsAsync();

            var matchedSessionGuids = new HashSet<string>();
            var matchedDesktopIds = new HashSet<string>();

            int tier1Count = 0, tier2Count = 0, tier3Count = 0;

            // ── Tier 1: GUID match (exact) ──────────────────────────────────
            foreach (var session in storedSessions)
            {
                var matchingDesktop = liveDesktops.FirstOrDefault(
                    d => d.Id.ToString().Equals(session.DesktopGuid, StringComparison.OrdinalIgnoreCase));

                if (matchingDesktop is not null && matchingDesktop.Id != Guid.Empty)
                {
                    matchedSessionGuids.Add(session.DesktopGuid);
                    matchedDesktopIds.Add(matchingDesktop.Id.ToString());

                    // Update name and index in case they changed (VDSK-04)
                    await DatabaseService.UpdateSessionAsync(
                        session.DesktopGuid,
                        session.DesktopGuid,
                        matchingDesktop.Name,
                        matchingDesktop.Index);

                    tier1Count++;
                }
            }

            // ── Tier 2: Name match (skip ambiguous) ─────────────────────────
            var unmatchedSessions = storedSessions
                .Where(s => !matchedSessionGuids.Contains(s.DesktopGuid))
                .ToList();

            var unmatchedDesktops = liveDesktops
                .Where(d => !matchedDesktopIds.Contains(d.Id.ToString()))
                .ToList();

            foreach (var session in unmatchedSessions.ToList())
            {
                if (string.IsNullOrEmpty(session.DesktopName))
                    continue;

                // Find desktops with matching name that haven't been matched yet
                var nameMatches = unmatchedDesktops
                    .Where(d => d.Name == session.DesktopName)
                    .ToList();

                if (nameMatches.Count == 1)
                {
                    // Unique name match — reassign session to this desktop
                    var desktop = nameMatches[0];
                    await DatabaseService.UpdateSessionAsync(
                        session.DesktopGuid,
                        desktop.Id.ToString(),
                        desktop.Name,
                        desktop.Index);

                    matchedSessionGuids.Add(session.DesktopGuid);
                    matchedDesktopIds.Add(desktop.Id.ToString());
                    unmatchedDesktops.Remove(desktop);
                    unmatchedSessions.Remove(session);
                    tier2Count++;
                }
                // 0 or 2+ matches → skip (ambiguous per user decision)
            }

            // ── Tier 3: Index match (strict one-to-one) ─────────────────────
            // Refresh unmatched lists after Tier 2
            unmatchedSessions = storedSessions
                .Where(s => !matchedSessionGuids.Contains(s.DesktopGuid))
                .ToList();

            foreach (var session in unmatchedSessions.ToList())
            {
                if (!session.DesktopIndex.HasValue)
                    continue;

                int sessionIndex = session.DesktopIndex.Value;

                // Find unmatched desktops at this index
                var indexMatches = unmatchedDesktops
                    .Where(d => d.Index == sessionIndex)
                    .ToList();

                // Strict condition (VDSK-05): exactly one unmatched session AND one unmatched desktop at this index
                var sessionsAtIndex = unmatchedSessions
                    .Where(s => s.DesktopIndex.HasValue && s.DesktopIndex.Value == sessionIndex)
                    .ToList();

                if (indexMatches.Count == 1 && sessionsAtIndex.Count == 1)
                {
                    var desktop = indexMatches[0];
                    await DatabaseService.UpdateSessionAsync(
                        session.DesktopGuid,
                        desktop.Id.ToString(),
                        desktop.Name,
                        desktop.Index);

                    matchedSessionGuids.Add(session.DesktopGuid);
                    matchedDesktopIds.Add(desktop.Id.ToString());
                    unmatchedDesktops.Remove(desktop);
                    unmatchedSessions.Remove(session);
                    tier3Count++;
                }
            }

            // ── Create sessions for new desktops ────────────────────────────
            int newCount = 0;
            foreach (var desktop in unmatchedDesktops)
            {
                if (desktop.Id == Guid.Empty)
                    continue;

                await DatabaseService.CreateSessionAsync(
                    desktop.Id.ToString(),
                    desktop.Name,
                    desktop.Index);
                newCount++;
            }

            // ── Count orphaned sessions ─────────────────────────────────────
            int orphanedCount = storedSessions.Count - matchedSessionGuids.Count;

            LogService.Info(
                $"Session matching complete: Tier 1 (GUID): {tier1Count}, " +
                $"Tier 2 (Name): {tier2Count}, Tier 3 (Index): {tier3Count}, " +
                $"Orphaned: {orphanedCount}, New: {newCount}");
        }

        /// <summary>
        /// Ensures a session row exists for the current desktop.
        /// Uses INSERT OR IGNORE — safe to call multiple times.
        /// </summary>
        public static async Task EnsureCurrentDesktopSessionAsync()
        {
            await DatabaseService.CreateSessionAsync(
                _currentDesktopGuid,
                _currentDesktopName,
                _isAvailable ? _currentDesktopIndex : (int?)null);
        }

        /// <summary>
        /// Shuts down the virtual desktop service and releases all COM objects.
        /// Safe to call even if not initialized or already shut down.
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                VirtualDesktopInterop.Dispose();
            }
            catch (Exception ex)
            {
                LogService.Warn($"Error during VirtualDesktopService shutdown: {ex.Message}");
            }

            _isAvailable = false;
            LogService.Info("VirtualDesktopService: shut down");
        }
    }
}
