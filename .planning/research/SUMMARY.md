# Project Research Summary

**Project:** JoJot
**Domain:** WPF desktop notepad with Windows virtual desktop integration
**Researched:** 2026-03-02
**Confidence:** MEDIUM — stack and architecture are HIGH confidence (first-party sources); virtual desktop COM interop is LOW confidence due to undocumented, version-sensitive APIs; AOT is a confirmed CRITICAL BLOCKER

## Executive Summary

JoJot is a plain-text Windows desktop notepad whose defining feature is one window per virtual desktop — a capability no competitor offers. The product lives at the intersection of three technically demanding domains: WPF desktop application development, undocumented Windows COM APIs for virtual desktop management, and a custom multi-tab editing engine. The recommended approach is a single-process architecture where one background process owns all virtual desktop windows, communicating via named pipe IPC. All persistent state lives in a single SQLite database via WAL mode. The UI layer is WPF with a custom ResourceDictionary theming system. This stack is entirely first-party Microsoft, well-supported through 2028, and avoids unnecessary dependencies.

The most important finding from research is a hard blocker in the original spec: `PublishAot=true` is not supported by WPF as of .NET 10, and this is unlikely to change in the near term (dotnet/wpf#3811 has been open since 2020 with 393+ trimmer warnings, and the .NET 10 WPF release notes contain no AOT work). The startup performance target of less than 200ms is achievable only for warm starts with a framework-dependent deployment using `PublishReadyToRun=true`. Cold starts on self-contained WPF apps are documented at 500ms–3 seconds due to Windows Defender scanning. The roadmap must address this spec correction in Phase 1 to avoid the project chasing an impossible constraint.

The second systemic risk is the Windows virtual desktop COM API. The interfaces required for desktop change notifications and window-drag detection (`IVirtualDesktopNotification`, `IVirtualDesktopManagerInternal`) are undocumented and their COM interface GUIDs change with major Windows updates (23H2, 24H2 already differ). The mitigation is total isolation: all COM code lives behind a `VirtualDesktopService` boundary that exposes only C# events. The rest of the app never touches COM types. Third-party wrappers (Grabacr07/VirtualDesktop, Slions.VirtualDesktop.WPF) have not verified .NET 10 compatibility and should be evaluated against hand-rolled COM interop using the MScholtes per-build GUID dispatch pattern.

## Key Findings

### Recommended Stack

The core stack is .NET 10 / WPF / C# 13 with `Microsoft.Data.Sqlite` 10.0.3 for persistence. These are all first-party, non-negotiable for the project, and fully supported. No ORM is needed — the 4-table SQLite schema (notes, app_state, pending_moves, preferences) is best served by raw ADO.NET, which is also AOT-annotated. The only external dependency worth adopting is `NHotkey` for cleaner global hotkey registration, but even this is optional — direct P/Invoke `RegisterHotKey` is well-understood and has no library risk.

Virtual desktop library support for .NET 10 is unconfirmed. Grabacr07/VirtualDesktop 5.0.5 and Slions.VirtualDesktop.WPF 6.9.2 both list net6–net8 as supported targets; .NET 10 compatibility is inferred by TFM fallback, not tested. Given the COM GUID instability across Windows builds, hand-rolling `[ComImport]` interfaces with per-OS-build GUID dispatch (as MScholtes does) may be the more reliable long-term path, even if it requires more initial code.

**Core technologies:**
- .NET 10 / WPF — UI framework, non-negotiable; LTS through 2028
- C# 13 — language, ships with .NET 10
- Microsoft.Data.Sqlite 10.0.3 — SQLite persistence; AOT-annotated, no EF Core overhead
- System.Threading.Mutex + System.IO.Pipes (BCL) — single-instance guard and IPC; no dependencies needed
- P/Invoke RegisterHotKey — global hotkey; no .NET API exists for this; NHotkey optional wrapper
- PublishReadyToRun=true — startup optimization replacing the unachievable PublishAot=true
- Hand-rolled COM interop or Grabacr07/VirtualDesktop (verify .NET 10 compat first)

### Expected Features

The feature research identifies a clear MVP boundary. Virtual desktop context is the product's only genuine differentiator — without it, JoJot is just another tabbed notepad. Three-tier session matching (GUID, name, index) is what makes the differentiator reliable across reboots, not just a demo. Custom undo/redo per tab is required infrastructure, not a polish feature, because WPF's built-in TextBox undo is incompatible with tab switching.

**Must have (table stakes):**
- Plain-text editing with autosave (500ms debounce) — core product promise
- Session restore on reopen — users assume this in 2026
- Tabbed interface with create/delete/switch (Ctrl+T, Ctrl+W) — muscle memory
- Custom undo/redo per tab — WPF native fails on tab switch; must replace it
- Dark/Light/System theme — non-negotiable in 2026
- Full keyboard shortcut set — power users won't adopt without this
- Deletion toast with 4-second undo — no confirmation dialogs
- Tab rename (inline, F2, context menu)

**Should have (competitive differentiators):**
- One window per virtual desktop — the product's unique value; without this, JoJot is indistinguishable
- Three-tier session matching (GUID, name, index) — makes the differentiator survive reboots
- Window title showing active desktop name — instant orientation
- Global hotkey (Win+Shift+N) — adoption driver; Heynote proves this matters
- Taskbar integration (left-click = focus/create, middle-click = quick capture)
- Tab pinning with sort-to-top
- Orphaned session recovery panel
- File drop with content inspection (extension-agnostic)
- Preferences dialog

**Defer (v2+):**
- Drag window between desktops UI (lock overlay + merge/reparent) — high complexity, not launch-blocking
- Memory pressure collapse for undo stacks — 50MB budget; defer until real-world data shows need
- Coarse checkpoint tier (5-minute undo intervals) — launch with tier-1 only
- Background schema migrations — needed as schema evolves; defer until first schema change

**Hard anti-features (never build):**
- Rich text or Markdown rendering — destroys the capture-first value proposition
- Cloud sync — local-first is the product's identity
- Cross-desktop full-text search — breaks the per-desktop mental model

### Architecture Approach

The architecture is a single-process, multi-window WPF application with a strict layered dependency hierarchy. The process never exits when a window closes; it persists as a background process waiting for IPC commands from taskbar clicks or the global hotkey. All durable state lives in `DataService` (SQLite); all COM interaction lives in `VirtualDesktopService`; all window lifecycle lives in `WindowManager`. These three boundaries are the key architectural decisions and must be established before any feature work.

**Major components:**
1. `SingleInstanceGuard` + `PipeServer`/`PipeClient` — process-level single-instance via named mutex; IPC via named pipe for taskbar/hotkey commands
2. `VirtualDesktopService` — COM interop isolation; exposes only C# events to the rest of the app; contains all failure modes
3. `WindowManager` — owns the `desktop_guid → MainWindow` dictionary; creates and destroys windows; orchestrates merge/reparent on drag
4. `DataService` — single SQLite connection; WAL mode; all CRUD; no SQL outside this class
5. `UndoManager` + `UndoStack` — per-tab undo history; 50MB global budget; replaces WPF native TextBox undo completely
6. `MainWindow` / `TabPanel` / `EditorPane` — WPF window layer; assembles controls; handles keyboard, theming, file drop
7. `StartupOrchestrator` — enforces the 10-step window-first startup sequence; defers migrations to background

**Build order dictated by architecture:**
Data layer → VirtualDesktopService → Core services (mutex, pipe, hotkey) → Editing layer → Services (preferences, theme) → Window controls → MainWindow → WindowManager → StartupOrchestrator

### Critical Pitfalls

1. **PublishAot=true on WPF crashes at runtime** — Do not set it. Use `PublishReadyToRun=true` instead. Resolve this in Phase 1 before any feature work; validate the published binary runs as a CI step.

2. **Virtual desktop COM GUIDs change with every major Windows update** — Wrap all COM calls in `VirtualDesktopService`. Use build-number dispatch to select the correct GUID per OS version. Test against Windows 11 23H2 (build 22631) and 24H2 (build 26100) specifically. Never let COM types escape the service boundary.

3. **200ms cold-start is not achievable for self-contained WPF** — Documented at 500ms–3s on real machines (dotnet/runtime#78379). Measure on a clean VM in Phase 1. Accept "< 200ms warm start" or "< 500ms cold start" as the real target. Use the window-first startup sequence (window appears, then background work).

4. **WPF TextBox native undo intercepts Ctrl+Z before the custom stack** — Set `IsUndoEnabled="False"` on the TextBox in XAML (not `UndoLimit=0`). Handle Ctrl+Z at the Window level via InputBindings. Verify with a test: type, tab-switch, Ctrl+Z — only the custom stack should respond.

5. **Desktop state stored in Window objects is lost when windows close** — All durable state must live in `DataService`. `MainWindow` holds only transient UI state. On `Window.Closing`, flush to DB before destruction. The process must survive window close.

## Implications for Roadmap

Based on combined research, the following phase structure is recommended. The ordering is driven by two constraints: (1) architectural dependencies flow bottom-up (data → services → windows), and (2) the virtual desktop COM layer is the highest-risk component and must be isolated early so its instability can be contained.

### Phase 1: Foundation and Infrastructure

**Rationale:** Everything else depends on this layer. The AOT spec correction must happen here. The single-instance mutex and named pipe IPC are required before any multi-window behavior can exist. The SQLite schema and DataService must exist before autosave, session restore, or preferences can work. Startup timing must be measured from day one.
**Delivers:** Runnable skeleton with single-instance enforcement, IPC server, SQLite schema, and a measured startup baseline. Published binary (ReadyToRun) verified to run.
**Addresses:** SQLite data model (notes, app_state, pending_moves, preferences), SingleInstanceGuard, PipeServer/PipeClient, StartupOrchestrator shell
**Avoids:** AOT publish blocker (resolve before feature work), cold-start target misalignment (measure baseline now)

### Phase 2: Virtual Desktop Integration

**Rationale:** This is the product's core differentiator and highest-risk component. It must be built before any window management can work. Isolating it as a full phase ensures COM failures are contained and the abstraction layer is designed correctly from the start.
**Delivers:** VirtualDesktopService with COM interop isolation, desktop GUID detection, C# event layer, and tested fallback behavior. Three-tier session matching. Verified against Windows 11 23H2 and 24H2.
**Addresses:** Virtual desktop detection, session matching, orphaned session identification
**Avoids:** COM GUID instability (abstraction layer), COM without fallback (graceful degradation to default GUID)

### Phase 3: Core Editing and Window Management

**Rationale:** With the foundation and virtual desktop layer in place, the window-per-desktop lifecycle can be built. The custom undo stack must be implemented before multi-tab editing is usable (WPF native undo breaks on tab switch). Autosave and session restore complete the core loop.
**Delivers:** WindowManager with one-window-per-desktop logic, MainWindow/TabPanel/EditorPane, custom UndoStack per tab, autosave with debounce, session restore on reopen.
**Addresses:** Tab list with create/delete/switch, plain-text editor, autosave, session restore, custom undo/redo, deletion toast with 4-second undo, tab rename
**Avoids:** WPF TextBox native undo interference (disable IsUndoEnabled from the start)

### Phase 4: Theming and Polish

**Rationale:** Once the core editing loop is stable, the visual layer can be completed. Theming must be done as a batch — retroactively adding ResourceDictionary tokens to a partially built UI is painful. This phase also completes the keyboard shortcut set and validates the startup time target.
**Delivers:** Light/Dark/System themes via ResourceDictionary, full keyboard shortcut set, font size controls, tab search/filter, tab labels with 3-tier fallback (name, content preview, "New note"), toolbar.
**Addresses:** Dark/Light/System theming, keyboard shortcuts, font size control, per-desktop tab search
**Avoids:** Hardcoded colors (all 10 color tokens must be verified), startup regression (measure after each addition)

### Phase 5: System Integration Features

**Rationale:** Global hotkey, taskbar integration, and file drop require the core editing loop and window management to be stable before they can be wired in. These are high-value adoption drivers that belong after the core is solid.
**Delivers:** Global hotkey (Win+Shift+N) via RegisterHotKey, taskbar left-click/middle-click handling, file drop with content inspection, preferences dialog with persistence.
**Addresses:** Global hotkey, taskbar integration, file drag-and-drop, preferences dialog, save as TXT export
**Avoids:** RegisterHotKey on background thread (must be on STA UI thread), hotkey conflict silent failure (surface to user via preferences)

### Phase 6: Advanced Virtual Desktop Features

**Rationale:** Drag-between-desktops UI (lock overlay, merge/reparent/cancel) and crash recovery for pending_moves are the most complex virtual desktop interactions. They belong after the core virtual desktop layer is proven stable.
**Delivers:** LockOverlay UI for inter-desktop drag, merge/reparent/cancel resolution flow, pending_moves crash recovery, orphaned session recovery panel, tab pinning and reorder.
**Addresses:** Drag window between desktops, merge/reparent UI, crash recovery, orphaned session recovery, tab pinning, tab clone
**Avoids:** Pending move state stored in Window objects (must be in DataService), polling fallback for missed COM events

### Phase Ordering Rationale

- Foundation before everything: The mutex, IPC, and SQLite schema have zero dependencies and are depended on by everything else.
- Virtual desktop second: It is the highest-risk component (undocumented COM, version-sensitive). Isolating it early contains the risk and lets the rest of the project proceed with a stable abstraction.
- Core editing third: Tab management and undo require the data layer and window manager concept but are independent of the COM layer details.
- Theming fourth: ResourceDictionary tokens must be defined before the UI grows further, but theming can wait until the editing loop is working.
- System integration fifth: Hotkeys and taskbar are adoption drivers, not core functionality. They need a stable window manager to receive their IPC commands.
- Advanced virtual desktop features last: Drag-between-desktops is the most complex interaction and the least launch-critical. The app ships without it; it's a v1.x addition.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 2 (Virtual Desktop):** COM API is undocumented. Before implementation, research the exact GUID values for Windows 11 23H2 vs 24H2 for IVirtualDesktopNotification and IVirtualDesktopManagerInternal. Evaluate Grabacr07/VirtualDesktop 5.0.5 vs hand-rolled COM interop against actual .NET 10 builds. This phase warrants `/gsd:research-phase`.
- **Phase 5 (System Integration):** RegisterHotKey and SetForegroundWindow have known gotchas (STA thread requirement, AttachThreadInput for focus stealing). Worth a focused research pass before implementation.

Phases with standard patterns (skip research-phase):
- **Phase 1 (Foundation):** Named mutex, named pipe, SQLite WAL — all well-documented BCL and Microsoft patterns. No research needed.
- **Phase 3 (Core Editing):** WPF TextBox, debounce timer, custom undo stack — all well-understood patterns. The spec documents them in detail.
- **Phase 4 (Theming):** WPF ResourceDictionary theming is thoroughly documented. Standard patterns apply.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All core technologies are first-party Microsoft. Only virtual desktop library .NET 10 compat is unverified (LOW sub-confidence on that specific item). |
| Features | MEDIUM-HIGH | Table stakes and anti-features are well-grounded in competitor analysis. The virtual desktop differentiator has no direct analogues to compare against — the feature set is correct but user adoption patterns are unproven. |
| Architecture | HIGH | Architecture is derived directly from detailed project spec documents (resources/ folder). Component responsibilities and data flows are fully specified. |
| Pitfalls | MEDIUM | Critical AOT/WPF conflict and COM GUID instability are confirmed by official sources (HIGH). Cold-start timing is confirmed by GitHub issue with ETW evidence (HIGH). UX pitfalls are well-grounded but some are inference from general WPF experience. |

**Overall confidence:** MEDIUM-HIGH

### Gaps to Address

- **Virtual desktop library .NET 10 compatibility:** Neither Grabacr07/VirtualDesktop 5.0.5 nor Slions.VirtualDesktop.WPF 6.9.2 explicitly lists .NET 10 as a supported target. This must be validated by actually building against .NET 10 before Phase 2 implementation begins. If compat fails, fall back to hand-rolled COM interop using MScholtes pattern.
- **Cold-start timing baseline:** The 200ms startup target must be measured on a clean Windows 11 VM with the published ReadyToRun binary in Phase 1. If it cannot be achieved, the spec constraint should be updated to "< 200ms warm start" before feature development begins. Do not treat this as a Phase 6 polish item.
- **Windows 11 24H2 COM GUID values:** The exact GUID constants for IVirtualDesktopNotification and IVirtualDesktopManagerInternal on Windows 11 24H2 (build 26100) must be sourced and validated before Phase 2 COM interop code is written. MScholtes/VirtualDesktop is the reference implementation for per-build dispatch.
- **SetForegroundWindow / AttachThreadInput behavior:** The global hotkey path requires bringing JoJot to the foreground when it may not be the active process. This is a known Windows restriction with a well-documented workaround (AttachThreadInput), but it must be validated in Phase 5 against the actual IPC + hotkey activation path.

## Sources

### Primary (HIGH confidence)
- `resources/01-data-model.md` through `resources/08-startup.md` — project spec documents (architecture, data flow, startup sequence)
- `.planning/PROJECT.md` — architectural decisions and constraints
- [dotnet/wpf#3811](https://github.com/dotnet/wpf/issues/3811) — WPF trim incompatibility, open since 2020
- [dotnet/wpf#11205](https://github.com/dotnet/wpf/issues/11205) — WPF Native AOT, closed as duplicate
- [Microsoft Learn: Native AOT deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) — WPF not listed as supported
- [dotnet/efcore#29725](https://github.com/dotnet/efcore/issues/29725) — Microsoft.Data.Sqlite AOT-annotated since .NET 8
- [Microsoft Learn: IVirtualDesktopManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager) — stable public COM interface documentation
- [Microsoft Learn: What's new in WPF for .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100) — confirms no AOT work in .NET 10

### Secondary (MEDIUM confidence)
- [dotnet/runtime#78379](https://github.com/dotnet/runtime/issues/78379) — self-contained WPF cold-start 2–3 seconds, ETW trace evidence
- [GitHub: Grabacr07/VirtualDesktop](https://github.com/Grabacr07/VirtualDesktop) — C# wrapper; per-build GUID files confirm API instability
- [GitHub: MScholtes/VirtualDesktop](https://github.com/MScholtes/VirtualDesktop) — per-OS-version COM GUID dispatch reference
- [NuGet: VirtualDesktop 5.0.5](https://www.nuget.org/packages/VirtualDesktop/) — package metadata, framework support declared
- [NuGet: Slions.VirtualDesktop.WPF 6.9.2](https://www.nuget.org/packages/Slions.VirtualDesktop.WPF) — package metadata, framework support declared
- [Notepads App GitHub](https://github.com/0x7c13/Notepads) — competitor feature reference
- [Heynote author's description](https://heyman.info/2024/heynote-scratchpad-for-developers) — competitor feature reference

### Tertiary (LOW confidence)
- AlternativeTo, Quora, appmus.com — competitor landscape, used only for corroboration; no design decisions based solely on these

---
*Research completed: 2026-03-02*
*Ready for roadmap: yes*
