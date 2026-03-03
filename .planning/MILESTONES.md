# Milestones

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
