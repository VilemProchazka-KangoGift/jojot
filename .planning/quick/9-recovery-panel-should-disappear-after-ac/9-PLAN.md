---
phase: quick-09
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Views/MainWindow.Recovery.cs
  - JoJot.Tests/Views/RecoveryPanelBroadcastTests.cs
autonomous: true
requirements: [QUICK-09]
must_haves:
  truths:
    - "After adopting/deleting an orphaned session on one window, ALL other JoJot windows hide their recovery panel"
    - "After adopting/deleting an orphaned session, the blue orphan badge disappears on ALL JoJot windows"
    - "If orphans still remain after a partial action, other windows refresh their badge and recovery panel content correctly"
  artifacts:
    - path: "JoJot/Views/MainWindow.Recovery.cs"
      provides: "Cross-window orphan action broadcast"
  key_links:
    - from: "MainWindow.Recovery.cs RefreshAfterOrphanAction"
      to: "App.GetAllWindows()"
      via: "iterate all windows, call UpdateOrphanBadge + HideRecoveryPanel on each other window"
      pattern: "GetAllWindows.*UpdateOrphanBadge"
---

<objective>
Fix recovery panel not disappearing across all JoJot windows after acting on orphaned sessions.

Purpose: When a user adopts or deletes orphaned sessions from the recovery panel, the orphan badge (blue dot on hamburger menu) and the recovery panel itself only update on the current window. Other JoJot windows (on other virtual desktops) still show the stale badge and open recovery panel. This is because `RefreshAfterOrphanAction()` only calls `UpdateOrphanBadge()` and `HideRecoveryPanel()` on `this` — it never broadcasts to sibling windows.

Output: Modified `RefreshAfterOrphanAction()` that broadcasts badge/panel updates to all windows.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@JoJot/Views/MainWindow.Recovery.cs
@JoJot/Views/MainWindow.HamburgerMenu.cs
@JoJot/App.xaml.cs
</context>

<interfaces>
<!-- Key types and contracts the executor needs -->

From JoJot/App.xaml.cs:
```csharp
// App._windows is Dictionary<string, MainWindow> keyed by desktop GUID
public List<MainWindow> GetAllWindows() => [.. _windows.Values];
```

From JoJot/Views/MainWindow.Recovery.cs:
```csharp
public void UpdateOrphanBadge()  // Updates badge dot + menu item visibility based on OrphanedSessionGuids
private void HideRecoveryPanel()  // Hides panel with animation, sets _recoveryPanelOpen = false
private async Task RefreshAfterOrphanAction()  // THE METHOD TO FIX — only updates current window
```

From JoJot/Views/MainWindow.xaml.cs:
```csharp
private bool _recoveryPanelOpen  // Forwarding property to ViewModel.IsRecoveryOpen
```
</interfaces>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Broadcast orphan action to all windows</name>
  <files>JoJot/Views/MainWindow.Recovery.cs, JoJot.Tests/Views/RecoveryPanelBroadcastTests.cs</files>
  <behavior>
    - Test 1: BroadcastOrphanUpdate calls UpdateOrphanBadge on all windows from GetAllWindows (verify via mock/spy pattern if feasible, otherwise test the extraction logic)
    - Test 2: BroadcastOrphanUpdate calls HideRecoveryPanel on other windows that have _recoveryPanelOpen == true
    - Test 3: BroadcastOrphanUpdate does NOT hide recovery panel on the current window (caller handles its own panel state separately in RefreshAfterOrphanAction)
  </behavior>
  <action>
Modify `RefreshAfterOrphanAction()` in `MainWindow.Recovery.cs` to broadcast orphan state changes to all other JoJot windows after acting on an orphan.

Specifically, after the existing `UpdateOrphanBadge()` call on `this`, add a broadcast to all sibling windows:

```csharp
// Broadcast to all other windows
if (Application.Current is App app)
{
    foreach (var window in app.GetAllWindows())
    {
        if (window == this) continue;
        window.UpdateOrphanBadge();
        window.HideRecoveryPanel();
    }
}
```

The key points:
1. Skip `this` window — it already handles its own state (badge updated above, panel refreshed or hidden below)
2. Always call `UpdateOrphanBadge()` on other windows — this reads from the shared `VirtualDesktopService.OrphanedSessionGuids` which has already been updated by `RemoveOrphanGuid`
3. Always call `HideRecoveryPanel()` on other windows — `HideRecoveryPanel` already has a guard `if (!_recoveryPanelOpen) return;` so calling it unconditionally is safe. Other windows' recovery panels show stale data (the orphan that was just acted on), so closing them is the correct behavior. If the user wants to see remaining orphans on another window, they can reopen the panel which will fetch fresh data.
4. `HideRecoveryPanel()` must be made `public` (currently `private`) so other windows can call it. Alternatively, create a new `public` method `NotifyOrphansChanged()` that calls both `UpdateOrphanBadge()` and `HideRecoveryPanel()` — this is cleaner since it provides a single public API for cross-window notification. Prefer the `NotifyOrphansChanged()` approach.

Recommended approach — add a new public method:

```csharp
/// <summary>
/// Called by other windows after an orphan recovery action.
/// Updates the badge and closes the recovery panel (which would show stale data).
/// </summary>
public void NotifyOrphansChanged()
{
    UpdateOrphanBadge();
    HideRecoveryPanel();
}
```

Then in `RefreshAfterOrphanAction()`, after the existing `UpdateOrphanBadge()` line, add:

```csharp
// Notify all other windows to update their badge and close stale recovery panels
if (Application.Current is App app)
{
    foreach (var window in app.GetAllWindows())
    {
        if (window == this) continue;
        window.NotifyOrphansChanged();
    }
}
```

For the test file: Since `HideRecoveryPanel` requires WPF UI elements (animations on the RecoveryPanel UserControl), direct unit testing of the broadcast is not feasible without a running WPF app. Instead, write tests that verify the `NotifyOrphansChanged` method correctly delegates — test that `UpdateOrphanBadge` behavior is correct given changed `OrphanedSessionGuids` state (this is already tested). The primary verification is the build + manual test.

If creating a test is not feasible due to WPF UI thread requirements, skip the test file and rely on build verification + manual testing.
  </action>
  <verify>
    <automated>dotnet build JoJot/JoJot.slnx && dotnet test JoJot.Tests/JoJot.Tests.csproj --no-build</automated>
  </verify>
  <done>
    - `RefreshAfterOrphanAction()` broadcasts to all sibling windows via `NotifyOrphansChanged()`
    - `NotifyOrphansChanged()` is public and calls `UpdateOrphanBadge()` + `HideRecoveryPanel()`
    - Build succeeds with no errors
    - All existing tests pass
  </done>
</task>

</tasks>

<verification>
1. `dotnet build JoJot/JoJot.slnx` — compiles without errors
2. `dotnet test JoJot.Tests/JoJot.Tests.csproj` — all existing tests pass
3. Manual verification: Open JoJot with orphaned sessions present. Open recovery panel on two windows. Act on an orphan (Adopt or Delete) on one window. The other window's recovery panel should close and its blue badge should update.
</verification>

<success_criteria>
- Acting on orphans in one window causes ALL other windows to hide the recovery panel and update the orphan badge
- No regressions in existing tests
- Clean build
</success_criteria>

<output>
After completion, create `.planning/quick/9-recovery-panel-should-disappear-after-ac/9-SUMMARY.md`
</output>
