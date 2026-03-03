# Phase 10: Window Drag & Crash Recovery - Context

**Gathered:** 2026-03-03
**Status:** Ready for planning

<domain>
## Phase Boundary

When a user drags a JoJot window to another virtual desktop, detect the move, show a lock overlay for conflict resolution (reparent, merge, or cancel), and recover gracefully from crashes mid-drag via the pending_moves table. DRAG-01 through DRAG-10.

</domain>

<decisions>
## Implementation Decisions

### Lock overlay layout
- Centered vertically in the window, card-style over a semi-transparent dark overlay (rgba 0,0,0,0.65)
- Context message above buttons: show target desktop name + situation ("Moved to Work" / "This desktop already has 5 notes")
- Friendly button labels: "Keep here" (reparent), "Merge notes" (merge), "Go back" (cancel)
- Same layout for 2-button case (no existing session: Keep here + Go back) and 3-button case (existing session: all three); buttons change dynamically, not the layout
- Content visible but non-interactive behind the overlay

### Merge behavior
- Merged tabs append at the bottom of the unpinned section in the target window
- Pinned tabs from the source window stay pinned in the target window (join bottom of existing pins)
- Toast notification after merge: "Merged N notes from {source desktop name}"
- Target window keeps its currently active tab focused; merged tabs don't steal focus

### Transition animations
- Lock overlay: 150ms fade-in from transparent to full opacity (matches existing toast timing)
- Cancel ("Go back"): overlay fades out over 150ms while COM API moves window back to origin desktop
- Reparent ("Keep here"): overlay fades out over 150ms, window title updates to new desktop name — that's the feedback
- Merge ("Merge notes"): overlay fades out, then standard WPF window close on the dragged window; no fancy transitions

### Cancel failure escalation (DRAG-07)
- When "Go back" fails (MoveWindowToDesktop COM call fails), replace the "Go back" button with "Retry"
- Show instruction text below: "If retry fails, move this window back manually via Win+Tab"
- "Keep here" and "Merge notes" buttons remain available as alternatives

### Warning badge (DRAG-10)
- Append "(misplaced)" to the window title when GUID mismatch detected: "JoJot — Desktop 1 (misplaced)"
- Uses existing title update system, no custom icons or toolbar modifications
- When a misplaced window gains focus (user switches to that desktop), show the lock overlay automatically — prevents misplaced state from persisting silently

### Crash recovery
- On startup, read pending_moves table and restore windows to their origin desktop before showing them
- After crash recovery, show a toast: "Recovered window from interrupted move"
- Existing stub in App.xaml.cs:124 (`// Phase 10: await DatabaseService.ResolvePendingMovesAsync();`) is the integration point

### Claude's Discretion
- Detection mechanism for window-to-desktop drag (IVirtualDesktopNotification callbacks vs polling vs hybrid)
- Exact overlay XAML structure and button styling
- pending_moves write/read implementation details
- Timing of overlay appearance relative to COM notification
- Error handling for COM failures during reparent/merge operations
- How "second drag while overlay active is ignored" is implemented (DRAG-08)

</decisions>

<specifics>
## Specific Ideas

- Button labels should be outcome-oriented: "Keep here" / "Merge notes" / "Go back" — not technical jargon like "Reparent" / "Merge" / "Cancel"
- Merge toast reuses the existing toast notification pattern from Phase 5 (deletion toast)
- Recovery toast uses the same toast pattern, just different message
- Overlay pattern is consistent with existing confirmation overlay used for bulk deletes

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `pending_moves` table already exists in DatabaseService schema (window_id, from_desktop, to_desktop, detected_at)
- Phase 10 stub comment at `App.xaml.cs:124`: `// Phase 10: await DatabaseService.ResolvePendingMovesAsync();`
- `IVirtualDesktopManager.MoveWindowToDesktop(IntPtr, ref Guid)` — COM API for programmatic window moves (Cancel/Go back flow)
- `VirtualDesktopInterop.GetWindowDesktopId(IntPtr)` — check which desktop a window is on
- Toast notification system from Phase 5 — reuse for merge and recovery toasts
- Confirmation overlay from Phase 8 — similar centered overlay pattern for lock overlay
- `_windows` dictionary in `App.xaml.cs` — maps desktop GUIDs to MainWindow instances

### Established Patterns
- VirtualDesktopNotificationListener handles COM callbacks (DesktopRenamed, CurrentDesktopChanged, DesktopCreated, DesktopDestroyed)
- Window title format: "JoJot — {desktop name}" with live updates via DesktopRenamed event
- Static service pattern (VirtualDesktopService, DatabaseService) — new drag logic should follow same pattern
- 150ms animation timing used consistently (toast slide-up)

### Integration Points
- `VirtualDesktopNotificationListener` — needs new event for window move detection (ViewVirtualDesktopChanged currently a no-op)
- `App.xaml.cs` startup sequence — pending_moves resolution goes after session matching, before window creation
- `App._windows` dictionary — must be updated when reparent changes a window's desktop GUID
- `DatabaseService` — needs CRUD methods for pending_moves table
- `MainWindow.xaml` — lock overlay XAML added to the window template

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 10-window-drag-crash-recovery*
*Context gathered: 2026-03-03*
