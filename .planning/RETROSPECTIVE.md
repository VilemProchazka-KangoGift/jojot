# Project Retrospective

*A living document updated after each milestone. Lessons feed forward into future planning.*

## Milestone: v1.0 — MVP

**Shipped:** 2026-03-03
**Phases:** 14 | **Plans:** 31 | **Sessions:** ~15

### What Was Built
- Complete WPF desktop notepad with per-virtual-desktop windows, tabs, and state
- SQLite data layer with WAL mode, single-instance process with named pipe IPC
- Virtual desktop COM interop with three-tier session matching (GUID, name, index)
- Two-tier undo/redo with 50MB global memory budget and automatic collapse
- Light/Dark/System theming with instant ResourceDictionary swap (12 color tokens)
- Window drag detection with lock overlay, reparent/merge/cancel resolution flows
- File drop, preferences dialog, global hotkey, 20+ keyboard shortcuts
- Orphaned session recovery, bulk delete operations, crash recovery

### What Worked
- **Bottom-up phase ordering** — building data layer first, then COM interop, then UI layers meant each phase had solid foundations
- **Phase-level verification** — VERIFICATION.md files with specific code evidence caught gaps early (Phases 6, 7 missing verification led to gap-closure phases)
- **Audit before milestone close** — milestone audit caught 3 integration bugs (OnClosing deletion, FlushAsync before drag, FlushAndClose await) that would have been shipped
- **Decimal phase numbering** — allowed inserting urgent gap-closure work (8.1, 8.2, 10.1, 10.2) without disrupting numbering
- **Code-behind pattern** — avoiding MVVM kept complexity low for a single-developer WPF app; no binding boilerplate
- **Raw COM interop** — building own interop layer instead of depending on NuGet gave full control over Windows 11 build compatibility

### What Was Inefficient
- **SUMMARY frontmatter** — `requirements_completed` field never adopted across 31 summaries; requirements tracking happened through VERIFICATION.md and REQUIREMENTS.md instead
- **Phase 9 duration** — file drop + preferences + hotkeys + keyboard shortcuts combined into one phase was too large (~55 min vs ~10 min average); should have been 2-3 phases
- **Verification timing** — missing VERIFICATION.md for Phases 6, 7, 9, 10 required 2 gap-closure phases; should verify as part of phase execution, not after
- **Phase 8.2 and 10.2 plan statuses** — ROADMAP showed `[ ] TBD` plans even after execution completed (cosmetic)

### Patterns Established
- **Custom Popup for themed menus** — WPF ContextMenu can't use DynamicResource; custom Popup pattern reusable
- **SemaphoreSlim(1,1) for async write serialization** — standard pattern for all DB access
- **Dispatcher.BeginInvoke for post-layout work** — scroll restore, COM state settling, initial focus
- **_suppress flag for programmatic text changes** — prevents TextChanged handler recursion during undo/tab switch
- **ShowInfoToast reuse** — toast infrastructure from Phase 5 reused for file drop errors and crash recovery messages
- **Phase-level VERIFICATION.md** — code-evidence verification with file paths and line numbers

### Key Lessons
1. **Combine verification into phase execution** — don't create separate verification phases; verify during or immediately after each phase
2. **Audit before shipping** — the milestone audit caught 3 real bugs in cross-phase integration that individual phase testing missed
3. **Keep phases under 3 plans** — Phase 9 with 3 diverse subsystems (file drop, preferences, keyboard) was too broad; each should have been its own phase
4. **COM interop needs build-specific testing** — Windows 11 23H2 vs 24H2 GUID differences caught during Phase 2; GUID dispatch dictionary pattern essential
5. **WPF code-behind is viable** — for single-developer apps, code-behind is faster than MVVM with no downside; skip the architecture debate

### Cost Observations
- Model mix: ~40% opus, ~50% sonnet, ~10% haiku
- Sessions: ~15 total
- Notable: Entire v1.0 built in 2 days; sonnet agents handled most execution; opus for planning and complex integration

## Milestone: v1.1 — Polish & Stability

**Shipped:** 2026-03-10
**Phases:** 7 | **Plans:** 26 | **Sessions:** ~20

### What Was Built
- Crash/freeze elimination: stack overflow on pin/unpin/delete, rename freeze (event cascade guards)
- Tab panel UX overhaul: background highlight, Fluent pin icon, adaptive title width, resizable panel (GridSplitter)
- Dark mode tab legibility, font size percentage display, hamburger menu dismiss fix
- Full UI/UX polish pass: note persistence fix, tab button layout redesign, in-place drag fade, full-window file drop
- Recovery sidebar with flat rows, tab name + excerpt previews, desktop name context
- Tab cleanup panel with age-based filtering, pinned toggle, preview list, and confirmation dialog
- 57MB self-contained Windows installer via Inno Setup with CalVer versioning (2026.3.0)

### What Worked
- **User-driven requirements** — all v1.1 requirements came from manual review of v1.0, so every change was a real user need
- **Side panel pattern reuse** — preferences, recovery, and cleanup panels share Width=320, slide animation, one-panel-at-a-time; third panel (cleanup) built in 2 plans
- **MVVM migration (between milestones)** — forwarding properties pattern enabled incremental migration without breaking any partial classes; 302 tests added
- **Gap closure workflow** — Phase 15 needed 6 gap closure rounds (15-06 through 15-11), but each was scoped and fast; 11 plans total in 5 days
- **Decimal phase insertion** — Phase 15.1 inserted cleanly between 15 and 16 for urgent fixes discovered during UAT

### What Was Inefficient
- **Phase 15 estimation** — originally scoped at 5 plans, actually required 11 (6 gap closure rounds); 18 requirements was too many for one phase
- **Drag ghost → drag fade iteration** — Phase 15 implemented ghost adorner (DragAdorner + RenderTargetBitmap), then Phase 15.1 replaced it entirely with in-place opacity fade; wasted effort on the ghost approach
- **Registry fallback discovery** — Windows 25H2 desktop name issue (COM GetName() returning empty) only found in gap closure round 6 (15-11); earlier testing on 25H2 would have caught it sooner
- **No v1.1 milestone audit** — skipped audit before archival; v1.0's audit caught 3 bugs, so skipping was a calculated risk

### Patterns Established
- **One-panel-at-a-time** — `CloseAllSidePanels()` before opening any panel; shared pattern for future panels
- **In-place drag fade** — 50% opacity on drag start, 150ms animated recovery on drop; simpler than ghost adorner
- **Event cascade guard** — `_isRebuildingTabList` class-level field prevents SelectionChanged stack overflow during collection rebuild
- **Registry fallback for COM** — HKCU VirtualDesktops\Desktops\{GUID}\Name when COM GetName() returns empty
- **MinHeight on hover containers** — prevents vertical jitter when icon visibility toggles

### Key Lessons
1. **Limit phases to ~5 requirements max** — Phase 15 with 18 requirements needed 6 gap closure rounds; breaking into 3-4 phases would have reduced rework
2. **Test on all target OS versions early** — Windows 25H2 desktop name issue could have been found in Phase 11, not Phase 15 gap closure round 6
3. **Prefer simpler UI approaches first** — drag fade (opacity change) was simpler and better than drag ghost (adorner + bitmap capture); try simple first
4. **Side panel pattern scales well** — once established, adding a new panel (cleanup) is a 2-plan task with predictable effort
5. **MVVM migration pays off for testing** — 302 tests enabled confident refactoring; forwarding properties make migration incremental and safe

### Cost Observations
- Model mix: ~30% opus, ~55% sonnet, ~15% haiku
- Sessions: ~20 total
- Notable: Gap closure rounds were fast (~10min each) but added up; 6 rounds on Phase 15 suggests initial planning underestimated WPF visual edge cases

---

## Cross-Milestone Trends

### Process Evolution

| Milestone | Sessions | Phases | Key Change |
|-----------|----------|--------|------------|
| v1.0 | ~15 | 14 | Baseline — established phase/plan/verify/audit cycle |
| v1.1 | ~20 | 7 | Gap closure workflow matured; side panel pattern established; MVVM + 302 tests added |

### Cumulative Quality

| Milestone | Requirements | Coverage | Tech Debt Items | Tests |
|-----------|-------------|----------|-----------------|-------|
| v1.0 | 120 | 100% | 4 (minor) | 0 |
| v1.1 | 13 | 100% | 3 (minor) | 302 |

### Top Lessons (Verified Across Milestones)

1. Audit-before-ship catches integration bugs that per-phase testing misses
2. Bottom-up dependency ordering prevents rework
3. Keep phases under 5 requirements — both v1.0 Phase 9 (3 subsystems) and v1.1 Phase 15 (18 requirements) were too broad
4. Reusable UI patterns (toast, side panel) pay off exponentially in later phases
5. Test on all target OS versions early — Windows build differences cause late-discovery bugs
