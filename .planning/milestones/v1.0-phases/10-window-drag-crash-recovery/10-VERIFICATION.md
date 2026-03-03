---
phase: 10-window-drag-crash-recovery
verified: 2026-03-03T21:00:00Z
status: passed
score: 10/10 requirements verified
re_verification: false
---

# Phase 10: Window Drag & Crash Recovery Verification Report

**Phase Goal:** Detect window drags between virtual desktops via COM notification, present a lock overlay with reparent/merge/cancel resolution flows, and recover from crashes using the pending_moves table.
**Verified:** 2026-03-03T21:00:00Z
**Status:** PASSED
**Re-verification:** No — gap closure verification (Phase 10.2)

## Goal Achievement

### Observable Truths (Plan 10-01: Infrastructure)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | ViewVirtualDesktopChanged fires a WindowViewChanged event on VirtualDesktopNotificationListener when a window is moved between desktops | VERIFIED | `VirtualDesktopNotificationListener.cs` line 33: `public event Action<IntPtr>? WindowViewChanged;`. Line 102: `WindowViewChanged?.Invoke(view)` inside the COM callback implementation |
| 2 | VirtualDesktopService exposes a public WindowMovedToDesktop event with args (windowHwnd, fromDesktopGuid, toDesktopGuid, toDesktopName) | VERIFIED | `VirtualDesktopService.cs` line 39: `public static event Action<IntPtr, string, string, string>? WindowMovedToDesktop;`. Line 539: `WindowMovedToDesktop?.Invoke(hwnd, expectedDesktopGuid, currentDesktopGuid, toDesktopName)` in `DetectMovedWindow()` |
| 3 | VirtualDesktopInterop.MoveWindowToDesktop(IntPtr hwnd, Guid desktopId) wraps the COM API call | VERIFIED | `VirtualDesktopInterop.cs` line 106: `public static void MoveWindowToDesktop(IntPtr hwnd, Guid desktopId)`. Line 109: `int hr = _manager!.MoveWindowToDesktop(hwnd, ref desktopId);` delegating to `IVirtualDesktopManager` COM interface |
| 4 | DatabaseService provides InsertPendingMoveAsync, DeletePendingMoveAsync, GetPendingMovesAsync, DeleteAllPendingMovesAsync | VERIFIED | `DatabaseService.cs`: `InsertPendingMoveAsync` (line 947), `DeletePendingMoveAsync` (line 980), `GetPendingMovesAsync` (line 1005), `DeleteAllPendingMovesAsync` (line 1026). All use `_writeLock` serialization pattern |
| 5 | PendingMove model record exists with Id, WindowId, FromDesktop, ToDesktop, DetectedAt fields | VERIFIED | `Models/PendingMove.cs` line 7: `public sealed record PendingMove(long Id, string WindowId, string FromDesktop, string? ToDesktop, string DetectedAt);` |

**Score:** 5/5 truths verified

### Observable Truths (Plan 10-02: Overlay UI & Resolution)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Dragging a JoJot window to a desktop with no existing session shows overlay with Keep here and Go back buttons | VERIFIED | `MainWindow.xaml.cs` `ShowDragOverlayAsync()` line 3218-3220: checks `App.Current.HasWindowForDesktop(toGuid)` — if false, `DragMergeBtn.Visibility = Visibility.Collapsed` (only Keep here and Go back shown) |
| 2 | Dragging to a desktop with existing session shows Keep here, Merge notes, and Go back buttons | VERIFIED | `MainWindow.xaml.cs` `ShowDragOverlayAsync()` line 3215: if `HasWindowForDesktop(toGuid)` is true, `DragMergeBtn.Visibility = Visibility.Visible` — all three buttons shown |
| 3 | Lock overlay is semi-transparent dark (rgba 0,0,0,0.65) with content visible but non-interactive | VERIFIED | `MainWindow.xaml` line 535: `DragOverlay` Grid at `Panel.ZIndex="200"`. Background `#A6000000` (0xA6 = 166/255 ~= 0.65 alpha). Content area beneath is visible through the overlay but not interactive due to ZIndex layering |
| 4 | Clicking Keep here re-scopes window and notes to new desktop, removes pending_moves row | VERIFIED | `MainWindow.xaml.cs` `DragKeepHere_Click()` line 3243: calls `DatabaseService.MigrateNotesDesktopGuidAsync` (reparent notes), updates `_desktopGuid`, calls `App.ReparentWindow()` (update registry), `DatabaseService.UpdateSessionDesktopGuidAsync`, `DatabaseService.DeletePendingMoveAsync`, then `HideDragOverlayAsync()` |
| 5 | Clicking Merge notes appends tabs to existing window preserving pin state, closes dragged window | VERIFIED | `MainWindow.xaml.cs` `DragMerge_Click()` line 3285: calls `DatabaseService.MigrateTabsPreservePinsAsync(sourceGuid, targetGuid)` (preserves pins), `App.ReloadWindowTabs(targetGuid)`, `App.ShowMergeToast(...)`, then closes source window. `DatabaseService.cs` `MigrateTabsPreservePinsAsync` (line 1078) explicitly preserves `pinned` flag |
| 6 | Clicking Go back moves window to original desktop via MoveWindowToDesktop COM API | VERIFIED | `MainWindow.xaml.cs` `DragCancel_Click()` line 3323: calls `VirtualDesktopService.TryMoveWindowToDesktop(hwnd, _dragFromDesktopGuid)`. VirtualDesktopService line 554: `TryMoveWindowToDesktop` wraps `VirtualDesktopInterop.MoveWindowToDesktop` with error handling |
| 7 | If Go back fails, Go back replaced with Retry and manual instruction text appears | VERIFIED | `MainWindow.xaml.cs` `DragCancel_Click()` lines 3344-3345: on failure, `DragCancelBtn.Content = "Retry"` and `DragCancelFailureText.Visibility = Visibility.Visible`. Failure text in MainWindow.xaml provides manual instruction |
| 8 | Second drag while overlay active is silently ignored | VERIFIED | `MainWindow.xaml.cs` line 60: `private bool _isDragOverlayActive;` field. `ShowDragOverlayAsync()` line 3191: `if (_isDragOverlayActive) return;` — early return guard. Also checked at line 654 for keyboard shortcuts and at line 3383 for misplaced check |
| 9 | Crash recovery on startup reads pending_moves and restores windows to origin desktop | VERIFIED | `App.xaml.cs` line 124: `await ResolvePendingMovesAsync()` in startup sequence. `ResolvePendingMovesAsync()` (line 391): reads `DatabaseService.GetPendingMovesAsync()`, for each move migrates tabs back to origin desktop using `MigrateTabsPreservePinsAsync`, shows recovery toast |
| 10 | Window title shows "(misplaced)" when desktop GUID mismatch detected | VERIFIED | `MainWindow.xaml.cs` `OnWindowActivated_CheckMisplaced()` line 3381: on Activated event, checks `VirtualDesktopService.GetWindowDesktopId(hwnd)` vs `_desktopGuid`. Mismatch at line 3401-3403: `if (!currentTitle.Contains("(misplaced)")) Title = currentTitle + " (misplaced)"`. Auto-shows drag overlay |
| 11 | Misplaced window gaining focus auto-shows lock overlay | VERIFIED | `MainWindow.xaml.cs` line 3414: after detecting mismatch and adding badge, calls `await ShowDragOverlayAsync(_desktopGuid, currentGuid, toName)` — overlay appears automatically on focus |

**Score:** 11/11 truths verified

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `VirtualDesktopNotificationListener` | `WindowViewChanged` event | COM callback | WIRED | Line 102: `WindowViewChanged?.Invoke(view)` in COM notification implementation |
| `VirtualDesktopService.OnWindowViewChanged` | `DetectMovedWindow` | Dispatcher dispatch | WIRED | Line 362: subscribes `_notificationListener.WindowViewChanged += OnWindowViewChanged`. Line 484: `OnWindowViewChanged` dispatches `DetectMovedWindow()` via `Dispatcher.BeginInvoke` at Normal priority |
| `DetectMovedWindow` | `WindowMovedToDesktop` event | GUID mismatch detection | WIRED | Line 506-539: iterates windows, checks `GetWindowDesktopId` against expected GUID, fires `WindowMovedToDesktop` event on mismatch |
| `MainWindow constructor` | `OnWindowMovedToDesktop` | Event subscription | WIRED | Line 143: `VirtualDesktopService.WindowMovedToDesktop += OnWindowMovedToDesktop`. Line 1632: unsubscribed on close |
| `OnWindowMovedToDesktop` | `ShowDragOverlayAsync` | HWND filter | WIRED | Line 3173: filters by `movedHwnd == hwnd`, then calls `ShowDragOverlayAsync(fromGuid, toGuid, toName)` |
| `ShowDragOverlayAsync` | `DatabaseService.InsertPendingMoveAsync` | Crash recovery | WIRED | Writes pending_move immediately on drag detection, before showing overlay |
| `DragKeepHere_Click` | `MigrateNotesDesktopGuidAsync` | Reparent | WIRED | Line 3243: calls `DatabaseService.MigrateNotesDesktopGuidAsync(oldGuid, newGuid)` then updates `_desktopGuid` and registry |
| `DragMerge_Click` | `MigrateTabsPreservePinsAsync` | Merge | WIRED | Line 3285: calls `DatabaseService.MigrateTabsPreservePinsAsync(sourceGuid, targetGuid)` preserving pin state |
| `DragCancel_Click` | `TryMoveWindowToDesktop` | Cancel/go-back | WIRED | Line 3328: `VirtualDesktopService.TryMoveWindowToDesktop(hwnd, _dragFromDesktopGuid)` with failure escalation |
| `App.xaml.cs startup` | `ResolvePendingMovesAsync` | Crash recovery | WIRED | Line 124: called in startup sequence before window creation |
| `MainWindow.Activated` | `OnWindowActivated_CheckMisplaced` | Misplaced detection | WIRED | Line 144: `Activated += OnWindowActivated_CheckMisplaced`. Checks GUID mismatch on every window activation |
| `App.xaml.cs` | `HasWindowForDesktop` | Merge availability | WIRED | Line 345: checks `_windows` dictionary for existing window on target desktop |
| `App.xaml.cs` | `ReparentWindow` | Registry update | WIRED | Line 354: updates `_windows` dictionary key from old GUID to new GUID |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| DRAG-01 | 10-01 | Detect window drag to another desktop via IVirtualDesktopNotification::OnWindowMovedToDesktop | SATISFIED | Full event chain: `VirtualDesktopNotificationListener.WindowViewChanged` (COM callback) -> `VirtualDesktopService.OnWindowViewChanged` -> `DetectMovedWindow` (GUID mismatch) -> `WindowMovedToDesktop` event -> `MainWindow.OnWindowMovedToDesktop` |
| DRAG-02 | 10-01, 10-02 | Write pending_moves row immediately on detection; apply lock overlay | SATISFIED | `ShowDragOverlayAsync()`: calls `DatabaseService.InsertPendingMoveAsync` immediately, then shows DragOverlay with 150ms fade-in animation |
| DRAG-03 | 10-02 | Lock overlay: semi-transparent dark (rgba 0,0,0,0.65), content visible but non-interactive | SATISFIED | `MainWindow.xaml` DragOverlay: Background="#A6000000" (alpha ~0.65), Panel.ZIndex="200" above all content. Content visible through semi-transparent overlay but not interactive |
| DRAG-04 | 10-02 | Reparent button (no existing session on target): re-scope window and all notes to new desktop | SATISFIED | `DragKeepHere_Click()`: `MigrateNotesDesktopGuidAsync` (reparent notes), updates `_desktopGuid`, `App.ReparentWindow()` (registry), `UpdateSessionDesktopGuidAsync` (DB), `DeletePendingMoveAsync` (cleanup) |
| DRAG-05 | 10-02 | Merge button (existing session on target): append tabs to existing window, close dragged window | SATISFIED | `DragMerge_Click()`: `MigrateTabsPreservePinsAsync` (preserves pin state), `App.ReloadWindowTabs` (refresh target), `App.ShowMergeToast` (notification), then closes source window |
| DRAG-06 | 10-01, 10-02 | Cancel button: move window back to original desktop via MoveWindowToDesktop | SATISFIED | `DragCancel_Click()`: `VirtualDesktopService.TryMoveWindowToDesktop(hwnd, _dragFromDesktopGuid)` wrapping `VirtualDesktopInterop.MoveWindowToDesktop` COM API |
| DRAG-07 | 10-02 | Cancel failure: replace Cancel with Retry + manual instruction message | SATISFIED | `DragCancel_Click()`: on failure, `DragCancelBtn.Content = "Retry"`, `DragCancelFailureText.Visibility = Visibility.Visible`. Subsequent clicks retry the same COM call |
| DRAG-08 | 10-02 | Second drag while overlay active is ignored | SATISFIED | `_isDragOverlayActive` bool field (line 60). `ShowDragOverlayAsync()` line 3191: `if (_isDragOverlayActive) return;` — silently ignores. Additional guards at lines 654 and 3383 |
| DRAG-09 | 10-01, 10-02 | Crash recovery: pending_moves rows on startup restore window to origin desktop | SATISFIED | `App.xaml.cs` `ResolvePendingMovesAsync()` (line 391): reads `GetPendingMovesAsync()`, migrates tabs back to origin using `MigrateTabsPreservePinsAsync`, shows recovery toast, clears pending_moves |
| DRAG-10 | 10-02 | Persistent warning badge in title bar when window GUID doesn't match current desktop GUID | SATISFIED | `OnWindowActivated_CheckMisplaced()` (line 3381): on every Activated event, checks desktop GUID mismatch. On mismatch: adds "(misplaced)" to title (line 3403), auto-shows drag overlay (line 3414). On correct desktop: removes badge (line 3423) |

**All 10 requirements satisfied. No orphaned requirements.**

### Human Verification Required

#### 1. Drag Detection Across Desktops
**Test:** Drag a JoJot window from Desktop 1 to Desktop 2
**Expected:** Lock overlay appears with resolution buttons on the target desktop
**Why human:** COM notification timing and cross-desktop window movement require multi-desktop testing

#### 2. Reparent Flow
**Test:** Drag window to empty desktop, click "Keep here"
**Expected:** Window stays, title updates to new desktop name, notes re-scoped
**Why human:** Multi-desktop state change requires runtime observation

#### 3. Merge Flow
**Test:** Drag window to desktop with existing JoJot window, click "Merge notes"
**Expected:** Source window closes, tabs appear in target window (pins preserved)
**Why human:** Cross-window tab migration and window lifecycle require runtime testing

#### 4. Cancel/Go Back Flow
**Test:** Click "Go back" in overlay
**Expected:** Window moves back to original desktop. If COM call fails: button changes to "Retry" with instruction text
**Why human:** COM MoveWindowToDesktop and failure escalation require runtime testing

#### 5. Crash Recovery
**Test:** Force-kill JoJot while drag overlay is active, then relaunch
**Expected:** On startup, pending_moves recovered, tabs migrated back, recovery toast shown
**Why human:** Crash simulation and recovery sequence require process lifecycle testing

### Gaps Summary

No gaps. All 10 Phase 10 DRAG requirements are substantively implemented in the codebase. The detection chain from COM notification through VirtualDesktopService to MainWindow overlay is fully wired. All three resolution flows (reparent, merge, cancel) handle database state, UI updates, and edge cases. Crash recovery reads pending_moves on startup and restores tabs to origin desktop. The misplaced window badge and auto-show overlay provide persistent GUID mismatch detection.

---

_Verified: 2026-03-03T21:00:00Z_
_Verifier: Claude (gsd-verifier, gap closure Phase 10.2)_
