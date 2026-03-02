# Pitfalls Research

**Domain:** WPF desktop notepad with virtual desktop integration, Native AOT, SQLite, multi-window single-process
**Researched:** 2026-03-02
**Confidence:** MEDIUM — Critical AOT/WPF conflict confirmed by official sources; COM API instability confirmed by community; startup time gap confirmed by GitHub issues; other findings from multiple corroborating sources.

---

## Critical Pitfalls

### Pitfall 1: WPF is Not Native AOT Compatible

**What goes wrong:**
The project spec requires `PublishAot=true`. WPF is not trim-compatible and does not support Native AOT as a first-class feature in any .NET version including .NET 10. Setting `PublishAot=true` on a WPF project generates hundreds of trimming warnings and the published binary may crash at runtime in untested code paths. GitHub issue dotnet/wpf#3811 (opened 2020) has 377+ trimming warnings and remains open. The .NET 10 WPF release notes contain no mention of Native AOT or trim compatibility work.

**Why it happens:**
WPF makes heavy use of reflection for XAML binding, `x:Type` markup extensions, dependency property registration, dynamic resource resolution, and data binding type converters. These patterns are fundamentally incompatible with the trimmer's static analysis. Developers see the spec say "Native AOT" and enable `PublishAot=true`, then find a mix of runtime crashes and suppressed warnings.

**How to avoid:**
Accept that WPF + Native AOT in the traditional sense is not achievable in 2026. The correct interpretation of the spec's "Native AOT" intent is: achieve fast startup and self-contained deployment without requiring a pre-installed runtime. The practical path is `PublishSingleFile=true` combined with `ReadyToRun=true` and `SelfContained=true` — this provides a single distributable binary with significantly reduced startup overhead without requiring full AOT compilation. Do NOT set `PublishAot=true` unless you can verify every used XAML and WPF feature survives trimming (currently impossible). Test the actual published binary as a separate CI step — `dotnet run` will appear to work fine even when the AOT publish would fail.

**Warning signs:**
- Build log shows IL2xxx trimming warnings when `PublishAot=true` is set
- App crashes in published binary but not in `dotnet run` debug build
- Libraries (e.g., third-party converters, Prism, etc.) are added without checking AOT compatibility

**Phase to address:**
Foundation / Phase 1 — resolve publish strategy before any feature work begins. Validate the publishing pipeline produces a runnable binary on first day.

---

### Pitfall 2: Virtual Desktop COM API GUIDs Break with Every Major Windows Update

**What goes wrong:**
The `IVirtualDesktopNotification`, `IVirtualDesktopManagerInternal`, and related undocumented COM interfaces use GUIDs that Microsoft changes with major Windows updates (23H2, 24H2, etc.). Apps using these interfaces break silently — calls simply fail or return wrong results — after a Windows update. Affected developers then scramble to reverse-engineer the new GUIDs. Community-maintained libraries (e.g., Grabacr07/VirtualDesktop) maintain separate implementation files per Windows build version and must be updated after each major release.

**Why it happens:**
Only `IVirtualDesktopManager` (shobjidl_core.h) is a documented, stable public API. The features JoJot needs — `IVirtualDesktopNotification` events for window drag detection, `IVirtualDesktopManagerInternal` for getting desktop names and enumerating desktops — are internal, undocumented COM interfaces. Microsoft does not guarantee stability and changes them without notice.

**How to avoid:**
1. Wrap all virtual desktop COM calls behind an abstraction layer (`IVirtualDesktopService`) from day one. Never call COM methods directly from business logic.
2. Use the documented `IVirtualDesktopManager` for the core "which desktop is this window on" check — it is stable.
3. For undocumented interfaces, use the community pattern of runtime GUID selection based on `Environment.OSVersion.Version.Build` — switch on build number to pick the correct COM interface GUID.
4. Fall back gracefully: if any COM call returns `E_NOINTERFACE` or similar HRESULT failure, activate the documented fallback described in the spec (fall back to `"default"` GUID, single-desktop mode).
5. Test specifically against Windows 11 24H2 (build 26100) and 23H2 (build 22631) since these have different GUIDs for these interfaces.

**Warning signs:**
- `IVirtualDesktopNotification` events stop firing after a Windows update
- `GetWindowDesktopId` works but desktop name resolution returns null
- Users on Windows 11 24H2 get different behavior than 23H2

**Phase to address:**
Virtual Desktop phase — build the abstraction layer first, implement COM calls within it, never expose COM types outside the service class.

---

### Pitfall 3: Native AOT Has No Built-in COM — Virtual Desktop Calls Need ComWrappers

**What goes wrong:**
If `PublishAot=true` is eventually pursued (or a future .NET version enables WPF AOT), the COM interop path used for virtual desktop APIs (Marshal-based RCW/CCW) does not work. Native AOT explicitly states "No built-in COM" as a limitation. The `Marshal.ReleaseComObject`, `Marshal.GetComInterfaceForObject`, and standard `[ComImport]` patterns will either be stripped at compile time or fail at runtime.

**Why it happens:**
Native AOT cannot generate COM RCW/CCW infrastructure dynamically. The traditional COM interop layer relies on runtime code generation which AOT forbids.

**How to avoid:**
If any future AOT path is pursued, all COM interfaces must be reimplemented using `ComWrappers` — the explicit opt-in COM interop mechanism that works with AOT. For the virtual desktop APIs specifically, this means manually defining vtable layouts and implementing `IComWrappers`. This is significant effort. For the current WPF project without AOT, standard `[ComImport]` is fine. Abstract it behind an interface so the implementation can be swapped.

**Warning signs:**
- `[ComImport]` interfaces used directly outside an abstraction layer
- `Marshal.ReleaseComObject` called from multiple places in the codebase

**Phase to address:**
Virtual Desktop phase — design the COM interop layer with future AOT migration in mind even if ComWrappers are not implemented now.

---

### Pitfall 4: WPF Cold-Start Time of < 200ms Is Unachievable Without Mitigation

**What goes wrong:**
The spec requires < 200ms to first interactive window. Self-contained WPF apps on .NET Core/5+ take 500ms–3 seconds on cold start (first launch after Windows boot), primarily due to assembly loading and Windows Defender scanning every managed DLL. Subsequent starts (warm) are faster but still rarely reach 200ms for non-trivial apps. GitHub issue dotnet/runtime#78379 confirmed this. .NET Framework 4.8 WPF could reach ~200ms because it used the pre-installed shared runtime which Defender had already scanned.

**Why it happens:**
.NET Core WPF ships as self-contained with many assemblies. On first launch post-reboot, Defender scans each assembly file before the CLR can load it. The CLR also must JIT-compile methods on first call. Both effects are eliminated on warm starts.

**How to avoid:**
1. Use `ReadyToRun=true` + `SelfContained=true` + `PublishSingleFile=true` — ReadyToRun pre-compiles to native code, eliminating JIT cost; single file reduces the number of individual file-scan events.
2. Measure the actual cold-start time on a clean Windows 11 machine before committing to the 200ms target. If it cannot be met, adjust the spec to "< 200ms warm start" or "< 500ms cold start" — do not spend the project chasing an impossible target.
3. Keep the critical path minimal: no DB migrations, no COM calls that can fail slowly, no synchronous I/O on main thread before window appears.
4. Use the startup sequence exactly as specced: window appears first, background work happens after.
5. Instrument startup with `Stopwatch` from day one so regression is caught immediately.

**Warning signs:**
- Cold-start timing never measured during development (only warm-start in dev loop)
- Dependencies added to the startup path without measuring their init cost
- SQLite schema creation taking >20ms due to unindexed first queries

**Phase to address:**
Foundation / Phase 1 — measure baseline startup immediately after skeleton is created. Startup sequence phase — verify the 200ms target is achievable before finalizing the spec constraint.

---

### Pitfall 5: WPF TextBox Native Undo Fights the Custom Undo Stack

**What goes wrong:**
WPF `TextBox` has its own built-in undo manager that intercepts `Ctrl+Z` / `Ctrl+Y` at the control level before the command reaches the Window or Application. When you implement a custom `UndoStack`, you must explicitly disable the native TextBox undo, or both stacks fire simultaneously. Additionally, the native undo manager keeps its own copy of content history in memory (up to 100 operations by default), doubling memory usage per tab. If the native undo is not disabled, it will also clear its stack on property assignments (e.g., setting `Text` from code), which conflicts with tab switching in JoJot.

**Why it happens:**
The TextBox processes `ApplicationCommands.Undo` internally via its `CommandBinding` before it bubbles. Developers often disable native undo by setting `UndoLimit=0` but forget that `IsUndoEnabled=false` is the correct property. Using the wrong property leaves partial undo behavior.

**How to avoid:**
Set `IsUndoEnabled="False"` on the TextBox in XAML — not `UndoLimit`. Handle `Ctrl+Z` and `Ctrl+Y` at the Window level via `InputBindings` and route them to the active tab's `UndoStack`. Verify with a test: type 5 characters, press `Ctrl+Z` five times — only the custom stack should trigger.

**Warning signs:**
- Both `UndoLimit=0` and `IsUndoEnabled=false` appear in the same XAML (developer unsure which works)
- Tab switching causes text to "jump back" unexpectedly
- Memory usage grows even when the custom stack is capped

**Phase to address:**
Editing phase — validate TextBox undo is completely disabled as the first step before implementing the custom stack.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Calling COM methods directly without abstraction layer | Faster initial implementation | Every Windows update potentially breaks the app; no testability | Never — always wrap behind interface |
| Suppressing AOT/trimming warnings with `<SuppressILWarnings>` | Build succeeds without refactoring | Runtime crashes in production for paths the dev didn't test; false sense of AOT compatibility | Never for this project |
| Multiple SQLite connections for "convenience" | Simpler code in each module | WAL mode write conflicts; "database is locked" errors in production; undefined behavior | Never — spec mandates single connection |
| Using WPF DataBinding for undo state (`INotifyPropertyChanged`) | Less boilerplate | Binding observers keep tabs alive after window close; memory leak via event subscriptions | Never — undo stacks must be explicitly managed |
| Storing desktop GUID as the only session identifier | Simpler schema | All sessions become orphaned after every reboot since GUIDs are reassigned | Never — three-tier matching is mandatory |
| Calling `SetForegroundWindow` directly without AttachThreadInput | Works in development | Fails intermittently in production when JoJot is not the foreground process | Acceptable only if the global hotkey path uses AttachThreadInput fallback |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| `IVirtualDesktopManager` COM | Assuming `GetWindowDesktopId` returns the current user-selected desktop | It returns the desktop the HWND was assigned to, which only updates after the window is fully moved — poll on `WM_ACTIVATEAPP` as the spec requires |
| Named pipe IPC | Using `NamedPipeServerStream` with `WaitForConnection()` blocking the main thread | Run pipe server on a dedicated background thread; marshal incoming messages to the UI dispatcher |
| `RegisterHotKey` | Calling from a background thread | Must be called on the STA UI thread that owns the HWND; call from the Window's `Loaded` event |
| `SetForegroundWindow` | Calling directly when JoJot is not the foreground process | Windows blocks this; use `AttachThreadInput` to attach to the foreground thread first, then call `SetForegroundWindow`, then detach |
| SQLite WAL mode | Opening the connection in a `using` block per query | Connection pooling leaves the WAL file locked; use a single long-lived connection for the app lifetime |
| `IVirtualDesktopNotification` subscription | Registering notification sink on the wrong thread | COM notification callbacks arrive on the thread that registered; that thread must pump messages; register on the main STA thread |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| SQLite write on main thread per keystroke | UI stutters at 500ms intervals during typing | Autosave writes on a background thread via `Task.Run`; debounce timer fires on main thread, actual write dispatched off it | Immediately on slow machines or large note content |
| `TextChanged` event firing on programmatic `Text` set | Autosave triggers during tab restoration, creating false dirty state and extra DB writes | Guard autosave with an `_isLoading` flag; suppress events during tab load | Every time a tab is switched |
| UndoStack memory growth without collapse trigger | JoJot process grows to 500MB+ after 8 hours of use across many tabs | Measure actual memory baseline per tab; the collapse threshold must be tested, not assumed | After ~30 minutes with 10+ active tabs and frequent typing |
| XAML parser cost during window creation | Each new window takes 150ms to parse XAML | Use `XamlReader.Load` with precompiled XAML (the default in WPF builds) — do not dynamically generate XAML strings | Every window open in multi-desktop scenario |
| Drag-and-drop content inspection on main thread | File drop blocks UI for 1-2 seconds with large files | Read file on `Task.Run`; display drop feedback immediately; show result when inspection completes | Files approaching the 500KB limit |

---

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Deletion toast dismissed before user notices | User loses note with no recovery path | Position toast at bottom of tab list where eye naturally rests after clicking Delete; make it 4 seconds, never less |
| Lock overlay covers entire window including title bar | User cannot see which desktop owns the window during drag resolution | Keep title bar visible above overlay as specced — title bar stays interactive |
| Global hotkey `Win+Shift+N` conflicting with OS or other apps | Hotkey silently fails; user thinks app is broken | `RegisterHotKey` returns false on conflict; surface this as a visible error in Preferences, not a silent failure. Offer fallback registration at startup with user-visible failure notification |
| Tab search (Ctrl+F) capturing focus and breaking typing | User types into search instead of note | Make Ctrl+F clearly toggle search bar with obvious visual state; pressing Escape returns focus to editor immediately |
| Empty tab cleanup on window close deleting unsaved intent | User opened blank tab to capture something, closes window briefly, returns to find tab gone | The spec mandates deleting empty tabs on window close — this is intentional but counterintuitive; consider minimum 1-second age before auto-delete |

---

## "Looks Done But Isn't" Checklist

- [ ] **Startup sequence:** Window appears to show in dev environment — verify actual cold-start timing on a clean VM. The dev machine has warm runtime caches; a real user machine does not.
- [ ] **Virtual desktop GUIDs:** Desktop detection works on your current Windows build — verify on both Windows 11 23H2 and 24H2. COM interface GUIDs differ between them.
- [ ] **Undo/redo:** `Ctrl+Z` appears to work — verify native TextBox undo is fully disabled by checking `IsUndoEnabled=False` is set AND that the TextBox has no residual undo history after 50 Ctrl+Z presses.
- [ ] **Single instance:** Second launch appears to close — verify the named pipe message is received by the background process before the second instance exits (pipe timing issue on slow machines).
- [ ] **Window close vs. exit:** "X" button appears to close cleanly — verify the background process remains alive after window close (no `Application.Current.Shutdown()` called).
- [ ] **Pending moves recovery:** Crash recovery appears to work — test by killing the process (Task Manager, End Task) while drag overlay is active, then relaunching.
- [ ] **Autosave debounce:** Typing appears to save — verify the write cap prevents double-writes (autosave timer and keyboard shortcut triggering two saves within 500ms).
- [ ] **Memory collapse:** App appears stable — run for 4 hours with 15 tabs open and continuous typing, then check process memory. Collapse must trigger automatically.
- [ ] **Theme switching:** Theme appears to change instantly — verify the `ResourceDictionary` swap works with all 10 color tokens AND that no UI element has a hardcoded color.
- [ ] **SQLite connection:** App appears to work — verify no secondary connection is opened anywhere (search for `new SqliteConnection` — there should be exactly one).

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| WPF AOT found incompatible late in project | HIGH | Switch to `PublishSingleFile + ReadyToRun`; measure startup time impact; document that full Native AOT is not achievable with WPF |
| COM API GUIDs changed by Windows update after ship | MEDIUM | Ship a hotfix updating GUID constants for the new Windows build; the abstraction layer makes this a one-file change |
| SQLite "database is locked" in production | MEDIUM | Enable retry logic in the connection wrapper; add `Busy Timeout=5000` to the connection string; investigate all code paths that open connections |
| Startup time exceeds 200ms on real machines | MEDIUM | Audit startup path for blocking calls; add `ReadyToRun=true`; move all non-critical initialization to post-window-shown background work |
| Memory leak from event subscriptions discovered late | HIGH | Audit all `+=` event subscriptions against their corresponding `-=` in Dispose/Unloaded; introduce a `CompositeDisposable` pattern for all tab event wiring |
| Custom undo stack conflicting with TextBox native undo | LOW | Set `IsUndoEnabled=False` on the TextBox; verify with automated test |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| WPF + Native AOT incompatibility | Phase 1: Foundation | Publish with `ReadyToRun + SelfContained + SingleFile`; confirm binary runs and startup time is measured |
| Virtual desktop COM GUID instability | Virtual Desktop phase | Test against Windows 11 23H2 and 24H2; verify abstraction layer intercepts failures |
| COM + AOT (no built-in COM) | Virtual Desktop phase | Design abstraction layer with interface; document ComWrappers as future migration path |
| Cold-start < 200ms target | Phase 1: Foundation | Measure on clean VM before declaring target achievable; adjust spec if needed |
| TextBox native undo interference | Editing phase | Write a test: type, tab-switch, Ctrl+Z — verify only custom stack responds |
| SetForegroundWindow failure | Window management phase | Test focus acquisition from taskbar hotkey when JoJot is not in foreground |
| SQLite threading / locking | Foundation / DB phase | Grep for `new SqliteConnection` — must appear exactly once in codebase |
| Event subscription memory leaks | Throughout — audit at each phase boundary | Run dotMemory or VS diagnostic snapshot after 30 minutes of simulated use |
| UndoStack memory pressure | Editing phase | Automated test: create 20 tabs, fill each with 10KB, check memory stays under 50MB budget |
| Global hotkey conflict | Preferences / keyboard phase | Register hotkey; verify failure is surfaced to user; test with conflicting app installed |

---

## Sources

- [dotnet/wpf#3811 — WPF is not trim-compatible (open since 2020)](https://github.com/dotnet/wpf/issues/3811) — HIGH confidence: official Microsoft repo
- [dotnet/wpf#11205 — Make WPF Native AOT compatible (closed as duplicate)](https://github.com/dotnet/wpf/issues/11205) — HIGH confidence: official Microsoft repo
- [Native AOT deployment overview — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) — HIGH confidence: official docs. Explicit limitation: "Windows: No built-in COM"
- [Native code interop with Native AOT — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop) — HIGH confidence: official docs. P/Invoke works; ComWrappers required for COM
- [What's new in WPF for .NET 10 — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100) — HIGH confidence: confirms no AOT or trim work in .NET 10 WPF
- [dotnet/runtime#78379 — Self-contained WPF cold start 2-3 seconds](https://github.com/dotnet/runtime/issues/78379) — HIGH confidence: official repo issue with ETW trace evidence
- [Grabacr07/VirtualDesktop — C# wrapper for Virtual Desktop API](https://github.com/Grabacr07/VirtualDesktop) — MEDIUM confidence: community library that maintains per-build-version GUID files, confirming API instability
- [MScholtes/VirtualDesktop — Windows 10/11 virtual desktop tool](https://github.com/MScholtes/VirtualDesktop) — MEDIUM confidence: multiple build-version implementation files confirming GUID changes per Windows release
- [IVirtualDesktopManager — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager) — HIGH confidence: official docs confirming only GetWindowDesktopId and IsWindowOnCurrentVirtualDesktop are documented/stable
- [Undocumented APIs — Windows Compatibility Cookbook](https://learn.microsoft.com/en-us/windows/compatibility/undocumented-apis) — HIGH confidence: Microsoft's own guidance not to use undocumented APIs
- [Using COM in NativeAOT — Medium/codevision](https://codevision.medium.com/using-com-in-nativeaot-131dbc0d559e) — MEDIUM confidence: explains ComWrappers requirement for COM in AOT
- [WinFormsComInterop — kant2002/GitHub](https://github.com/kant2002/WinFormsComInterop) — MEDIUM confidence: demonstrates COM + AOT requires ComWrappers, WPF even harder than WinForms
- [NativeAOT/trimming compatibility for Microsoft.Data.Sqlite — dotnet/efcore#29725](https://github.com/dotnet/efcore/issues/29725) — HIGH confidence: confirmed resolved in .NET 8+ — SQLite is AOT-compatible
- [Window Activation Headaches in WPF — Rick Strahl's Web Log](https://weblog.west-wind.com/posts/2020/Oct/12/Window-Activation-Headaches-in-WPF) — MEDIUM confidence: well-documented SetForegroundWindow restrictions
- [Fighting Common WPF Memory Leaks — JetBrains Blog](https://blog.jetbrains.com/dotnet/2014/09/04/fighting-common-wpf-memory-leaks-with-dotmemory/) — MEDIUM confidence: event subscription leaks in WPF are well-established
- [Weak event patterns — WPF Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/events/weak-event-patterns) — HIGH confidence: official documentation on the problem and solution
- [SQLite WAL write-ahead logging — sqlite.org](https://sqlite.org/wal.html) — HIGH confidence: official SQLite documentation confirming single-writer behavior and WAL constraints
- [Implementing Global Hot Keys in WPF — Magnus Montin](https://blog.magnusmontin.net/2015/03/31/implementing-global-hot-keys-in-wpf/) — MEDIUM confidence: RegisterHotKey must be on STA thread, conflict handling required

---
*Pitfalls research for: WPF desktop notepad with virtual desktop integration (JoJot)*
*Researched: 2026-03-02*
