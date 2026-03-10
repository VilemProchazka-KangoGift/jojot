# Roadmap: JoJot

## Milestones

- ✅ **v1.0 MVP** — Phases 1-10 (shipped 2026-03-03)
- ✅ **v1.1 Polish & Stability** — Phases 11-16 (shipped 2026-03-10)

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

<details>
<summary>✅ v1.1 Polish & Stability (Phases 11-16, 8 total) — SHIPPED 2026-03-10</summary>

- [x] Phase 11: Critical Bug Fixes (1 plan) — completed 2026-03-03
- [x] Phase 12: Tab Panel UX (2/2 plans) — completed 2026-03-04
- [x] Phase 13: Theme, Display & Menu Polish (2/2 plans) — completed 2026-03-04
- [x] Phase 14: Installer (2/2 plans) — completed 2026-03-10
- [x] Phase 15: Review Round 2 — UI/UX Bug Fixes & Polish (11/11 plans) — completed 2026-03-05
- [x] Phase 15.1: Recovery Panel, Tab Rename & Reorder Fixes (6/6 plans) — completed 2026-03-06
- [x] Phase 16: Tab Cleanup Panel (2/2 plans) — completed 2026-03-07

Full details: `.planning/milestones/v1.1-ROADMAP.md`

</details>

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
| 11. Critical Bug Fixes | v1.1 | 1/1 | Complete | 2026-03-03 |
| 12. Tab Panel UX | v1.1 | 2/2 | Complete | 2026-03-04 |
| 13. Theme, Display & Menu Polish | v1.1 | 2/2 | Complete | 2026-03-04 |
| 14. Installer | v1.1 | 2/2 | Complete | 2026-03-10 |
| 15. Review Round 2 UI/UX | v1.1 | 11/11 | Complete | 2026-03-05 |
| 15.1. Recovery, Rename, Reorder | v1.1 | 6/6 | Complete | 2026-03-06 |
| 16. Tab Cleanup Panel | v1.1 | 2/2 | Complete | 2026-03-07 |
| 01. In-editor find panel with Ctrl+F | v1.2 | 3/3 | Complete (verification pending) | 2026-03-10 |

### Phase 1: Add in-editor find panel with Ctrl+F

**Goal:** Replace the inline EditorFindBar with a full-featured find-and-replace side panel, including real-time search, match highlighting via adorner overlay, case/whole-word toggles, and replace operations
**Requirements:** FIND-01 (panel UI), FIND-02 (replace), FIND-03 (highlighting), FIND-04 (keyboard shortcuts), FIND-05 (cleanup old find bar)
**Depends on:** None (standalone feature)
**Plans:** 3 plans

Plans:
- [x] 01-01-PLAN.md — Enhanced find engine + FindReplacePanel UserControl + theme colors (completed 2026-03-10)
- [x] 01-02-PLAN.md — MainWindow integration, keyboard shortcuts, replace operations, cleanup (completed 2026-03-10)
- [x] 01-03-PLAN.md — TextBoxHighlightAdorner + end-to-end visual verification (completed 2026-03-10)
