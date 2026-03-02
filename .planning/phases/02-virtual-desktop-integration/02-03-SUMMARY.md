---
phase: 02-virtual-desktop-integration
plan: "03"
subsystem: interop
tags: [com-notification, window-title, em-dash, desktop-rename, live-update]

# Dependency graph
requires:
  - phase: 02-virtual-desktop-integration
    provides: "VirtualDesktopInterop, IVirtualDesktopNotification, IVirtualDesktopNotificationService"
provides:
  - VirtualDesktopNotificationListener: COM callback for rename, switch, create, destroy events
  - VirtualDesktopService events: DesktopRenamed, CurrentDesktopChanged
  - VirtualDesktopService.SubscribeNotifications/UnsubscribeNotifications lifecycle
  - MainWindow.UpdateDesktopTitle with em-dash format
affects: [03-notes, 06-multi-window]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "COM callback pattern: [ComVisible(true)] class implementing COM interface"
    - "Event bridging: COM callbacks fire internal events -> fire public string-based events"
    - "Fire-and-forget database update from COM callback with error logging"
    - "Dispatcher.InvokeAsync for UI thread marshaling from COM callback events"
    - "Window title format: em-dash U+2014 with spaces"

key-files:
  created:
    - JoJot/Interop/VirtualDesktopNotificationListener.cs
  modified:
    - JoJot/Services/VirtualDesktopService.cs
    - JoJot/MainWindow.xaml.cs
    - JoJot/App.xaml.cs

key-decisions:
  - "COM callbacks use the existing IVirtualDesktopNotification interface from ComInterfaces.cs"
  - "Database update on rename is fire-and-forget (Task.Run) to avoid blocking COM callback"
  - "Title format uses Unicode escape \\u2014 for em-dash to ensure correct encoding"
  - "CurrentDesktopChanged event logged only — Phase 3 will handle multi-window visibility"

patterns-established:
  - "Notification lifecycle: subscribe on startup (after COM init), unsubscribe before COM dispose"
  - "No COM types in public events: DesktopRenamed(string, string) not (Guid, string)"
  - "Title fallback chain: name > 'Desktop N' > plain 'JoJot'"

requirements-completed: [VDSK-06, VDSK-07]

# Metrics
duration: ~10min
completed: 2026-03-02
---

# Plan 02-03: Live Window Title and COM Notifications Summary

**COM notification subscription for instant desktop rename detection with em-dash title format and database persistence**

## Performance

- **Tasks:** 2
- **Files created:** 1
- **Files modified:** 3

## Accomplishments
- VirtualDesktopNotificationListener implementing full IVirtualDesktopNotification COM callback
- DesktopRenamed and CurrentDesktopChanged public events with string-based API (no COM types)
- Notification lifecycle: subscribe after COM init, unsubscribe before COM dispose
- Window title shows "JoJot -- {name}" / "JoJot -- Desktop N" / "JoJot" with em-dash U+2014
- Live title update on desktop rename via COM notification (no polling)
- Database persistence of desktop name on rename (fire-and-forget with error logging)

## Task Commits

1. **Task 1-2: Notification listener and title wiring** - `9d04d8e` (feat)

## Files Created/Modified
- `JoJot/Interop/VirtualDesktopNotificationListener.cs` - COM callback implementor for all desktop events
- `JoJot/Services/VirtualDesktopService.cs` - Added events, SubscribeNotifications, UnsubscribeNotifications, OnDesktopRenamed, OnCurrentDesktopChanged
- `JoJot/MainWindow.xaml.cs` - Added UpdateDesktopTitle with em-dash format
- `JoJot/App.xaml.cs` - Added Step 5.55 (notification subscription), Step 9.5 (initial title + event handlers)

## Decisions Made
- COM callbacks marshal to public events with string types to maintain COM boundary isolation
- Database update from rename callback is fire-and-forget to avoid blocking the COM callback thread
- CurrentDesktopChanged logged only; multi-window handling deferred to Phase 3

## Deviations from Plan
None - plan executed as written

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Full virtual desktop integration operational
- Title updates live, session matching runs on startup, notifications active
- Ready for note storage (Phase 3) and multi-window management (Phase 6)

---
*Phase: 02-virtual-desktop-integration*
*Completed: 2026-03-02*
