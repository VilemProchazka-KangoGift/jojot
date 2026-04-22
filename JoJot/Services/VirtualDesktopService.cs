using JoJot.Interop;
using JoJot.Models;

namespace JoJot.Services;

/// <summary>
/// Public API for virtual desktop detection and session management.
/// All COM interop is isolated behind this boundary — no COM types appear in the public API.
/// Falls back silently to single-notepad mode when the COM API is unavailable.
/// </summary>
public static class VirtualDesktopService
{
    private static bool _isAvailable;
    private static string _currentDesktopGuid = DefaultDesktopGuid;
    private static string _currentDesktopName = "";
    private static int _currentDesktopIndex;

    private static VirtualDesktopNotificationListener? _notificationListener;
    private static uint _notificationCookie;
    private static bool _notificationsRegistered;
    private static Timer? _pollingTimer;

    /// <summary>
    /// Fired when a desktop is renamed. Args: (desktopGuid, newName).
    /// No COM types in the signature — safe for consumers outside the interop boundary.
    /// </summary>
    public static event Action<string, string>? DesktopRenamed;

    /// <summary>
    /// Fired when the current desktop changes. Args: (oldGuid, newGuid).
    /// No COM types in the signature — safe for consumers outside the interop boundary.
    /// </summary>
    public static event Action<string, string>? CurrentDesktopChanged;

    /// <summary>
    /// Fired when a window is detected moving to a different desktop.
    /// Args: (windowHwnd, fromDesktopGuid, toDesktopGuid, toDesktopName).
    /// The handler should show the lock overlay for conflict resolution.
    /// </summary>
    public static event Action<IntPtr, string, string, string>? WindowMovedToDesktop;

    /// <summary>Default desktop GUID used in fallback mode when COM is unavailable.</summary>
    private const string DefaultDesktopGuid = "default";

    /// <summary>
    /// Whether the virtual desktop COM API is available and functioning.
    /// When false, the app runs in single-notepad fallback mode.
    /// </summary>
    public static bool IsAvailable => _isAvailable;

    /// <summary>
    /// The GUID of the current virtual desktop as a string.
    /// Returns "default" in fallback mode.
    /// Uses Volatile.Read for cross-thread visibility (read from WndProc hook on UI thread,
    /// written from COM callback thread).
    /// </summary>
    public static string CurrentDesktopGuid => Volatile.Read(ref _currentDesktopGuid);

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
            Volatile.Write(ref _currentDesktopGuid, id.ToString());
            _currentDesktopName = name;
            _currentDesktopIndex = index;
            _isAvailable = true;

            LogService.Info(
                "VirtualDesktopService: available=true, desktop={DesktopGuid}, name={DesktopName}, index={DesktopIndex}",
                _currentDesktopGuid, _currentDesktopName, _currentDesktopIndex);
        }
        catch (Exception ex)
        {
            LogService.Warn("Virtual desktop API unavailable — fallback mode: {ErrorMessage}", ex.Message);
            _isAvailable = false;
            Volatile.Write(ref _currentDesktopGuid, DefaultDesktopGuid);
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
        {
            return [new DesktopInfo(Guid.Empty, "", 0)];
        }

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
            LogService.Warn("Failed to enumerate desktops: {ErrorMessage}", ex.Message);
            return [new DesktopInfo(Guid.Empty, "", 0)];
        }
    }

    /// <summary>
    /// Returns information about the current virtual desktop.
    /// In fallback mode, returns a default DesktopInfo.
    /// </summary>
    public static DesktopInfo GetCurrentDesktopInfo()
    {
        if (!_isAvailable)
        {
            return new DesktopInfo(Guid.Empty, "", 0);
        }

        try
        {
            var (id, name, index) = VirtualDesktopInterop.GetCurrentDesktop();
            return new DesktopInfo(id, name, index);
        }
        catch (Exception ex)
        {
            LogService.Warn("Failed to get current desktop: {ErrorMessage}", ex.Message);
            return new DesktopInfo(Guid.Empty, "", 0);
        }
    }

    // ─── Orphaned Session State ─────────────────────────────────────────

    /// <summary>
    /// GUIDs of sessions that failed all three matching tiers.
    /// Populated during <see cref="MatchSessionsAsync"/>, consumed by the recovery panel.
    /// </summary>
    public static IReadOnlyList<string> OrphanedSessionGuids { get; private set; } = [];

    /// <summary>
    /// Updates the orphaned session list (called after recovery actions).
    /// </summary>
    public static void SetOrphanedSessionGuids(List<string> guids)
    {
        OrphanedSessionGuids = guids;
    }

    // ─── Session Matching ───────────────────────────────────────────────

    /// <summary>
    /// Three-tier session matching: reconnects stored desktop sessions to live desktops.
    /// Tier 1: GUID match (exact).
    /// Tier 2: Name match (skip if ambiguous — multiple desktops share name).
    /// Tier 3: Index match (strict: exactly one unmatched session + one unmatched desktop at index).
    /// Sessions that fail all tiers are preserved as orphaned for recovery.
    /// New desktops with no stored session get a fresh app_state row.
    /// </summary>
    public static async Task MatchSessionsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isAvailable)
        {
            LogService.Info("Session matching skipped (fallback mode)");
            await SessionStore.CreateSessionAsync(DefaultDesktopGuid, null, null).ConfigureAwait(false);

            // Fallback orphan detection: any session that isn't the default is orphaned
            var fallbackSessions = await SessionStore.GetAllSessionsAsync().ConfigureAwait(false);
            var fallbackOrphanCandidates = fallbackSessions
                .Where(s => !s.DesktopGuid.Equals(DefaultDesktopGuid, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.DesktopGuid)
                .ToList();

            // Auto-delete empty orphans (no tabs) — no point offering recovery
            var fallbackOrphans = new List<string>();
            foreach (var guid in fallbackOrphanCandidates)
            {
                int tabCount = await NoteStore.GetNoteCountForDesktopAsync(guid, cancellationToken).ConfigureAwait(false);
                if (tabCount == 0)
                {
                    await SessionStore.DeleteSessionAndNotesAsync(guid).ConfigureAwait(false);
                    LogService.Info("Auto-deleted empty orphaned session {DesktopGuid}", guid);
                }
                else
                {
                    fallbackOrphans.Add(guid);
                }
            }
            OrphanedSessionGuids = fallbackOrphans;
            if (fallbackOrphans.Count > 0)
            {
                LogService.Info("Fallback orphan detection: {OrphanCount} orphaned session(s)", fallbackOrphans.Count);
            }

            return;
        }

        var liveDesktops = GetAllDesktops();
        var storedSessions = await SessionStore.GetAllSessionsAsync().ConfigureAwait(false);

        var matchedSessionGuids = new HashSet<string>();
        var matchedDesktopIds = new HashSet<string>();

        int tier1Count = 0, tier2Count = 0, tier3Count = 0;

        // ─── Tier 1: GUID match (exact) ─────────────────────────────────
        foreach (var session in storedSessions)
        {
            var matchingDesktop = liveDesktops.FirstOrDefault(
                d => d.Id.ToString().Equals(session.DesktopGuid, StringComparison.OrdinalIgnoreCase));

            if (matchingDesktop is not null && matchingDesktop.Id != Guid.Empty)
            {
                matchedSessionGuids.Add(session.DesktopGuid);
                matchedDesktopIds.Add(matchingDesktop.Id.ToString());

                // Update name and index in case they changed
                await SessionStore.UpdateSessionAsync(
                    session.DesktopGuid,
                    session.DesktopGuid,
                    matchingDesktop.Name,
                    matchingDesktop.Index).ConfigureAwait(false);

                tier1Count++;
            }
        }

        // ─── Tier 2: Name match (skip ambiguous) ────────────────────────
        var unmatchedSessions = storedSessions
            .Where(s => !matchedSessionGuids.Contains(s.DesktopGuid))
            .ToList();

        var unmatchedDesktops = liveDesktops
            .Where(d => !matchedDesktopIds.Contains(d.Id.ToString()))
            .ToList();

        foreach (var session in unmatchedSessions.ToList())
        {
            if (string.IsNullOrEmpty(session.DesktopName))
            {
                continue;
            }

            // Find desktops with matching name that haven't been matched yet
            var nameMatches = unmatchedDesktops
                .Where(d => d.Name == session.DesktopName)
                .ToList();

            if (nameMatches.Count == 1)
            {
                // Unique name match — reassign session to this desktop
                var desktop = nameMatches[0];
                await SessionStore.UpdateSessionAsync(
                    session.DesktopGuid,
                    desktop.Id.ToString(),
                    desktop.Name,
                    desktop.Index).ConfigureAwait(false);

                matchedSessionGuids.Add(session.DesktopGuid);
                matchedDesktopIds.Add(desktop.Id.ToString());
                unmatchedDesktops.Remove(desktop);
                unmatchedSessions.Remove(session);
                tier2Count++;
            }
            // 0 or 2+ matches → skip (ambiguous)
        }

        // ─── Tier 3: Index match (strict one-to-one) ────────────────────
        // Refresh unmatched lists after Tier 2
        unmatchedSessions = storedSessions
            .Where(s => !matchedSessionGuids.Contains(s.DesktopGuid))
            .ToList();

        foreach (var session in unmatchedSessions.ToList())
        {
            if (!session.DesktopIndex.HasValue)
            {
                continue;
            }

            int sessionIndex = session.DesktopIndex.Value;

            // Find unmatched desktops at this index
            var indexMatches = unmatchedDesktops
                .Where(d => d.Index == sessionIndex)
                .ToList();

            // Strict condition: exactly one unmatched session AND one unmatched desktop at this index
            var sessionsAtIndex = unmatchedSessions
                .Where(s => s.DesktopIndex.HasValue && s.DesktopIndex.Value == sessionIndex)
                .ToList();

            if (indexMatches.Count == 1 && sessionsAtIndex.Count == 1)
            {
                var desktop = indexMatches[0];
                await SessionStore.UpdateSessionAsync(
                    session.DesktopGuid,
                    desktop.Id.ToString(),
                    desktop.Name,
                    desktop.Index).ConfigureAwait(false);

                matchedSessionGuids.Add(session.DesktopGuid);
                matchedDesktopIds.Add(desktop.Id.ToString());
                unmatchedDesktops.Remove(desktop);
                unmatchedSessions.Remove(session);
                tier3Count++;
            }
        }

        // ─── Create sessions for new desktops ───────────────────────────
        int newCount = 0;
        foreach (var desktop in unmatchedDesktops)
        {
            if (desktop.Id == Guid.Empty)
            {
                continue;
            }

            await SessionStore.CreateSessionAsync(
                desktop.Id.ToString(),
                desktop.Name,
                desktop.Index).ConfigureAwait(false);
            newCount++;
        }

        // ─── Expose orphaned sessions for recovery ──────────────────────
        var orphanedGuids = storedSessions
            .Where(s => !matchedSessionGuids.Contains(s.DesktopGuid))
            .Select(s => s.DesktopGuid)
            .ToList();

        // Auto-delete empty orphans (no tabs) — no point offering recovery
        var nonEmptyOrphans = new List<string>();
        foreach (var guid in orphanedGuids)
        {
            int tabCount = await NoteStore.GetNoteCountForDesktopAsync(guid, cancellationToken).ConfigureAwait(false);
            if (tabCount == 0)
            {
                await SessionStore.DeleteSessionAndNotesAsync(guid).ConfigureAwait(false);
                LogService.Info("Auto-deleted empty orphaned session {DesktopGuid}", guid);
            }
            else
            {
                nonEmptyOrphans.Add(guid);
            }
        }
        OrphanedSessionGuids = nonEmptyOrphans;
        int orphanedCount = nonEmptyOrphans.Count;

        LogService.Info(
            "Session matching complete: Tier 1 (GUID): {Tier1}, Tier 2 (Name): {Tier2}, Tier 3 (Index): {Tier3}, Orphaned: {Orphaned}, New: {New}",
            tier1Count, tier2Count, tier3Count, orphanedCount, newCount);
    }

    /// <summary>
    /// Ensures a session row exists for the current desktop.
    /// Uses INSERT OR IGNORE — safe to call multiple times.
    /// </summary>
    public static async Task EnsureCurrentDesktopSessionAsync(CancellationToken cancellationToken = default)
    {
        await SessionStore.CreateSessionAsync(
            _currentDesktopGuid,
            _currentDesktopName,
            _isAvailable ? _currentDesktopIndex : (int?)null).ConfigureAwait(false);
    }

    // ─── Notification Subscription ──────────────────────────────────────

    /// <summary>
    /// Registers for COM desktop notifications (rename, switch, create, destroy).
    /// Must be called on the WPF UI thread (STA requirement for COM callbacks).
    /// If subscription fails, logs a warning and continues — title won't update live
    /// but static title still works at startup.
    /// </summary>
    public static void SubscribeNotifications()
    {
        if (!_isAvailable)
        {
            LogService.Info("Notification subscription skipped (fallback mode)");
            return;
        }

        var notifService = VirtualDesktopInterop.GetNotificationService();
        if (notifService is null)
        {
            LogService.Warn("Notification service unavailable — falling back to polling");
            StartDesktopPolling();
            return;
        }

        try
        {
            _notificationListener = new VirtualDesktopNotificationListener();

            // Wire listener events to public events + database updates
            _notificationListener.DesktopRenamed += OnDesktopRenamed;
            _notificationListener.CurrentDesktopChanged += OnCurrentDesktopChanged;
            _notificationListener.WindowViewChanged += OnWindowViewChanged;

            int hr = notifService.Register(_notificationListener, out _notificationCookie);
            if (hr != 0)
            {
                LogService.Warn("Notification registration failed (HRESULT: {HResult}) — falling back to polling", $"0x{hr:X8}");
                _notificationListener = null;
                StartDesktopPolling();
                return;
            }

            _notificationsRegistered = true;
            LogService.Info("Desktop notifications registered (cookie={Cookie})", _notificationCookie);
        }
        catch (Exception ex)
        {
            LogService.Warn("Failed to subscribe to desktop notifications — falling back to polling: {ErrorMessage}", ex.Message);
            _notificationListener = null;
            StartDesktopPolling();
        }
    }

    /// <summary>
    /// Starts polling GetCurrentDesktop() as a fallback when COM notifications are unavailable.
    /// Detects desktop switches by comparing the polled GUID to the cached value.
    /// </summary>
    private static void StartDesktopPolling()
    {
        _pollingTimer = new Timer(_ => PollDesktopChange(), null, 500, 500);
        LogService.Info("Desktop polling started (500ms interval)");
    }

    /// <summary>
    /// Polls the current desktop and fires <see cref="CurrentDesktopChanged"/> if a switch is detected.
    /// </summary>
    private static void PollDesktopChange()
    {
        try
        {
            var (id, name, index) = VirtualDesktopInterop.GetCurrentDesktop();
            string newGuid = id.ToString();
            if (!newGuid.Equals(_currentDesktopGuid, StringComparison.OrdinalIgnoreCase))
            {
                string oldGuid = Volatile.Read(ref _currentDesktopGuid);
                Volatile.Write(ref _currentDesktopGuid, newGuid);
                _currentDesktopName = name;
                _currentDesktopIndex = index;
                LogService.Info("Poll: desktop switched {OldGuid} -> {NewGuid}", oldGuid, newGuid);
                CurrentDesktopChanged?.Invoke(oldGuid, newGuid);
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("Desktop polling failed: {ErrorMessage}", ex.Message);
        }
    }

    /// <summary>
    /// Unregisters the COM notification callback and cleans up the listener.
    /// Safe to call even if not subscribed or already unsubscribed.
    /// </summary>
    public static void UnsubscribeNotifications()
    {
        _pollingTimer?.Dispose();
        _pollingTimer = null;

        if (!_notificationsRegistered)
        {
            return;
        }

        try
        {
            var notifService = VirtualDesktopInterop.GetNotificationService();
            if (notifService is not null && _notificationCookie != 0)
            {
                notifService.Unregister(_notificationCookie);
                LogService.Info("Desktop notifications unregistered (cookie={Cookie})", _notificationCookie);
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("Error unregistering desktop notifications: {ErrorMessage}", ex.Message);
        }

        if (_notificationListener is not null)
        {
            _notificationListener.DesktopRenamed -= OnDesktopRenamed;
            _notificationListener.CurrentDesktopChanged -= OnCurrentDesktopChanged;
            _notificationListener.WindowViewChanged -= OnWindowViewChanged;
            _notificationListener = null;
        }

        _notificationsRegistered = false;
    }

    /// <summary>
    /// Handles desktop rename: updates internal state, persists to database,
    /// and fires the public DesktopRenamed event.
    /// Updates both the window title and app_state.desktop_name immediately.
    /// </summary>
    private static void OnDesktopRenamed(Guid desktopId, string newName)
    {
        string guidStr = desktopId.ToString();

        // Update internal state if this is our current desktop
        if (guidStr.Equals(_currentDesktopGuid, StringComparison.OrdinalIgnoreCase))
        {
            _currentDesktopName = newName;
        }

        // Persist to database immediately (fire-and-forget with error logging)
        _ = Task.Run(async () =>
        {
            try
            {
                await SessionStore.UpdateDesktopNameAsync(guidStr, newName).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to persist desktop rename to database: {ErrorMessage}", ex.Message);
            }
        });

        // Fire public event (consumers handle UI thread marshaling)
        DesktopRenamed?.Invoke(guidStr, newName);
    }

    /// <summary>
    /// Handles desktop switch: updates internal state and fires the public CurrentDesktopChanged event.
    /// </summary>
    private static void OnCurrentDesktopChanged(Guid oldDesktopId, Guid newDesktopId)
    {
        string oldGuid = oldDesktopId.ToString();
        string newGuid = newDesktopId.ToString();

        // Update internal state to reflect the new current desktop
        Volatile.Write(ref _currentDesktopGuid, newGuid);

        // Try to update name and index from the new desktop
        try
        {
            var (id, name, index) = VirtualDesktopInterop.GetCurrentDesktop();
            _currentDesktopName = name;
            _currentDesktopIndex = index;
        }
        catch (Exception ex)
        {
            LogService.Warn("Failed to refresh desktop info after switch: {ErrorMessage}", ex.Message);
        }

        // Fire public event (consumers handle UI thread marshaling)
        CurrentDesktopChanged?.Invoke(oldGuid, newGuid);
    }

    // ─── Window Drag Detection ──────────────────────────────────────────

    /// <summary>
    /// Handles the ViewVirtualDesktopChanged COM callback.
    /// The IntPtr view parameter is an IApplicationView pointer (NOT an HWND).
    /// We cannot directly map it to a window, so we check all known windows
    /// to find which one has moved to a different desktop.
    /// Uses Dispatcher.BeginInvoke to let COM state settle before querying.
    /// </summary>
    private static void OnWindowViewChanged(IntPtr view)
    {
        // Must dispatch to UI thread and let COM state settle
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Normal,
            new Action(() =>
            {
                try
                {
                    DetectMovedWindow();
                }
                catch (Exception ex)
                {
                    LogService.Warn("Error detecting moved window: {ErrorMessage}", ex.Message);
                }
            }));
    }

    /// <summary>
    /// Iterates all known JoJot windows and checks if any has moved to a different desktop.
    /// Called after COM state has settled via Dispatcher.BeginInvoke.
    /// </summary>
    private static void DetectMovedWindow()
    {
        if (!_isAvailable)
        {
            return;
        }

        var app = System.Windows.Application.Current as App;
        if (app is null)
        {
            return;
        }

        var allWindows = app.GetAllWindows();
        foreach (var window in allWindows)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    continue;
                }

                Guid currentDesktopId = VirtualDesktopInterop.GetWindowDesktopId(hwnd);
                string currentDesktopGuid = currentDesktopId.ToString();
                string expectedDesktopGuid = window.DesktopGuid;

                if (!currentDesktopGuid.Equals(expectedDesktopGuid, StringComparison.OrdinalIgnoreCase))
                {
                    // This window has moved!
                    string toDesktopName = "";
                    try
                    {
                        var desktops = GetAllDesktops();
                        var targetDesktop = desktops.FirstOrDefault(d =>
                            d.Id.ToString().Equals(currentDesktopGuid, StringComparison.OrdinalIgnoreCase));
                        toDesktopName = targetDesktop?.Name ?? "";
                    }
                    catch (Exception ex) { LogService.Debug("Desktop name lookup failed (best-effort): {Error}", ex.Message); }

                    LogService.Info("Window drag detected: {FromDesktop} -> {ToDesktop} (target: {TargetName})", expectedDesktopGuid, currentDesktopGuid, toDesktopName);
                    WindowMovedToDesktop?.Invoke(hwnd, expectedDesktopGuid, currentDesktopGuid, toDesktopName);
                    return; // Only one window can move at a time
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("Error checking window desktop: {ErrorMessage}", ex.Message);
            }
        }
    }

    /// <summary>
    /// Moves a window to the specified desktop via COM API.
    /// Returns true if successful, false if the COM call failed.
    /// </summary>
    public static bool TryMoveWindowToDesktop(IntPtr hwnd, string desktopGuid)
    {
        if (!_isAvailable)
        {
            return false;
        }

        try
        {
            Guid targetId = Guid.Parse(desktopGuid);
            VirtualDesktopInterop.MoveWindowToDesktop(hwnd, targetId);
            LogService.Info("Moved window to desktop {DesktopGuid}", desktopGuid);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("MoveWindowToDesktop failed for {DesktopGuid}: {ErrorMessage}", desktopGuid, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Switches the user's view to the specified desktop via COM API.
    /// Returns true if successful, false if the COM call failed.
    /// </summary>
    public static bool TrySwitchToDesktop(string desktopGuid)
    {
        if (!_isAvailable)
        {
            return false;
        }

        try
        {
            Guid targetId = Guid.Parse(desktopGuid);
            VirtualDesktopInterop.SwitchToDesktop(targetId);
            LogService.Info("Switched to desktop {DesktopGuid}", desktopGuid);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("SwitchToDesktop failed for {DesktopGuid}: {ErrorMessage}", desktopGuid, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Shuts down the virtual desktop service and releases all COM objects.
    /// Unsubscribes notifications before disposing COM objects.
    /// Safe to call even if not initialized or already shut down.
    /// </summary>
    public static void Shutdown()
    {
        UnsubscribeNotifications();

        try
        {
            VirtualDesktopInterop.Dispose();
        }
        catch (Exception ex)
        {
            LogService.Warn("Error during VirtualDesktopService shutdown: {ErrorMessage}", ex.Message);
        }

        _isAvailable = false;
        LogService.Info("VirtualDesktopService: shut down");
    }
}
