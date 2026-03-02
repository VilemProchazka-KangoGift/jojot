# Feature Research

**Domain:** Plain-text desktop notepad / quick-capture scratchpad (Windows)
**Researched:** 2026-03-02
**Confidence:** MEDIUM-HIGH — core categories verified across multiple sources; virtual-desktop-per-window is a unique differentiator with no direct analogues to compare against

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features users assume exist. Missing these = product feels broken or incomplete.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Plain text editing | Core product promise | LOW | Monospace font, word-wrap — users hate formatting lock-in |
| Autosave | Windows Notepad added this in 2024; users now assume it | LOW | Debounced write; explicit "Save" button is obsolete UX |
| Session restore | Notepad, Notepad++, Notepads all restore sessions | MEDIUM | Content must survive close/reopen |
| Tabbed interface | Any modern multi-document text app has tabs | MEDIUM | Ctrl+T to create, Ctrl+W to close — these shortcuts are muscle memory |
| Undo/redo (Ctrl+Z / Ctrl+Y) | Universal text editing expectation | MEDIUM | WPF native TextBox undo fails on tab-switch — custom stack required |
| Dark mode / light mode | All major notepads support system theme by 2025 | LOW | System theme follow is non-negotiable; manual toggle is table stakes |
| Font size control | Any text app lets users adjust font | LOW | Ctrl+= / Ctrl+- / Ctrl+Scroll are standard |
| Search/filter within app | At minimum, tab filtering | LOW | Ctrl+F to focus search is universal |
| New tab with instant focus | One-click / one-shortcut to start writing | LOW | Delay to first keystroke kills quick-capture value prop |
| Delete tab | Close tabs individually | LOW | Middle-click is standard browser/editor convention |
| Copy/paste | Fundamental text operation | LOW | Standard behavior expected |
| File open via drag-and-drop | Notepad++ supports this; users expect it | MEDIUM | Content inspection over extension is the right approach |
| Keyboard shortcuts for all common actions | Power users demand this | LOW | Full shortcut table is non-negotiable |
| Tooltips on toolbar icons | Icons alone are ambiguous | LOW | 600 ms hover delay is standard |

### Differentiators (Competitive Advantage)

Features that set JoJot apart. Not universally expected in notepads, but directly serve the core value proposition.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| One window per virtual desktop | Zero competitors do this — notes stay in context when switching desktops | HIGH | Requires IVirtualDesktopManager (COM, undocumented) + IVirtualDesktopNotification |
| Three-tier session matching (GUID → name → index) | Notes survive reboots even though Windows reassigns virtual desktop GUIDs | HIGH | Session continuity is the entire product's promise — this makes it robust |
| Window title shows active desktop name | Instant orientation — "I know what context I'm in" | LOW | Live-updated via IVirtualDesktopNotification |
| Drag window between desktops: merge/reparent UI | Lets users reorganize their desktop layout without losing notes | HIGH | Lock overlay + merge vs. reparent decision — unique to virtual desktop apps |
| Orphaned session recovery panel | Notes from deleted desktops aren't lost | MEDIUM | Adopt / open / delete options |
| Taskbar interaction: left-click = focus/create, middle-click = quick capture | Taskbar becomes a zero-friction capture trigger | MEDIUM | RegisterHotKey (Win+Shift+N) + named pipe IPC to existing process |
| Global hotkey (Win+Shift+N) | Summons the note window without touching the mouse | MEDIUM | Heynote has this too — it drives adoption significantly |
| Tab pinning with sort-to-top | Protect important notes from bulk operations | LOW | Pinned zone / unpinned zone drag-to-reorder is intuitive |
| Tab labels: 3-tier fallback (name → content preview → "New note") | Tabs are always self-labeling — no "Untitled 1" clutter | LOW | Content preview updates in real-time as user types |
| Two-tier undo stack (50 fine-grained + 20 coarse checkpoints) | Recover a note's state from an hour ago — goes beyond typical Ctrl+Z | HIGH | 50 MB global budget with LRU collapse is sophisticated memory management |
| 4-second undo toast instead of confirmation dialog | Faster deletion workflow — no modal dialog friction | LOW | Toast replaces the "Are you sure?" pattern — used by Superhuman, linear.app |
| Tab clone | Duplicate a note's content into a new tab | LOW | Rarely in notepad apps; useful for templates |
| Per-desktop tab search (label + content) | Find a note by typing anywhere in it, scoped to current desktop | LOW | Ctrl+F focuses search; Escape returns to editor |
| Content-aware file drop (extension-agnostic) | Accepts .log, .yaml, .json, etc. — not just .txt | MEDIUM | 8 KB content inspection; 500 KB size cap |
| Native AOT binary (< 200ms startup) | Instant launch — no "warm up" delay | HIGH | WPF + AOT matured in .NET 9/10; requires AOT-safe library choices |
| Single background process + named pipe IPC | One process manages all virtual desktop windows; low resource use | HIGH | Named mutex for single-instance; named pipe for inter-window commands |

### Anti-Features (Commonly Requested, Often Problematic)

Features that seem good but would compromise JoJot's core promise or create scope creep.

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Rich text / Markdown rendering | Users see other apps do it; feels "modern" | Destroys the "fastest possible capture" promise; formatting decisions add friction; Microsoft Notepad adding this in 2025 generated significant backlash from plain-text users | Keep plain text; content-preview tab labels provide enough structure |
| Cloud sync | "Access my notes anywhere" | Adds network dependency, latency, auth complexity, offline handling — none of this is JoJot's value; local-first IS the value proposition | Files are on-disk SQLite; users can put the DB in a synced folder manually |
| Full-text search across all desktops | "Find a note I wrote somewhere" | Violates the "notes per context" design — cross-desktop search breaks the mental model of per-desktop isolation; also high implementation cost | Search is per-desktop only; tab labels make scoped search sufficient |
| Images or attachments | "Paste a screenshot" | Binary data in a plain-text context; massive scope expansion; file size and format handling | Plain text only; link to files instead |
| Encryption | "Protect sensitive notes" | Not a password manager; encryption adds key management UX that is a product unto itself | Users with sensitive data should use a dedicated secure notes app |
| Confirmation dialogs for delete | "Don't let me accidentally delete" | Modal dialogs interrupt flow; the 4-second undo toast covers the recovery case without adding friction | Toast undo with 4-second window |
| Tab persistence of undo history | "Remember my undo stack after restart" | Undo stacks are in-memory snapshots; serializing them adds startup cost and complexity disproportionate to value | Undo persists for the session lifetime; full content is always saved |
| Markdown-formatted export | "Export with headers/bold" | JoJot is a plain text tool; exporting formatting requires a render pipeline | Export as .txt with raw content; users can open in any Markdown editor |
| Mobile / cross-platform | "Use on phone too" | Virtual desktop integration is Windows-only; re-architecting for mobile destroys the platform-specific differentiator | Windows desktop only is a strength, not a weakness |
| Multiple themes / custom colors | "I want my own color scheme" | Light/Dark/System is what users actually use; custom themes add UI surface area with minimal adoption | Three themes cover 99% of use cases |
| Collaboration / sharing | "Share notes with a teammate" | Requires a backend, auth, conflict resolution — completely out of scope | Copy full note to clipboard; share the .txt file |

---

## Feature Dependencies

```
[Single-instance process + named pipe IPC]
    └──required-by──> [Taskbar click handling (left/middle)]
    └──required-by──> [Global hotkey (Win+Shift+N)]
    └──required-by──> [Window-per-desktop management]

[IVirtualDesktopManager detection]
    └──required-by──> [Window-per-desktop creation]
                          └──required-by──> [Window title shows desktop name]
                          └──required-by──> [Drag window between desktops UI]
                          └──required-by──> [Orphaned session recovery]

[SQLite data model (notes, app_state, pending_moves, preferences)]
    └──required-by──> [Autosave with debounce]
                          └──required-by──> [Session restore on reopen]
                          └──required-by──> [Two-tier undo (snapshot timing)]
    └──required-by──> [Three-tier session matching]
    └──required-by──> [pending_moves crash recovery]

[Tab list panel]
    └──required-by──> [Tab rename (inline)]
    └──required-by──> [Tab search/filter]
    └──required-by──> [Tab pinning with sort-to-top]
    └──required-by──> [Drag-to-reorder within zones]
    └──required-by──> [Tab hover delete icon]

[Plain-text editor (TextBox)]
    └──required-by──> [Autosave]
    └──required-by──> [Custom undo/redo stack per tab]
                          └──required-by──> [Memory pressure collapse (LRU)]
    └──required-by──> [File drop → new tab]
    └──required-by──> [Copy selection / full note]
    └──required-by──> [Font size controls]

[Deletion toast]
    └──enhances──> [Tab delete (hover icon, middle-click, Ctrl+W, toolbar)]
    └──enhances──> [Bulk delete operations]

[Theming (ResourceDictionary swap)]
    └──enhances──> [All UI components]
    └──required-by──> [Preferences dialog (theme toggle)]

[Preferences dialog]
    └──required-by──> [Theme preference persistence]
    └──required-by──> [Font size preference persistence]
    └──required-by──> [Global hotkey configuration]
    └──required-by──> [Autosave debounce configuration]
```

### Dependency Notes

- **IPC before any multi-window feature:** Named pipe IPC and the single-instance mutex must exist before taskbar click handling, global hotkeys, or window-per-desktop creation can work. This is the foundation layer.
- **SQLite schema before all persistence:** Every feature that saves state (autosave, session restore, preferences, session matching) requires the data model to be established first.
- **Virtual desktop detection before window management:** IVirtualDesktopManager is required to know which desktop the app launched on, which drives all per-window behavior.
- **Custom undo stack independent of editor:** WPF TextBox native undo clears on tab switch — the custom stack must be in place before any multi-tab editing is usable.
- **Theming must be established early:** ResourceDictionary tokens need to be defined before any other UI components are built, otherwise retroactive theming is painful.

---

## MVP Definition

### Launch With (v1)

Minimum viable to prove the core concept: "notes tied to virtual desktop context."

- [ ] SQLite data model + WAL mode — persistent storage is non-negotiable
- [ ] Single-instance process + named pipe IPC — required for all window management
- [ ] Virtual desktop detection (IVirtualDesktopManager) — the product's core differentiator
- [ ] One window per desktop — the central feature
- [ ] Tab list with create/delete/switch — basic multi-note capability
- [ ] Plain-text editor with autosave (500ms debounce) — core editing flow
- [ ] Session restore on reopen — notes must survive close/reopen
- [ ] Three-tier session matching — desktop GUIDs change on reboot; this makes the product reliable
- [ ] Dark/Light/System theming — table stakes in 2025/2026
- [ ] Full keyboard shortcut set — power users won't adopt without this
- [ ] Taskbar integration (left-click focus/create, middle-click quick capture) — zero-friction access
- [ ] Custom undo/redo stack per tab — WPF native fails on tab switch; must replace it
- [ ] Deletion toast with 4-second undo — the no-confirmation-dialog UX requires this safety net
- [ ] Tab rename (inline, F2, context menu) — tab labels from content preview are useful but rename is essential
- [ ] Native AOT + < 200ms startup — the performance promise must be validated before shipping

### Add After Validation (v1.x)

Features that improve the experience once core is working and validated.

- [ ] Global hotkey (Win+Shift+N) — drives daily adoption; add once window management is stable
- [ ] Drag window between desktops UI (lock overlay + merge/reparent) — complex virtual desktop interaction; useful but not launch-blocking
- [ ] Orphaned session recovery panel — edge case; add once session matching is proven
- [ ] Tab pinning + drag-to-reorder — useful for power users; low implementation cost
- [ ] Tab clone — low cost quality-of-life feature
- [ ] File drop with content inspection — common enough to add early post-launch
- [ ] Preferences dialog — exposes font size, theme, hotkey, debounce tuning
- [ ] Save as TXT export — needed for interoperability
- [ ] pending_moves crash recovery — makes window dragging robust across crashes

### Future Consideration (v2+)

Features to defer until product-market fit is established.

- [ ] Background schema migrations — needed as schema evolves; defer until first schema change is required
- [ ] Memory pressure collapse for undo stacks — 50 MB budget enforcement; defer until real-world usage shows it's needed (50 MB is generous for text content)
- [ ] Coarse checkpoint tier (5-minute intervals) — tier-2 undo is sophisticated; launch with tier-1 only, add tier-2 once editing flow is stable

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| SQLite data model | HIGH | LOW | P1 |
| Single-instance + IPC | HIGH | MEDIUM | P1 |
| Virtual desktop detection | HIGH | HIGH | P1 |
| Window per desktop | HIGH | HIGH | P1 |
| Tab list + create/delete | HIGH | MEDIUM | P1 |
| Plain-text editor + autosave | HIGH | LOW | P1 |
| Session restore | HIGH | LOW | P1 |
| Three-tier session matching | HIGH | HIGH | P1 |
| Custom undo/redo stack | HIGH | HIGH | P1 |
| Dark/Light/System theming | HIGH | LOW | P1 |
| Keyboard shortcuts | HIGH | LOW | P1 |
| Taskbar integration | HIGH | MEDIUM | P1 |
| Deletion toast | HIGH | LOW | P1 |
| Tab rename | MEDIUM | LOW | P1 |
| Native AOT / startup perf | HIGH | HIGH | P1 |
| Global hotkey | HIGH | LOW | P2 |
| Tab pinning | MEDIUM | LOW | P2 |
| File drop | MEDIUM | MEDIUM | P2 |
| Preferences dialog | MEDIUM | LOW | P2 |
| Save as TXT | MEDIUM | LOW | P2 |
| Tab clone | LOW | LOW | P2 |
| Drag-between-desktops UI | MEDIUM | HIGH | P2 |
| Orphaned session recovery | MEDIUM | MEDIUM | P2 |
| pending_moves crash recovery | MEDIUM | MEDIUM | P2 |
| Memory pressure undo collapse | LOW | HIGH | P3 |
| Coarse undo checkpoints (tier 2) | LOW | MEDIUM | P3 |
| Background migrations | MEDIUM | MEDIUM | P3 |

**Priority key:**
- P1: Must have for launch
- P2: Should have, add when possible
- P3: Nice to have, future consideration

---

## Competitor Feature Analysis

| Feature | Windows Notepad (2025) | Notepad++ | Notepads App | Heynote | JoJot |
|---------|------------------------|-----------|--------------|---------|-------|
| Plain text editing | Yes | Yes | Yes | Yes (+ code blocks) | Yes |
| Tabs | Yes (multi-window, not tabs) | Yes | Yes | No (single buffer) | Yes |
| Autosave | Yes | Cache-based | Yes (session snapshot) | Yes | Yes |
| Dark mode / system theme | Yes | Yes (plugin) | Yes | Yes | Yes |
| Per-virtual-desktop notes | No | No | No | No | **Yes (unique)** |
| Single background process | No | No | No | No | **Yes** |
| Global hotkey | No | No | No | Yes | Yes |
| Session restore across reboot | Yes | Yes | Yes (snapshot) | Yes | Yes (3-tier matching) |
| Keyboard shortcut set | Partial | Full | Full | Partial | Full |
| File drag-and-drop | Yes (.txt only) | Yes | Yes | No | Yes (content-inspected) |
| Tab search (label + content) | No | Partial | No | No | **Yes** |
| Tab rename | No | Yes (right-click) | Yes | N/A | Yes (double-click, F2) |
| Tab pinning | No | No | No | No | **Yes** |
| Custom undo stack per tab | No | Yes (Scintilla) | No | No | **Yes** |
| No confirmation delete | No (dialog) | No (dialog) | No | N/A | **Yes (toast undo)** |
| Startup time | Slow (JIT) | Fast | Fast | Medium (Electron) | **< 200ms (AOT)** |
| Rich text / Markdown | Yes (added 2025) | Syntax highlight | Markdown preview | Syntax highlight | **No (by design)** |
| Cloud sync | No | No | No | No | No (local-first by design) |

---

## Sources

- [Defending Notepad: Microsoft Notepad 2025 Features](https://twit.tv/posts/tech/defending-notepad-why-microsofts-latest-updates-actually-make-sense) — MEDIUM confidence (editorial)
- [Text Formatting in Notepad — Windows Insider Blog, May 2025](https://blogs.windows.com/windows-insider/2025/05/30/text-formatting-in-notepad-begin-rolling-out-to-windows-insiders/) — HIGH confidence (official Microsoft)
- [Windows Notepad Wikipedia](https://en.wikipedia.org/wiki/Windows_Notepad) — MEDIUM confidence (community-maintained)
- [Notepad++ Wikipedia](https://en.wikipedia.org/wiki/Notepad++) — MEDIUM confidence
- [Notepads App GitHub](https://github.com/0x7c13/Notepads) — HIGH confidence (official repo)
- [Heynote — A dedicated scratchpad for developers](https://heyman.info/2024/heynote-scratchpad-for-developers) — HIGH confidence (author's own description)
- [AlternativeTo: Best Windows Notepad Alternatives 2025](https://alternativeto.net/software/notepad/) — LOW confidence (crowdsourced)
- [Notepad++ vs Notepad2 Comparison 2025 — appmus.com](https://appmus.com/vs/notepad-plus-plus-vs-notepad2) — LOW confidence (third-party aggregator)
- [Microsoft Windows Virtual Desktops Support](https://support.microsoft.com/en-us/windows/configure-multiple-desktops-in-windows-36f52e38-5b4a-557b-2ff9-e1a60c976434) — HIGH confidence (official)
- [6 Free Lightweight Notepad Alternatives for Windows — tech2geek.net](https://www.tech2geek.net/6-free-and-lightweight-notepad-alternatives-for-windows-no-ai-required/) — LOW confidence (blog)
- [Quora: Notepad with tabs and autosave](https://www.quora.com/Is-there-a-comfortable-desktop-notepad-with-tabs-and-autosave-options-besides-NeechPad) — LOW confidence (user Q&A)

---
*Feature research for: Plain-text desktop notepad with virtual desktop integration (Windows)*
*Researched: 2026-03-02*
