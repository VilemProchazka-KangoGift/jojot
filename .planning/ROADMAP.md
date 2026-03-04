# Roadmap: JoJot

## Milestones

- ✅ **v1.0 MVP** — Phases 1-10 (shipped 2026-03-03)
- 🚧 **v1.1 Polish & Stability** — Phases 11-14 (in progress)

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

- [x] **Phase 11: Critical Bug Fixes** — Eliminate stack overflow crashes on pin/unpin and delete, and fix tab rename freeze (completed 2026-03-03)
- [x] **Phase 12: Tab Panel UX** — Replace border highlight with background highlight, add pin icon, improve title sizing, make panel user-resizable, and verify drag-to-reorder (completed 2026-03-04)
- [ ] **Phase 13: Theme, Display & Menu Polish** — Fix dark mode tab legibility, change font size display to percentages, verify window title shows desktop name, and fix hamburger menu dismiss behavior
- [ ] **Phase 14: Installer** — Produce a Windows MSI or MSIX installer for distribution

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
**Plans**: TBD

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
| 13. Theme, Display & Menu Polish | v1.1 | 0/? | Not started | - |
| 14. Installer | v1.1 | 0/? | Not started | - |
