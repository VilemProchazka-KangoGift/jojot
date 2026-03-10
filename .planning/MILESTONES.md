# Milestones
## v1.1 Polish & Stability

**Shipped:** 2026-03-10
**Phases:** 7 (11-16 + 1 decimal phase)
**Plans:** 26
**Requirements:** 13/13 satisfied

**Delivered:** Stabilized the v1.0 app by eliminating crashes and freezes, overhauled the tab panel UX, polished dark mode and menus, redesigned session recovery and tab cleanup as side panels, and shipped a self-contained Windows installer.

**Key Accomplishments:**
1. Eliminated stack overflow crashes on pin/unpin/delete and tab rename freeze via event cascade guards
2. Tab panel overhaul: background highlight, Fluent pin icon, adaptive title width, resizable panel with persistence
3. Dark mode tab legibility fix, font size percentage display, hamburger menu dismiss fix
4. Full UI/UX polish pass: note persistence fix, tab button layout redesign, in-place drag fade, full-window file drop
5. Recovery sidebar with flat rows, tab name + excerpt previews, and desktop name context
6. Tab cleanup panel with age-based filtering, pinned toggle, preview list, and confirmation dialog
7. 57MB self-contained Windows installer via Inno Setup with CalVer versioning (2026.3.0)

**Stats:**
- Lines of code: 22,223 (C# + XAML), up from 13,995
- Commits: 190
- Timeline: 7 days (2026-03-03 → 2026-03-10)
- Git range: `v1.0` → `631b3ca`

**Tech Debt:**
- SUMMARY frontmatter `requirements_completed` field empty across plan summaries
- Desktop name registry fallback adds Windows version-specific code path (25H2)
- Phase 15 required 11 plans (6 gap closure rounds) — initial estimation was 5 plans

**Archive:** `.planning/milestones/v1.1-ROADMAP.md`, `.planning/milestones/v1.1-REQUIREMENTS.md`

---


## v1.0 MVP

**Shipped:** 2026-03-03
**Phases:** 14 (1-10 + 4 decimal gap-closure phases)
**Plans:** 31
**Requirements:** 120/120 satisfied

**Delivered:** A complete WPF desktop notepad that gives each Windows virtual desktop its own independent window with tabs, autosave, undo/redo, theming, file drop, preferences, and window drag handling — all managed by a single background process.

**Key Accomplishments:**
1. SQLite data layer with WAL mode, single-instance process with named pipe IPC
2. Virtual desktop detection via COM interop with three-tier session matching across reboots
3. Per-desktop window management with geometry persistence and taskbar click handling
4. Full tab management: create, rename, search, drag-to-reorder, pin, clone with 3-tier label fallback
5. Plain-text editor with two-tier undo/redo (50 fine-grained + 20 coarse, 50MB global budget) and 500ms autosave
6. Light/Dark/System theming with instant ResourceDictionary swap, 12 color tokens, 8-button toolbar
7. Window drag detection with lock overlay (reparent/merge/cancel), crash recovery via pending_moves
8. File drop with content inspection, preferences dialog, global hotkey (Win+Shift+N), 20+ keyboard shortcuts

**Stats:**
- Lines of code: 13,995 (C# + XAML)
- Files: 31
- Timeline: 2 days (2026-03-02 → 2026-03-03)
- Git range: `11533f6` → `eaec5f7`

**Tech Debt:**
- SUMMARY frontmatter `requirements_completed` field empty across all 31 plan summaries
- `GetPendingMovesAsync` bypasses `_writeLock` (startup-only, low risk)
- `ShowDesktopCommand` IPC type defined but never sent (dead code)
- Phase 8 VERIFICATION needs 9 manual visual/interactive tests

**Archive:** `.planning/milestones/v1.0-ROADMAP.md`, `.planning/milestones/v1.0-REQUIREMENTS.md`

---
