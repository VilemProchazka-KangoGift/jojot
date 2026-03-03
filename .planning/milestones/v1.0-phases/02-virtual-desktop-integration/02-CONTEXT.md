# Phase 2: Virtual Desktop Integration - Context

**Gathered:** 2026-03-02
**Status:** Ready for planning

<domain>
## Phase Boundary

The app can detect the current virtual desktop via COM, maintain a stable desktop identity across reboots via three-tier session matching (GUID -> name -> index), update window titles live when desktops are renamed, and fall back gracefully when the COM API is unavailable. All COM interop is isolated behind a service boundary.

</domain>

<decisions>
## Implementation Decisions

### Fallback experience
- Silent degradation — no visual indication to the user when COM API is unavailable
- App launches normally in single-notepad mode; all notes go to a 'default' session
- Mid-session COM failure: freeze on last known desktop identity, re-detect on next restart
- In fallback mode, still create a 'default' row in app_state (persist geometry, active tab, etc.)
- If COM becomes available later (e.g. user upgrades OS), 'default' notes remain under 'default'; new desktops get their own sessions

### Window title behavior
- No truncation of long desktop names — show full name, let Windows handle taskbar overflow
- Em-dash separator with spaces: `JoJot — {desktop name}`
- Title formats by priority: `JoJot — {desktop name}` (name known) > `JoJot — Desktop N` (index only) > `JoJot` (fallback)
- Live title updates via IVirtualDesktopNotification subscription — instant, not polled
- On desktop rename notification: update both the window title AND app_state.desktop_name in SQLite immediately

### Session matching edge cases
- Ambiguous name match (multiple desktops share the same name): skip the name tier entirely, fall through to index matching
- Index matching applies only under the strict condition: exactly one unmatched session and one unmatched desktop at that index
- Sessions that fail all three tiers: mark as orphaned (preserved in DB for Phase 8 recovery panel)
- On successful match (any tier): update all three fields — desktop_guid, desktop_name, AND desktop_index in app_state
- New desktops always start with a fresh session; deleted desktop sessions become orphaned, not reassigned

### OS compatibility scope
- Windows 11 only; Windows 10 users get silent fallback mode (single-notepad)
- COM GUID mappings hardcoded in source code (static dictionary, no external config file)
- Unsupported or unknown OS builds: graceful fallback + log warning ("Unsupported OS build {X} — virtual desktop features disabled")
- Requires a code update + release to add support for new Windows builds

### Claude's Discretion
- Logging granularity for COM failures (once per session vs. every error)
- Which specific Windows 11 builds to support (23H2 and 24H2 minimum per success criteria; researcher determines if 22H2 should be included)
- COM notification subscription lifecycle management (when to subscribe/unsubscribe)
- Internal error handling strategy within VirtualDesktopService

</decisions>

<specifics>
## Specific Ideas

- The title should feel like a native Windows app — `JoJot — Desktop 1` mirrors how VS Code does `file.txt — Visual Studio Code`
- Fallback mode should be completely invisible — the user should never wonder why something is broken; it just works as a single notepad
- Session matching should be conservative — better to orphan a session and let the user recover it later than to assign notes to the wrong desktop

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `DatabaseService` (JoJot/Services/DatabaseService.cs): Static service with `app_state` table already storing `desktop_guid`, `desktop_name`, `desktop_index`; `notes` table links to desktops via `desktop_guid`
- `LogService` (JoJot/Services/LogService.cs): Logging infrastructure ready for COM diagnostic logging
- `IpcMessage` model (JoJot/Models/IpcMessage.cs): `ShowDesktopCommand(string DesktopGuid)` already defined for desktop-switching IPC
- `StartupService` (JoJot/Services/StartupService.cs): Welcome tab uses `'default'` as desktop_guid — aligns with fallback mode design

### Established Patterns
- Static service classes (DatabaseService, LogService, IpcService, StartupService) — new VirtualDesktopService should follow this pattern
- Async methods with `LogService.Error` for exception logging
- Startup orchestration in `App.xaml.cs.OnAppStartup` with numbered steps

### Integration Points
- `App.xaml.cs` Step 6 has a placeholder: "Phase 10: await DatabaseService.ResolvePendingMovesAsync()" — virtual desktop detection should integrate between Step 5 (integrity check) and Step 7 (welcome tab)
- `App.xaml.cs` Step 9 creates MainWindow — title setting needs to happen here or immediately after
- `HandleIpcCommand` has a stub for `ShowDesktopCommand` — Phase 2 will need to implement this handler
- `MainWindow.xaml` title property needs dynamic binding or code-behind updates

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 02-virtual-desktop-integration*
*Context gathered: 2026-03-02*
