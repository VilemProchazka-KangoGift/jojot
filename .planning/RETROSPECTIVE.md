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

---

## Cross-Milestone Trends

### Process Evolution

| Milestone | Sessions | Phases | Key Change |
|-----------|----------|--------|------------|
| v1.0 | ~15 | 14 | Baseline — established phase/plan/verify/audit cycle |

### Cumulative Quality

| Milestone | Requirements | Coverage | Tech Debt Items |
|-----------|-------------|----------|-----------------|
| v1.0 | 120 | 100% | 4 (minor) |

### Top Lessons (Verified Across Milestones)

1. Audit-before-ship catches integration bugs that per-phase testing misses
2. Bottom-up dependency ordering prevents rework
