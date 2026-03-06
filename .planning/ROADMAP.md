# Roadmap: JoJot

## Milestones

- ✅ **v1.0 MVP** — Phases 1-10 (shipped 2026-03-03)
- 🚧 **v1.1 Polish & Stability** — Phases 11-15 (in progress)

## Phases

<details>
<summary>✅ v1.0 MVP (Phases 1-10, 14 total) — SHIPPED 2026-03-03</summary>

- [x] Phase 1: Foundation (3/3 plans) — completed 2026-03-02
- [x] Phase 2: Virtual Desktop Integration (3/3 plans) — completed 2026-03-02
- [x] Phase 3: Window & Session Management (2/2 plans) — completed 2026-03-02
- [x] Phase 4: Tab Management (3/3 plans) — completed 2026-03-02
- [x] Phase 5: Deletion & Toast (2/2 plans) — completed 2026-03-02
- [x] Phase 6: Editor & Undo (3/3 plans) — completed 2026-03-03
- [x] Phase 7: Theming & Toolbar (2/2 plans) — completed 2026-03-03
- [x] Phase 8: Menus, Context Actions & Orphaned Sessions (3/3 plans) — completed 2026-03-03
- [x] Phase 8.1: Gap Closure — Code Fixes (1/1 plan) — completed 2026-03-03
- [x] Phase 8.2: Gap Closure — Verification (2/2 plans) — completed 2026-03-03
- [x] Phase 9: File Drop, Preferences, Hotkeys & Keyboard (3/3 plans) — completed 2026-03-03
- [x] Phase 10: Window Drag & Crash Recovery (2/2 plans) — completed 2026-03-03
- [x] Phase 10.1: Gap Closure — Integration Fixes (1/1 plan) — completed 2026-03-03
- [x] Phase 10.2: Gap Closure — Verification (1/1 plan) — completed 2026-03-03

Full details: `.planning/milestones/v1.0-ROADMAP.md`

</details>

### 🚧 v1.1 Polish & Stability (In Progress)

**Milestone Goal:** Fix critical crashes and freezes, polish the tab panel and UI, and ship a Windows installer — based on first-round manual review of v1.0.

- [x] **Phase 11: Critical Bug Fixes** — Eliminate stack overflow crashes on pin/unpin and delete, and fix tab rename freeze (completed 2026-03-03)
- [x] **Phase 12: Tab Panel UX** — Replace border highlight with background highlight, add pin icon, improve title sizing, make panel user-resizable, and verify drag-to-reorder (completed 2026-03-04)
- [x] **Phase 13: Theme, Display & Menu Polish** — Fix dark mode tab legibility, change font size display to percentages, verify window title shows desktop name, and fix hamburger menu dismiss behavior (completed 2026-03-04)
- [ ] **Phase 14: Installer** — Produce a Windows MSI or MSIX installer for distribution
- [x] **Phase 15: Review Round 2 — UI/UX Bug Fixes & Polish** — Fix note persistence bug, improve tab interactions, enhance drag-and-drop, refine preferences panel, redesign session recovery, and polish startup/move-to-desktop flows (completed 2026-03-05)
- [ ] **Phase 15.1: Recovery Panel, Tab Rename & Reorder Fixes** — Make recovery items full-width with tab info, fix escape-to-cancel rename, replace drag ghost with fade-out (gap closure in progress)

## Phase Details

### Phase 11: Critical Bug Fixes
**Goal**: The app runs without crashes or freezes during normal tab operations
**Depends on**: Nothing (fixes to existing code, highest priority)
**Requirements**: BUG-01, BUG-02, BUG-03
**Success Criteria** (what must be TRUE):
  1. User can pin and unpin a tab repeatedly without the app crashing
  2. User can delete tabs (single or bulk) without the app crashing
  3. User can double-click a tab to rename it, type a new name, and press Enter without the app freezing
  4. All three operations work correctly after the fix with no regression in normal tab behavior
**Plans**: TBD

### Phase 12: Tab Panel UX
**Goal**: The tab panel is visually clear, pin-aware, and user-resizable
**Depends on**: Phase 11 (stable app required for visual testing)
**Requirements**: TABUX-01, TABUX-02, TABUX-03, TABUX-04, TABUX-05
**Success Criteria** (what must be TRUE):
  1. The selected tab is highlighted by a distinct background color, not a left-edge border
  2. Pinned tabs display a visible pin icon that distinguishes them from unpinned tabs
  3. When the delete icon is visible on a tab, the title text shortens to fill the remaining space; when no delete icon is shown, the title spans the full tab width
  4. User can drag the divider between the tab panel and editor to resize the panel width to their preference
  5. User can drag a tab to a new position in the list and it stays in that order after release
**Plans**: 2 plans
- [x] 12-01-PLAN.md — Tab visual overhaul (background highlight, Fluent pin icon, adaptive title width, drag-reorder verification)
- [x] 12-02-PLAN.md — Resizable tab panel (GridSplitter, width persistence, human verification)

### Phase 13: Theme, Display & Menu Polish
**Goal**: Dark mode is legible, font size feedback is clear, and menus dismiss predictably
**Depends on**: Phase 11
**Requirements**: THEME-01, THEME-02, WIN-01, WIN-02
**Success Criteria** (what must be TRUE):
  1. In dark mode, tab names are readable against the tab background with sufficient contrast
  2. The font size indicator displays a percentage (e.g., "120%") rather than a point size, and tab labels scale with the font size change
  3. The window title bar shows the current virtual desktop name (matching Windows Task View)
  4. Clicking anywhere outside the hamburger menu popup closes it immediately
**Plans**: 2 plans
- [x] 13-01-PLAN.md — Dark mode tab contrast fix and font size percentage display with tab label scaling (THEME-01, THEME-02)
- [x] 13-02-PLAN.md — Hamburger menu dismiss fix and window title verification (WIN-01, WIN-02)

### Phase 15: Review Round 2 — UI/UX Bug Fixes & Polish
**Goal**: Address all issues from the second manual review — fix the critical note persistence bug, polish tab interactions, improve drag-and-drop visuals, refine preferences, redesign session recovery, and clean up startup and move-to-desktop flows
**Depends on**: Phase 13 (builds on existing v1.1 fixes)
**Requirements**:
  - **R2-BUG-01** (Critical): Only the first note retains text; all other notes lose content on any action (tab navigation, font size change)
  - **R2-MENU-01**: Do not show "Recover Sessions" menu item if there are no sessions to recover
  - **R2-FONT-01**: Reset button label should say "100%" instead of "Reset to 13pt"
  - **R2-FONT-02**: Tab titles are too large after font scaling — retain original relative size (tabs smaller than editor content)
  - **R2-FONT-03**: Tab bottom dates should scale with the font size resize
  - **R2-FONT-04**: Tab title font sizes randomly change between tabs (inconsistent sizing)
  - **R2-PREF-01**: Remove autosave delay preference (not user-configurable)
  - **R2-PREF-02**: Record global hotkey should temporarily disable the actual hotkey during recording (pressing same sequence minimizes window instead of recording)
  - **R2-TAB-01**: Add leeway to pin button press target (too precise); on hover, show a cross over the pin icon
  - **R2-TAB-02**: Increase the X (close) button size to match pin icon size
  - **R2-TAB-03**: For unpinned tabs, add a pin button to the left of the X button
  - **R2-DND-01**: Dragged tab should become invisible (blank space) with a "ghost" following the cursor
  - **R2-DND-02**: Do not show placement indicator lines for positions that wouldn't change the order (above and below the dragged item)
  - **R2-DROP-01**: File drag-and-drop from Explorer should work on the entire window (not just the toolbar); dropped file goes to first position (or first below pinned)
  - **R2-RECOVER-01**: Recover Sessions should be a sidebar (like preferences), show last desktop name, and remove the open button
  - **R2-STARTUP-01**: Automatically silently delete all empty notes on startup
  - **R2-MOVE-01**: Move-to-desktop card should show the source (original) desktop name
  - **R2-MOVE-02**: If there's already a window active on the target desktop, do not show the "keep here" button
**Success Criteria** (what must be TRUE):
  1. Switching between tabs preserves all note content — no text is lost
  2. "Recover Sessions" is hidden when no orphaned sessions exist
  3. Font size reset button shows "100%" and tab labels/dates scale proportionally without inconsistent sizing
  4. Autosave delay is removed from preferences; hotkey recording disables the live hotkey
  5. Pin/close buttons on tabs have adequate hit targets, unpinned tabs show both pin and close
  6. Dragging a tab shows a ghost cursor and hides the original; indicator lines only appear at valid new positions
  7. File drop works over the entire window and places the new tab at the top (below pinned)
  8. Recover Sessions appears as a sidebar with desktop name context
  9. Empty notes are silently cleaned up on startup
  10. Move-to-desktop shows source name and hides "keep here" when target already has a window
**Plans**: 11 plans (2 waves + gap closure)
- [x] 15-01-PLAN.md — Critical bug fix, font fixes, startup cleanup (R2-BUG-01, R2-FONT-01-04, R2-STARTUP-01)
- [x] 15-02-PLAN.md — Menu visibility, hotkey pause, autosave delay removal (R2-MENU-01, R2-PREF-01, R2-PREF-02)
- [x] 15-03-PLAN.md — Tab button layout redesign (R2-TAB-01, R2-TAB-02, R2-TAB-03)
- [x] 15-04-PLAN.md — Drag ghost adorner, smart indicators, full-window file drop (R2-DND-01, R2-DND-02, R2-DROP-01)
- [x] 15-05-PLAN.md — Recovery sidebar, tab previews, source name, keep-here visibility (R2-RECOVER-01, R2-MOVE-01, R2-MOVE-02)
- [x] 15-06-PLAN.md — Gap closure: tab hover glyphs, close icon, drag ghost snapshot, separator indicators (R2-TAB-01, R2-TAB-02, R2-DND-01)
- [x] 15-07-PLAN.md — Gap closure: file drop overlay tunneling, desktop name fallback, session reparent metadata (R2-DROP-01, R2-MOVE-01, R2-MENU-01)
- [x] 15-08-PLAN.md — Gap closure: tab hover layout redesign per user spec, drag ghost CaptureMode fix (R2-TAB-01, R2-TAB-02, R2-TAB-03, R2-DND-01)
- [x] 15-09-PLAN.md — Gap closure: file drop over editor TextBox, move overlay live COM names and refresh (R2-DROP-01, R2-MOVE-01)
- [x] 15-10-PLAN.md — Gap closure: tab hover height fix, drag ghost empty-space tracking (R2-TAB-01, R2-DND-01)
- [x] 15-11-PLAN.md — Gap closure: desktop name registry fallback for 25H2, move overlay dismiss on return (R2-MOVE-01, R2-MOVE-02)

### Phase 15.1: Recovery Panel, Tab Rename & Reorder Fixes (INSERTED)
**Goal**: Polish recovery panel layout, fix tab rename cancel behavior, and simplify drag-to-reorder visuals
**Depends on**: Phase 15 (builds on existing recovery panel and drag implementation)
**Requirements**:
  - **R3-RECOVER-01**: Recovery panel items should be full-width (not card-based), showing tab names and text excerpts
  - **R3-RENAME-01**: Pressing Escape during tab rename should cancel and restore the previous title
  - **R3-REORDER-01**: Remove the drag ghost concept; instead fade out the dragged item in place
**Success Criteria** (what must be TRUE):
  1. Recovery panel shows full-width items with tab names and content excerpts (not cards)
  2. Pressing Escape while renaming a tab restores the original title without saving
  3. Dragging a tab fades it out in the list instead of showing a ghost overlay following the cursor
**Plans**: 4 plans (2 original + 2 gap closure)
- [x] 15.1-01-PLAN.md — Escape-to-cancel rename fix and drag ghost replacement with in-place fade (R3-RENAME-01, R3-REORDER-01)
- [x] 15.1-02-PLAN.md — Recovery panel redesign from cards to flat rows with tab excerpts (R3-RECOVER-01)
- [x] 15.1-03-PLAN.md — Gap closure: fix LostMouseCapture aborting drag on capture transfer (R3-REORDER-01)
- [ ] 15.1-04-PLAN.md — Gap closure: fix recovery row padding and desktop name prominence (R3-RECOVER-01)

### Phase 14: Installer
**Goal**: JoJot can be installed on a clean Windows machine via a standard installer package
**Depends on**: Phase 13 (all fixes applied before packaging)
**Requirements**: DIST-01
**Success Criteria** (what must be TRUE):
  1. A Windows MSI or MSIX file exists that installs JoJot without requiring manual file placement
  2. The installed app launches correctly and all v1.1 behaviors are intact post-install
  3. The installer can be run on a machine that does not have .NET 10 installed (self-contained or runtime bundled)
**Plans**: TBD

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Foundation | v1.0 | 3/3 | Complete | 2026-03-02 |
| 2. Virtual Desktop Integration | v1.0 | 3/3 | Complete | 2026-03-02 |
| 3. Window & Session Management | v1.0 | 2/2 | Complete | 2026-03-02 |
| 4. Tab Management | v1.0 | 3/3 | Complete | 2026-03-02 |
| 5. Deletion & Toast | v1.0 | 2/2 | Complete | 2026-03-02 |
| 6. Editor & Undo | v1.0 | 3/3 | Complete | 2026-03-03 |
| 7. Theming & Toolbar | v1.0 | 2/2 | Complete | 2026-03-03 |
| 8. Menus, Context & Orphans | v1.0 | 3/3 | Complete | 2026-03-03 |
| 8.1. Gap Closure — Code | v1.0 | 1/1 | Complete | 2026-03-03 |
| 8.2. Gap Closure — Verification | v1.0 | 2/2 | Complete | 2026-03-03 |
| 9. File Drop, Prefs, Hotkeys | v1.0 | 3/3 | Complete | 2026-03-03 |
| 10. Window Drag & Recovery | v1.0 | 2/2 | Complete | 2026-03-03 |
| 10.1. Gap Closure — Integration | v1.0 | 1/1 | Complete | 2026-03-03 |
| 10.2. Gap Closure — Verification | v1.0 | 1/1 | Complete | 2026-03-03 |
| 11. Critical Bug Fixes | v1.1 | Complete    | 2026-03-03 | 2026-03-03 |
| 12. Tab Panel UX | v1.1 | 2/2 | Complete | 2026-03-04 |
| 13. Theme, Display & Menu Polish | v1.1 | 2/2 | Complete | 2026-03-04 |
| 15.1. Recovery, Rename, Reorder | v1.1 | 3/4 | Gap closure | - |
| 14. Installer | v1.1 | 0/? | Not started | - |
| 15. Review Round 2 UI/UX | v1.1 | 11/11 | Complete | 2026-03-05 |
