---
status: investigating
trigger: "Move overlay doesn't refresh when window is moved again"
created: 2026-03-05T00:00:00Z
updated: 2026-03-05T12:00:00Z
---

## Current Focus

hypothesis: Two remaining bugs after plan 15-09 fix: (A) DetectMovedWindow never fires when window returns to home desktop, (B) OnWindowActivated_CheckMisplaced does not dismiss overlay when window returns home
test: Full code trace of both event paths for "move back to original desktop" scenario
expecting: N/A - trace complete, root causes confirmed
next_action: Return diagnosis

## Symptoms

expected: Overlay should auto-dismiss if moved back to original desktop, or update in-place if moved to a third desktop
actual: Overlay stays stuck showing the first desktop's info; never updates or dismisses on subsequent moves
errors: None (no crashes)
reproduction: Show overlay by dragging window to Desktop B, then drag window back to Desktop A (or to Desktop C) while overlay is active
started: Since DRAG-01 implementation; plan 15-09 attempted to fix but left gaps

## Eliminated

- hypothesis: Unconditional _isDragOverlayActive guard blocks all re-entry
  evidence: Plan 15-09 replaced the unconditional guard with context-aware re-entry (lines 3604-3626). The guard now has three branches: auto-dismiss on return, no-op on same target, fall-through for third desktop.
  timestamp: 2026-03-05

- hypothesis: OnWindowActivated_CheckMisplaced still blocked by _isDragOverlayActive guard
  evidence: Plan 15-09 removed the guard at line 3868. The comment now reads "Don't skip when overlay active -- ShowDragOverlayAsync handles re-entry". The method no longer returns early when overlay is active.
  timestamp: 2026-03-05

- hypothesis: Third-desktop update path is broken
  evidence: Code trace confirms: when window moves from A->B then to C, DetectMovedWindow sees expected="A" vs current="C" (they differ), fires event with fromGuid="A", toGuid="C". ShowDragOverlayAsync re-entry branch: toGuid "C" != _dragFromDesktopGuid "A" (not a move-back), toGuid "C" != _dragToDesktopGuid "B" (not same target), so it falls through to update overlay in-place. This path appears correct.
  timestamp: 2026-03-05

- hypothesis: COM vtable mismatch causing GetName() to fail
  evidence: IVirtualDesktop GUID 3F07F4BE is same for builds 22621 and 26100+; vtable layout documented as stable across 22H2/23H2/24H2. Build 26200 maps correctly to the 26100 GuidSet.
  timestamp: 2026-03-05

- hypothesis: HString marshaling broken on build 26200
  evidence: [return: MarshalAs(UnmanagedType.HString)] is standard .NET COM interop; GetName() is non-PreserveSig so failures throw (caught by try/catch). The marshaling pattern is identical to GetWallpaperPath which would show same issue if HString was broken.
  timestamp: 2026-03-05

## Evidence

- timestamp: 2026-03-05
  checked: ShowDragOverlayAsync re-entry logic (lines 3604-3626) after plan 15-09 fix
  found: Re-entry logic IS implemented. Three branches exist - auto-dismiss when toGuid matches _dragFromDesktopGuid (line 3607), no-op when toGuid matches _dragToDesktopGuid (line 3617), fall-through for different third desktop (line 3620). The code is structurally correct.
  implication: The re-entry code exists but is NEVER REACHED for the move-back scenario. The problem is upstream.

- timestamp: 2026-03-05
  checked: DetectMovedWindow (VirtualDesktopService.cs lines 506-548) for "move back" scenario
  found: |
    CRITICAL BUG A FOUND.
    DetectMovedWindow compares `window.DesktopGuid` (which is `_desktopGuid` via the public property at line 210) against the COM-reported current desktop. `_desktopGuid` is the window's *logical* home desktop and is NEVER updated while the overlay is active (it only changes in DragKeepHere_Click at line 3733).

    Scenario trace:
    1. Window starts on Desktop A. _desktopGuid = "A".
    2. User drags to Desktop B. DetectMovedWindow: expected="A" != current="B" -> fires event. Overlay shows.
    3. User drags back to Desktop A. DetectMovedWindow: expected="A" vs current="A" -> THEY MATCH. No event fires.
    4. ShowDragOverlayAsync is NEVER called. The auto-dismiss branch (line 3607) is dead code for this scenario.

    The event-based path cannot detect "move back to home" because from DetectMovedWindow's perspective, the window is now where it belongs.
  implication: The entire auto-dismiss-on-return feature is non-functional via the COM event path

- timestamp: 2026-03-05
  checked: OnWindowActivated_CheckMisplaced (lines 3866-3917) for "move back" scenario
  found: |
    CRITICAL BUG B FOUND.
    When window returns to Desktop A and gains focus:
    - currentGuid = "A", _desktopGuid = "A" -> they ARE equal
    - Enters `else if (_isMisplaced)` branch at line 3902
    - This branch clears _isMisplaced and removes "(misplaced)" from title
    - BUT it does NOT call HideDragOverlayAsync() or dismiss the overlay
    - The overlay remains visible with stale information even though the window is back on its correct desktop

    The plan 15-09 fix removed the _isDragOverlayActive guard (correct), but the else-if branch that handles "window is back on correct desktop" was not updated to also dismiss the drag overlay.
  implication: Even the backup activation-based path fails to dismiss the overlay on move-back

- timestamp: 2026-03-05
  checked: OnWindowActivated_CheckMisplaced for "third desktop" scenario
  found: |
    When window is on Desktop C, currentGuid="C" != _desktopGuid="A", so it enters the first branch (line 3880-3901) and calls ShowDragOverlayAsync("A", "C", toName). If the COM event already updated _dragToDesktopGuid to "C", this is a no-op (same target). If activation fires first (race), it could trigger the update. Either way, the third-desktop scenario has a working path via DetectMovedWindow since expected="A" != current="C".
  implication: Third-desktop update is reachable via COM event path; activation path is a redundant backup

- timestamp: 2026-03-05
  checked: Source name logic (lines 3646-3670) after plan 15-09 fix
  found: Source name now queries live COM via VirtualDesktopService.GetAllDesktops() instead of stale DB. This was fixed by plan 15-09. The old DatabaseService.GetDesktopNameAsync() call is gone.
  implication: BUG 1(a) from previous diagnosis has been fixed

- timestamp: 2026-03-05
  checked: DragKeepHere_Click (lines 3722-3761) after plan 15-09 fix
  found: Line 3743 now reads `string name = targetInfo?.Name ?? _dragToDesktopName ?? ""` -- prefers fresh COM name from targetInfo, falls back to _dragToDesktopName. This was fixed by plan 15-09.
  implication: BUG 1(c) from previous diagnosis has been fixed

## Resolution

root_cause: |
  Two bugs prevent the overlay from refreshing/dismissing on subsequent window moves.
  Both relate to the "move back to original desktop" scenario. The "third desktop"
  scenario appears to work correctly via the COM event path.

  BUG A - DetectMovedWindow cannot detect "move back to home" (VirtualDesktopService.cs line 525)

    File: JoJot/Services/VirtualDesktopService.cs line 525
    Code: `if (!currentDesktopGuid.Equals(expectedDesktopGuid, OrdinalIgnoreCase))`
    Problem: expectedDesktopGuid comes from window.DesktopGuid which is _desktopGuid --
    the window's logical home. When the window moves back to its home desktop, current
    equals expected, so the comparison passes and no event fires. The re-entry auto-dismiss
    code in ShowDragOverlayAsync (line 3607) is unreachable for this scenario.
    Fix direction: DetectMovedWindow needs to also check if the window has a pending drag
    overlay active (_isDragOverlayActive) and fire the event even when the window returns
    to its home desktop, OR OnWindowActivated_CheckMisplaced needs to handle this case.

  BUG B - OnWindowActivated_CheckMisplaced doesn't dismiss overlay (MainWindow.xaml.cs line 3902)

    File: JoJot/MainWindow.xaml.cs line 3902-3911
    Code: `else if (_isMisplaced) { _isMisplaced = false; ... }`
    Problem: When window returns to correct desktop, this branch clears misplaced state
    and title badge but does NOT dismiss the drag overlay. It needs to also call
    HideDragOverlayAsync() and clean up pending_moves.
    Fix direction: Add overlay dismissal to the else-if branch:
      - Call await DatabaseService.DeletePendingMoveAsync(_desktopGuid)
      - Call await HideDragOverlayAsync()
    This is the simpler and more reliable fix since OnWindowActivated fires reliably
    when the user switches back to the desktop.

  RECOMMENDED FIX: Fix Bug B in OnWindowActivated_CheckMisplaced. When the window is
  back on its correct desktop AND the drag overlay is still active, dismiss the overlay.
  This handles the move-back scenario without needing to change VirtualDesktopService.
  The else-if branch at line 3902 should check _isDragOverlayActive and call
  HideDragOverlayAsync() + cleanup.

fix: (not applied - diagnosis only)
verification:
files_changed: []
