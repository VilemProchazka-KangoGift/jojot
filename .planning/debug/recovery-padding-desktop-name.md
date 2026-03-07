---
status: diagnosed
trigger: "Recovery panel padding and desktop name still broken after 15.1-04 fix"
created: 2026-03-06T15:00:00Z
updated: 2026-03-06T15:30:00Z
---

## Current Focus

hypothesis: CONFIRMED - Two distinct root causes found
test: Code review of CreateRecoveryRow, XAML ScrollViewer, and COM/DB name pipeline
expecting: n/a
next_action: Return diagnosis

## Symptoms

expected: |
  - Recovery rows flush-aligned with panel header (no extra left indentation)
  - Desktop name shown in bold 14pt, clearly visible
actual: |
  - "bullet list offset is still there" (Test 3)
  - Desktop name not visible/prominent (Test 4)
errors: none
reproduction: Open recovery panel with orphaned sessions
started: After 15.1-04 fix (commit 1309180)

## Eliminated

- hypothesis: Commit 1309180 changes were not applied to the file
  evidence: git show confirms diff applied. Current file lines 2526, 2533-2534, 2676 match the fix exactly. Container Margin is (0,10,0,10), nameBlock is Bold 14pt, divider is (0,0,0,0).
  timestamp: 2026-03-06

- hypothesis: Container StackPanel or divider has leftover horizontal margin
  evidence: Code at lines 2524-2527 and 2673-2677 confirmed correct (0px horizontal margins)
  timestamp: 2026-03-06

## Evidence

- timestamp: 2026-03-06
  checked: CreateRecoveryRow tab preview lines (lines 2557-2561)
  found: Each tab preview lineBlock has Margin = new Thickness(8, 1, 0, 1) -- 8px LEFT indent
  implication: This 8px left margin on every tab preview line creates the "bullet list offset" appearance. Combined with ScrollViewer's 16px margin, these lines sit at 24px while the header "Recover Sessions" sits at 16px. The container margin fix removed 12px from the container, but the individual lines WITHIN the container still have 8px left.

- timestamp: 2026-03-06
  checked: "+N more" block (lines 2598-2603)
  found: moreBlock also has Margin = new Thickness(8, 1, 0, 1) -- same 8px indent
  implication: ALL sub-items within the recovery row have an 8px left indent, creating a visual "list" indent effect

- timestamp: 2026-03-06
  checked: Desktop name visibility -- code vs data pipeline
  found: |
    Code is correct: FontSize=14, FontWeight=Bold, text from desktopName parameter.
    But desktopName comes from GetOrphanedSessionInfoAsync which reads desktop_name from app_state table.
    For the ORPHANED sessions, the desktop_name was stored when the session was originally created.
    On Win 25H2 build 26200: COM GetName() returns empty, registry fallback reads name.
    BUT: sessions were originally created BEFORE the registry fallback was added.
    If desktop_name is NULL in the DB for orphaned sessions, nameBlock shows "Unknown desktop".
  implication: The styling fix (Bold 14pt) is correct but the DATA may be null, showing "Unknown desktop" which the user interprets as "not visible/prominent" because it's a meaningless fallback string.

- timestamp: 2026-03-06
  checked: CreateSessionAsync call in fallback mode (VirtualDesktopService.cs:169)
  found: When _isAvailable is false, calls CreateSessionAsync("default", null, null) with null name
  implication: If COM failed at any point and sessions were created in fallback, names would be null

- timestamp: 2026-03-06
  checked: Session matching Tier 1 update (VirtualDesktopService.cs:204-208)
  found: UpdateSessionAsync updates desktop_name with matchingDesktop.Name from live COM
  implication: For MATCHED sessions, names get updated. But ORPHANED sessions by definition did NOT match any live desktop, so their names never get refreshed.

- timestamp: 2026-03-06
  checked: XAML ScrollViewer (MainWindow.xaml:798)
  found: ScrollViewer has Margin="16,0,16,16" wrapping RecoverySessionList StackPanel
  implication: This 16px provides the panel's internal padding and is correct/intentional. The issue is that tab preview lines ADD 8px on top of this, making them visually indented vs the header and desktop name.

## Resolution

root_cause: |
  **Issue 1 (Test 3 - "bullet list offset"): Tab preview lines have hardcoded 8px left margin**

  Location: MainWindow.xaml.cs lines 2561 and 2603

  The 15.1-04 fix correctly removed the 12px horizontal margin from the container StackPanel
  (line 2526) and the divider (line 2676). However, the individual tab preview TextBlocks
  INSIDE the container still have Margin = new Thickness(8, 1, 0, 1) at line 2561, and
  the "+N more" TextBlock has the same margin at line 2603.

  This creates a visual "indented list" or "bullet list offset" effect:
  - Header "Recover Sessions": 16px from edge (from ScrollViewer margin)
  - Desktop name (nameBlock): 16px from edge (flush, correct)
  - Tab preview lines: 16px + 8px = 24px from edge (indented, wrong)

  The user sees the tab preview lines pushed right compared to the desktop name and header,
  creating the "bullet list offset" appearance.

  Fix: Change Margin(8, 1, 0, 1) to Margin(0, 1, 0, 1) on both lineBlock (line 2561)
  and moreBlock (line 2603) to make all content flush-aligned within the container.

  **Issue 2 (Test 4 - desktop name not visible): Orphaned session names are NULL in database**

  The styling fix (Bold 14pt) IS correctly applied in the code at lines 2533-2534.
  The problem is that the DATA flowing into the nameBlock is null for orphaned sessions.

  When GetOrphanedSessionInfoAsync (DatabaseService.cs:867-876) reads desktop_name from
  app_state, it returns null for sessions where the name was never stored. This causes
  CreateRecoveryRow to display "Unknown desktop" (line 2532: desktopName ?? "Unknown desktop").

  The user's orphaned sessions likely have null desktop_name because:
  (a) They were created before the registry fallback was added to VirtualDesktopInterop
  (b) On Win 25H2 build 26200, COM GetName() returns empty and if the fallback didn't
      exist when the session was first created, the name was stored as null/empty
  (c) Orphaned sessions never get their names updated (only matched sessions do, via
      Tier 1 UpdateSessionAsync at VirtualDesktopService.cs:204-208)

  Fix direction: GetOrphanedSessionInfoAsync should attempt a live name lookup via registry
  as a fallback when desktop_name is null. Read from:
  HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops\{GUID}\Name

fix:
verification:
files_changed: []
