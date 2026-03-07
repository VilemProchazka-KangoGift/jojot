---
status: diagnosed
trigger: "Named desktops showing as 'Desktop N' instead of actual names"
created: 2026-03-05T12:30:00Z
updated: 2026-03-05T12:45:00Z
---

## Current Focus

hypothesis: IVirtualDesktop.GetName() returns empty string on Windows 11 25H2 (build 26200) due to COM interface incompatibility -- either a vtable shift or the method silently fails. The fallback path in UpdateDesktopTitle then produces "Desktop {index+1}". Additionally, IVirtualDesktopNotificationService GUID is wrong for 25H2, so live rename notifications never arrive either.
test: Confirmed via log analysis and registry cross-reference
expecting: N/A -- root cause confirmed
next_action: Implement fix (two approaches available)

## Symptoms

expected: Window titles and drag overlay should show custom desktop names (e.g., "JoJot", "Reddit Scraper", "MintyPen")
actual: All desktops show as "Desktop N" (e.g., "Desktop 1", "Desktop 5") everywhere -- window title and move overlay
errors: IVirtualDesktopNotificationService fails with HRESULT 0x80004002 (E_NOINTERFACE) on every startup
reproduction: Launch JoJot on Windows 11 25H2 (build 26200). All desktops have custom names but display as "Desktop N".
started: Since upgrading to Windows 11 25H2 (build 26200) -- COM GUIDs map only covers builds 22621 and 26100

## Eliminated

- hypothesis: GetName() is not being called
  evidence: Code path at VirtualDesktopInterop.cs:129 and :183 clearly calls desktop.GetName(). Logs show name="" consistently, meaning the call executes without throwing (or throws silently).
  timestamp: 2026-03-05T12:35:00Z

- hypothesis: Desktops are not actually named
  evidence: Registry at HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops\ confirms all 6 desktops have Name values (Fun&games, Reddit Scraper, JoJot, KangoGift, MintyPen, bzuky)
  timestamp: 2026-03-05T12:37:00Z

- hypothesis: COM initialization is failing entirely
  evidence: Logs show "VirtualDesktopInterop: initialization complete", desktop GUIDs are correctly detected, GetId() returns correct GUIDs, desktop index is correct. COM is working for everything EXCEPT GetName().
  timestamp: 2026-03-05T12:38:00Z

## Evidence

- timestamp: 2026-03-05T12:32:00Z
  checked: Application logs at %LocalAppData%\JoJot\jojot.log
  found: >
    Every single startup on build 26200 shows name="" for ALL desktops. Example:
    `VirtualDesktopService: available=true, desktop=3e012ed7-..., name="", index=4`
    This pattern is 100% consistent across every startup in the log file (20+ entries).
  implication: GetName() never returns a non-empty value on this OS build

- timestamp: 2026-03-05T12:33:00Z
  checked: IVirtualDesktopNotificationService initialization in logs
  found: >
    Every startup shows: `QueryService for IVirtualDesktopNotificationService failed (HRESULT: 0x80004002)`
    HRESULT 0x80004002 = E_NOINTERFACE -- the GUID used for the notification service interface is not recognized by Windows on this build.
  implication: >
    The notification service GUID in ComGuids.cs (88846798-1611-4D18-946A-5B8B2B5B0B80, for build 26100)
    does not match the actual GUID on build 26200 (Windows 11 25H2). Live desktop rename notifications
    are completely broken, but this is a SECONDARY issue -- even at startup, GetName() fails.

- timestamp: 2026-03-05T12:35:00Z
  checked: OS build number
  found: Windows 11 build 26200.7840 (25H2)
  implication: >
    ComGuids.Resolve(26200) returns the GuidSet for build 26100 (closest lower-or-equal).
    The IVirtualDesktop GUID 3F07F4BE is the same for 22621 and 26100 in the map, but may have
    changed for 26200 (25H2). Even if the IID is the same, the vtable layout may have shifted.

- timestamp: 2026-03-05T12:36:00Z
  checked: IVirtualDesktop interface definition in ComInterfaces.cs
  found: >
    Vtable layout: [0] IsViewVisible, [1] GetId, [2] GetName, [3] GetWallpaperPath, [4] IsRemote.
    GetId() (slot 1) returns correct GUIDs -- confirmed by log matching registry GUIDs.
    GetName() (slot 2) returns empty -- either the method silently returns empty HString,
    or a different vtable slot is being called that returns empty.
  implication: >
    If a new method was inserted between GetId and GetName in 25H2's vtable, then calling
    slot 2 would invoke the NEW method instead of GetName, potentially returning empty/null.
    GetId at slot 1 would still work correctly. This is a plausible vtable-shift scenario.

- timestamp: 2026-03-05T12:37:00Z
  checked: Registry desktop names
  found: >
    HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops\{GUID}\Name
    All 6 desktops have custom names:
    - {2A2A3EEF-...}: Fun&games
    - {329901B6-...}: Reddit Scraper
    - {3E012ED7-...}: JoJot
    - {85AD6151-...}: KangoGift
    - {A3C60CCE-...}: MintyPen
    - {DAD9E402-...}: bzuky
  implication: Desktop names ARE available via the registry as a reliable fallback

- timestamp: 2026-03-05T12:38:00Z
  checked: Error handling in GetName() calls
  found: >
    Both GetCurrentDesktop() (line 127-134) and GetAllDesktopsInternal() (line 181-188) wrap
    GetName() in a bare `catch` that silently swallows ALL exceptions and returns "".
    There is no logging in the catch block. We cannot distinguish between:
    (a) GetName() returning empty string (COM call succeeds but HString is null/empty)
    (b) GetName() throwing an exception (COM call fails, caught silently)
  implication: Need diagnostic logging in the catch block to determine failure mode

- timestamp: 2026-03-05T12:40:00Z
  checked: UpdateDesktopTitle logic in MainWindow.xaml.cs (line 1968-1981)
  found: >
    Logic is: if name is non-empty, use name; else if index has value, show "Desktop {index+1}".
    Since GetName() always returns "" on build 26200, the fallback ALWAYS triggers.
  implication: The display logic is correct -- the bug is in the data layer (GetName returning empty)

- timestamp: 2026-03-05T12:41:00Z
  checked: ShowDragOverlayAsync display name logic (line 3672-3686)
  found: >
    For the target desktop name, if toName is empty (which it always is since it comes from
    GetAllDesktops() which also returns empty names), it falls back to "Desktop {Index+1}".
    Same pattern -- correct fallback logic, wrong upstream data.
  implication: All name display paths are affected because the root data source (GetName()) fails

- timestamp: 2026-03-05T12:42:00Z
  checked: Notification service GUID for 25H2
  found: >
    IVirtualDesktopNotificationService GUID 88846798-1611-4D18-946A-5B8B2B5B0B80 (from 26100 build map)
    fails with E_NOINTERFACE on build 26200. This means Windows 11 25H2 changed this GUID.
    Similarly, IVirtualDesktopNotification GUID C179334C-4295-40D3-BEA1-C654D965605A may also
    be wrong for 25H2.
  implication: Even if GetName() were fixed, live rename notifications would remain broken without correct 25H2 GUIDs

## Resolution

root_cause: >
  TWO related issues on Windows 11 25H2 (build 26200):

  **PRIMARY: IVirtualDesktop.GetName() returns empty string for ALL desktops.**
  The COM call to GetName() either returns an empty HString or throws a silently-caught exception.
  Most likely cause: vtable layout shift in the IVirtualDesktop interface on build 26200. The
  IVirtualDesktop GUID (3F07F4BE) may be the same across builds, but a new method may have been
  inserted before GetName() in the vtable, causing the COM interop to call the wrong method slot.
  Since GetId() (vtable slot 1) still works correctly, the shift would be between GetId and GetName.

  Alternatively, the IVirtualDesktop IID itself may have changed for 26200 but the code is using
  the 26100 IID. The `IObjectArray.GetAt()` call uses `_guidSet.IVirtualDesktop` as the IID, and
  if this IID is wrong, the QueryInterface might succeed but return a different interface
  implementation that has an empty GetName(). However, this is less likely since GetId() works.

  The silent catch blocks at VirtualDesktopInterop.cs:131-134 and :185-188 mask whether GetName()
  is throwing or returning empty, making diagnosis harder.

  **SECONDARY: IVirtualDesktopNotificationService GUID wrong for build 26200.**
  The notification service GUID from the 26100 build map does not match 25H2, causing
  E_NOINTERFACE (0x80004002) on every startup. Live desktop rename notifications never work.

fix: (not applied -- research only)
verification: (not performed -- research only)
files_changed: []

## Suggested Fix Directions

### Direction 1: Registry Fallback (Recommended -- Immediate, Reliable)

Add a registry-based name lookup as fallback when COM GetName() returns empty:
- Path: `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops\{GUID}\Name`
- This registry path stores desktop names reliably across all Windows 11 builds
- Implement in VirtualDesktopInterop.cs or VirtualDesktopService.cs
- Already proven to contain correct names on this system (verified via PowerShell)
- Several open-source virtual desktop tools (Rainmeter plugins, AutoHotkey scripts) use this approach

### Direction 2: Find Correct 25H2 COM GUIDs (For Full Fix)

Research the correct COM interface GUIDs for build 26200 and add a new entry to ComGuids._buildMap:
- IVirtualDesktop IID may have changed (despite being the same in 22621 and 26100)
- IVirtualDesktopManagerInternal IID may have changed
- IVirtualDesktopNotification IID definitely changed (notification service fails)
- IVirtualDesktopNotificationService IID definitely changed (E_NOINTERFACE)
- Finding correct GUIDs requires reverse-engineering twinui.pcshell.dll on build 26200

### Direction 3: Add Diagnostic Logging (For Investigation)

Add logging inside the GetName() catch blocks to determine if the call is:
- Returning empty string (COM succeeds, HString is null/empty)
- Throwing a specific COM exception (vtable mismatch, access violation, etc.)
This would narrow down whether the issue is vtable shift vs IID mismatch.

### Recommended Approach

Combine Direction 1 (registry fallback) with Direction 3 (diagnostic logging):
1. When GetName() returns empty, look up the name from the registry using the desktop's GUID
2. Add logging to the GetName() catch block to capture the actual exception type and message
3. Later, pursue Direction 2 to find the correct 25H2 GUIDs for a proper COM fix

### Files That Need Changes

- `JoJot/Interop/VirtualDesktopInterop.cs` -- Add registry fallback in GetCurrentDesktop() and GetAllDesktopsInternal(), add diagnostic logging in catch blocks
- `JoJot/Interop/ComGuids.cs` -- Eventually add build 26200 GuidSet entry once correct GUIDs are discovered
- `JoJot/Services/VirtualDesktopService.cs` -- No changes needed (correctly passes through whatever name it receives)
- `JoJot/MainWindow.xaml.cs` -- No changes needed (display logic is correct, problem is upstream data)
