---
status: diagnosed
trigger: "Move overlay shows 'unknown desktop' instead of actual name; title bar still says 'Desktop N'"
created: 2026-03-05T12:00:00Z
updated: 2026-03-05T12:30:00Z
---

## Current Focus

hypothesis: Two related issues both caused by desktop_name being empty/null in app_state table and in live COM queries
test: Traced full data flow from COM GetName() through database storage to UI display
expecting: GetName() returns empty for un-renamed desktops; app_state stores empty; overlay shows "Unknown desktop"
next_action: Return diagnosis

## Symptoms

expected: Move overlay shows "From: {actual desktop name}"; title bar shows "JoJot -- {desktop name}"
actual: Move overlay shows "From: Unknown desktop"; title bar shows "JoJot -- Desktop 5"
errors: None (silent empty-string fallback)
reproduction: Open JoJot on a virtual desktop, drag window to another desktop, observe overlay source name and title
started: Since R2-MOVE-01 feature was added (Phase 15)

## Eliminated

(none -- root cause found on first hypothesis)

## Evidence

- timestamp: 2026-03-05T12:05:00Z
  checked: IVirtualDesktop COM interface vtable layout against MScholtes reference implementation
  found: Vtable layout matches exactly (IsViewVisible, GetId, GetName, GetWallpaperPath, IsRemote) with same GUID 3F07F4BE
  implication: COM interop is correctly aligned; GetName() calls the right vtable slot

- timestamp: 2026-03-05T12:10:00Z
  checked: Windows COM behavior for IVirtualDesktop.GetName()
  found: GetName() returns empty HSTRING for desktops that have not been explicitly renamed by the user in Task View. Windows shell generates "Desktop N" display names client-side; they are NOT stored as the desktop name.
  implication: For most users (who don't rename desktops), GetName() always returns empty

- timestamp: 2026-03-05T12:15:00Z
  checked: ShowDragOverlayAsync source name lookup (MainWindow.xaml.cs:3543-3552)
  found: Reads from DatabaseService.GetDesktopNameAsync(_desktopGuid) which queries app_state.desktop_name. If null/empty, shows "From: Unknown desktop"
  implication: Since GetName() returns empty, desktop_name in app_state is stored as empty/null, so overlay always shows "Unknown desktop"

- timestamp: 2026-03-05T12:18:00Z
  checked: UpdateDesktopTitle logic (MainWindow.xaml.cs:1893-1907)
  found: Falls through to "Desktop {index+1}" when name is empty but index is known. This is expected fallback.
  implication: Title shows "Desktop 5" because name is empty and index is 4 (zero-based). The title fallback works but doesn't match user expectation.

- timestamp: 2026-03-05T12:20:00Z
  checked: Move overlay code has no equivalent fallback
  found: DragOverlaySourceName.Text uses only database-stored name. No fallback to "Desktop N" format when name is empty.
  implication: Overlay and title use different code paths with different fallback behavior

- timestamp: 2026-03-05T12:25:00Z
  checked: Where desktop_name gets stored in app_state
  found: CreateSessionAsync and UpdateSessionAsync both store the name from GetAllDesktops() which calls GetName(). If GetName() returns "", the database stores NULL or empty.
  implication: The database never has a useful display name for un-renamed desktops

## Resolution

root_cause: |
  Two related issues with the same underlying cause:
  1. IVirtualDesktop.GetName() returns empty string for desktops not explicitly renamed by the user in Windows Task View.
     Windows generates "Desktop N" labels client-side in the shell UI; these are NOT stored as COM-accessible names.
  2. The move overlay source name code (ShowDragOverlayAsync) reads desktop_name from app_state, which is empty,
     and has NO fallback to generate "Desktop N" from the index -- it just shows "Unknown desktop".
  3. The title bar code (UpdateDesktopTitle) DOES have the correct fallback ("Desktop {index+1}") but the overlay doesn't use it.

fix: (not applied -- diagnosis only)
verification: (not verified -- diagnosis only)
files_changed: []
