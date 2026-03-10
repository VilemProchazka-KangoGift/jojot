# JoJot

## What This Is

Lightweight Windows desktop app for plain-text notes tied to virtual desktops. Each Windows virtual desktop gets its own independent JoJot window with its own tabs, autosave, undo/redo, and saved state. One background process manages all windows via a single SQLite database. Built for fastest possible capture — no formatting, no cloud sync, no clutter.

## Core Value

Instant note capture tied to your virtual desktop context — switch desktops, switch notes, zero friction.

## Requirements

### Validated

- ✓ SQLite data model with WAL mode, single connection, 4 tables — v1.0
- ✓ Virtual desktop detection via IVirtualDesktopManager COM, one window per desktop — v1.0
- ✓ Three-tier session matching (GUID, name, index) across reboots — v1.0
- ✓ Single-instance background process with named mutex and named pipe IPC — v1.0
- ✓ Taskbar click handling: left-click (focus/create), middle-click (quick capture) — v1.0
- ✓ Window title showing desktop name, live-updated — v1.0
- ✓ Tab list panel (180px fixed, scrollable, drag-to-reorder within zones) — v1.0
- ✓ Tab labels with 3-tier fallback (custom name, content preview, "New note") — v1.0
- ✓ Tab rename (double-click, F2, context menu), inline editing — v1.0
- ✓ Tab search box filtering by label and content (Ctrl+F) — v1.0
- ✓ Plain-text editor (monospace, word-wrap, no formatting) — v1.0
- ✓ Autosave with 500ms debounce to SQLite, write frequency cap — v1.0
- ✓ Two-tier undo/redo stack (50 fine-grained + 20 coarse, 50MB global budget) — v1.0
- ✓ Toolbar: undo, redo, pin, clone, copy, paste, save-as-TXT, delete — v1.0
- ✓ Copy behaviour: selection or full note if nothing selected — v1.0
- ✓ Save as TXT with OS dialog, UTF-8 BOM — v1.0
- ✓ Pin/unpin tabs, pinned always sorted to top, protected from bulk delete — v1.0
- ✓ Clone tab — v1.0
- ✓ Tab deletion (multiple triggers), immediate with 4-second undo toast — v1.0
- ✓ Deletion toast with undo, auto-dismiss, bulk support — v1.0
- ✓ File drop: content-inspected acceptance, 500KB limit, multiple files, error messages — v1.0
- ✓ Window menu: recover sessions, bulk delete operations, preferences, exit — v1.0
- ✓ Tab context menu: rename, pin, clone, save, delete, delete all below — v1.0
- ✓ Orphaned session recovery panel with adopt/open/delete actions — v1.0
- ✓ Window drag detection via IVirtualDesktopNotification, lock overlay — v1.0
- ✓ Drag overlay: reparent (no existing session) or merge (existing session) or cancel — v1.0
- ✓ pending_moves crash recovery on startup — v1.0
- ✓ Theming: light, dark, system (follows Windows), instant ResourceDictionary swap — v1.0
- ✓ Theme tokens for all UI elements (12 color tokens) — v1.0
- ✓ Preferences dialog: theme toggle, font size, autosave debounce, global hotkey — v1.0
- ✓ Global hotkey (Win+Shift+N default) via RegisterHotKey — v1.0
- ✓ Font size controls: Ctrl+=, Ctrl+-, Ctrl+0, Ctrl+Scroll — v1.0
- ✓ All keyboard shortcuts (tab management, editor, font size) — v1.0
- ✓ Startup sequence with ReadyToRun publishing — v1.0
- ✓ Background migrations after window shown, never on cold-start path — v1.0
- ✓ Post-delete focus rules (next below, then last, then new empty tab) — v1.0
- ✓ Window close: flush content, delete empty tabs, save geometry, keep process alive — v1.0
- ✓ Exit: flush all windows, delete all empty tabs, terminate process — v1.0
- ✓ Windows installer via Inno Setup with self-contained .NET 10 runtime — v1.1
- ✓ CalVer versioning (2026.3.0) with publisher metadata in EXE — v1.1
- ✓ Tab cleanup side panel with age-based filtering and confirmation — v1.1
- ✓ Recovery sidebar with full-width rows, tab excerpts, and adopt/delete — v1.1
- ✓ In-place drag fade (replaces ghost adorner) — v1.1
- ✓ Pin/close button hit targets and layout on all tabs — v1.1
- ✓ Full-window file drop coverage — v1.1

### Active

(No active requirements — v1.1 complete)

## Completed Milestone: v1.1 Polish & Stability

**Goal:** Fix critical bugs (crashes/freezes), improve UI polish, and add installer support based on first round of manual review.

**Target features:**
- Fix stack overflow on pin/unpin and delete
- Fix tab rename freeze
- Fix dark mode tab legibility, tab highlight style, tab panel sizing
- Add pin icon to tabs, resize percentages, menu dismiss behavior
- Windows installer support

### Out of Scope

- Rich text / markdown rendering — plain text only by design; core positioning
- Cloud sync — local-first, no network dependency
- Full-text search across all desktops — search is per-desktop only
- Images or attachments — text only
- Encryption — not a security tool
- Mobile or cross-platform — Windows desktop only (WPF)
- Tab persistence of undo history — stacks are in-memory only
- Native AOT (PublishAot=true) — WPF incompatible (dotnet/wpf#3811); using ReadyToRun instead
- Cold-start < 200ms guarantee — unrealistic for WPF (Defender scanning); warm-start best-effort

## Context

Shipped v1.0 with 13,995 LOC across 31 C# and XAML files.
Tech stack: WPF, .NET 10, C#, SQLite (Microsoft.Data.Sqlite), PublishReadyToRun.
14 phases executed in 2 days (2026-03-02 → 2026-03-03), 31 plans total.
All 120 requirements verified against codebase with code evidence.

Spec documents in `resources/` remain the definitive reference for v1.0 behavior.

Key technical state:
- Raw COM interop with [ComImport] for virtual desktop API (no NuGet; build-specific GUID dispatch)
- Single SQLite connection per process, WAL mode, SemaphoreSlim write serialization
- Custom two-tier undo/redo (WPF native TextBox undo unsuitable)
- Custom Popup-based menus (WPF ContextMenu incompatible with DynamicResource theming)

## Constraints

- **Tech stack**: WPF, .NET 10, C#, PublishReadyToRun, SQLite (Microsoft.Data.Sqlite) — non-negotiable
- **Platform**: Windows 10/11 only
- **Data**: Single SQLite connection per process, WAL mode, writes serialized
- **UX**: No confirmation dialogs for single-tab deletion (toast undo only)

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| ReadyToRun over Native AOT | WPF incompatible with PublishAot; ReadyToRun provides fast startup | ✓ Good — app launches quickly, no AOT issues |
| Custom undo/redo over WPF native | WPF TextBox undo clears on tab switch, can't transfer between tabs | ✓ Good — seamless per-tab undo with 50MB memory management |
| Single process + IPC over multi-process | One SQLite connection, consistent state, lower resource use | ✓ Good — simple architecture, no sync issues |
| Content inspection over extension for file drop | Accept any text file regardless of extension (.log, .yaml, etc.) | ✓ Good — binary detection works reliably |
| Three-tier session matching | GUIDs reassigned on reboot; name + index fallback ensures continuity | ✓ Good — handles reboot, desktop rename, desktop add/remove |
| No confirmation dialogs for delete | Speed over safety; 4-second undo toast provides recovery path | ✓ Good — fast workflow, toast undo prevents accidents |
| Raw COM interop with [ComImport] | No NuGet packages support .NET 10 virtual desktop API | ✓ Good — works on 23H2 and 24H2 with GUID dispatch |
| Custom Popup for menus | WPF ContextMenu can't use DynamicResource for themed backgrounds | ✓ Good — instant theme switching works across all menus |
| SemaphoreSlim(1,1) for DB writes | Async-compatible unlike lock; all writes serialized through single path | ✓ Good — no data races, clean async code |
| Destroy windows on close (not hide) | WPF cannot reopen after Close(); fresh instances via IPC | ✓ Good — clean lifecycle, no stale window state |
| Inno Setup for installer | Standard Windows installer tooling, free for open-source | ✓ Good — 57MB self-contained installer, minimal wizard |
| CalVer versioning (Year.Month.Build) | Communicates release timeline, no semver overhead | ✓ Good — 2026.3.0 in EXE properties |
| ASCII publisher name in PE metadata | Diacritics cause encoding issues in PE metadata | ✓ Good — "Vilem Prochazka" displays correctly everywhere |

---
*Last updated: 2026-03-10 after v1.1 milestone*
