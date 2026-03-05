---
status: diagnosed
trigger: "Recover Sessions menu not appearing when orphaned sessions exist after Keep-here + desktop removal"
created: 2026-03-05T00:00:00Z
updated: 2026-03-05T00:00:00Z
---

## Current Focus

hypothesis: DragKeepHere_Click preserves stale desktop_index from original desktop, allowing Tier 3 matching to silently consume the session on restart instead of flagging it as orphaned
test: Traced the full data flow from Keep-here through exit, desktop removal, and restart session matching
expecting: Orphaned session should appear in OrphanedSessionGuids but Tier 3 index matching silently reassigns it
next_action: Return diagnosis

## Symptoms

expected: "Recover Sessions" menu item visible when orphaned session exists after moving window via "Keep here", removing target desktop, and restarting
actual: "Recover Sessions" menu item not shown; user's notes may or may not appear depending on index alignment
errors: None (silent behavior)
reproduction: 1) Desktop 1 has JoJot session. 2) Create Desktop 2. 3) Drag window to Desktop 2, click "Keep here". 4) Exit JoJot. 5) Remove Desktop 2. 6) Start JoJot on Desktop 1.
started: By design — the code has always behaved this way

## Eliminated

- hypothesis: UpdateOrphanBadge not called at startup
  evidence: App.xaml.cs line 142 calls mainWindow.UpdateOrphanBadge() after session matching completes, sequentially awaited
  timestamp: 2026-03-05

- hypothesis: OrphanedSessionGuids cleared between MatchSessionsAsync and UpdateOrphanBadge
  evidence: No code modifies OrphanedSessionGuids between line 311 (set in MatchSessionsAsync) and line 2595 (read in UpdateOrphanBadge)
  timestamp: 2026-03-05

- hypothesis: Race condition in async startup
  evidence: All steps are sequentially awaited: MatchSessionsAsync -> EnsureCurrentDesktopSessionAsync -> ResolvePendingMovesAsync -> CreateWindowForDesktop -> UpdateOrphanBadge
  timestamp: 2026-03-05

- hypothesis: XAML default visibility hides MenuRecover
  evidence: MenuRecover has no Visibility attribute in XAML, defaults to Visible, only set by UpdateOrphanBadge in code-behind
  timestamp: 2026-03-05

- hypothesis: Fallback mode affecting detection
  evidence: Fallback mode has separate orphan detection that treats all non-"default" sessions as orphans — would show MORE, not fewer
  timestamp: 2026-03-05

## Evidence

- timestamp: 2026-03-05
  checked: DragKeepHere_Click (MainWindow.xaml.cs line 3591-3628)
  found: Calls UpdateSessionDesktopGuidAsync(oldGuid, newGuid) which ONLY updates desktop_guid column — does NOT update desktop_name or desktop_index
  implication: Session retains original desktop's index (typically 0) and name (typically empty) after reparenting

- timestamp: 2026-03-05
  checked: UpdateSessionDesktopGuidAsync (DatabaseService.cs line 1213-1240)
  found: SQL is "UPDATE app_state SET desktop_guid = @new WHERE desktop_guid = @old" — only desktop_guid column updated
  implication: Confirms stale metadata preserved in session row

- timestamp: 2026-03-05
  checked: MatchSessionsAsync Tier 3 (VirtualDesktopService.cs line 252-290)
  found: Matches sessions to desktops by index when exactly 1 unmatched session and 1 unmatched desktop share the same index
  implication: Stored index=0 from Desktop 1's original session matches Desktop 1's actual index=0 on restart

- timestamp: 2026-03-05
  checked: Tier 2 name matching (VirtualDesktopService.cs line 223-250)
  found: Skips sessions with empty/null DesktopName (line 225-226: if string.IsNullOrEmpty continue)
  implication: Windows COM API returns "" for unrenamed desktops, so Tier 2 is skipped in most cases

- timestamp: 2026-03-05
  checked: VirtualDesktopInterop.GetAllDesktopsInternal (VirtualDesktopInterop.cs line 154-194)
  found: GetName() returns "" for desktops not explicitly renamed by user
  implication: Default Windows desktop names like "Desktop 1" are NOT returned by the COM API — they're generated UI labels only

- timestamp: 2026-03-05
  checked: Orphan detection (VirtualDesktopService.cs line 306-312)
  found: Computes orphans as storedSessions where GUID not in matchedSessionGuids — correct logic
  implication: Since Tier 3 adds the session to matchedSessionGuids, it's not flagged as orphaned

- timestamp: 2026-03-05
  checked: Full startup sequence (App.xaml.cs line 119-142)
  found: MatchSessionsAsync -> EnsureCurrentDesktopSessionAsync -> ResolvePendingMovesAsync -> CreateWindowForDesktop -> UpdateOrphanBadge — all sequential
  implication: No timing issue; OrphanedSessionGuids is fully populated before badge check

## Resolution

root_cause: DragKeepHere_Click (MainWindow.xaml.cs) calls UpdateSessionDesktopGuidAsync which only updates the desktop_guid column, leaving the original desktop's desktop_index (typically 0) in the session row. On restart after removing the target desktop, MatchSessionsAsync Tier 3 index matching silently reassigns the session to whichever remaining desktop shares that index — bypassing orphan detection entirely. The session is never flagged as orphaned, so the Recover Sessions menu correctly (per current logic) stays hidden.
fix: (not applied — diagnosis only)
verification: (not verified — diagnosis only)
files_changed: []
