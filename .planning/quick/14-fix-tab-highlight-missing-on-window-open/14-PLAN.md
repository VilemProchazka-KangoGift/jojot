---
phase: quick-14
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Views/MainWindow.Tabs.cs
autonomous: true
requirements: [FIX-HIGHLIGHT]

must_haves:
  truths:
    - "When the window opens with existing tabs, the first tab has a visible selected background highlight"
    - "After pinning or unpinning a tab, the active tab retains its selected background highlight"
    - "After any RebuildTabList call, the re-selected active tab always shows its highlight"
  artifacts:
    - path: "JoJot/Views/MainWindow.Tabs.cs"
      provides: "Deferred highlight application after DataTemplate rendering"
      contains: "ApplyActiveHighlight"
  key_links:
    - from: "TabList_SelectionChanged"
      to: "ApplyActiveHighlight"
      via: "Dispatcher.BeginInvoke at DispatcherPriority.Loaded for deferred template readiness"
      pattern: "DispatcherPriority\\.Loaded"
---

<objective>
Fix tab highlight not appearing on window open and after pin toggle.

Purpose: The selected tab must always show its highlight background (`c-selected-bg`) whenever the editor is displaying content. Currently, two scenarios lose the highlight:
1. Window opens and selects the first tab -- DataTemplate visual tree is not yet applied, so `FindNamedDescendant<Border>(item, "OuterBorder")` returns null and `ApplyActiveHighlight` silently exits.
2. Pinning/unpinning a tab calls `RebuildTabList()` which creates entirely new ListBoxItems, then `SelectTabByNote` fires `TabList_SelectionChanged` which calls `ApplyActiveHighlight` -- but again, the template may not be rendered yet for the new item.

Output: Updated `MainWindow.Tabs.cs` with deferred highlight application.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@JoJot/Views/MainWindow.Tabs.cs
@JoJot/Views/MainWindow.xaml.cs
@JoJot/Views/MainWindow.xaml

<interfaces>
<!-- Key method: ApplyActiveHighlight (line 414) -->
```csharp
private void ApplyActiveHighlight(ListBoxItem item)
{
    var outerBorder = FindNamedDescendant<Border>(item, "OuterBorder");
    if (outerBorder is null) return; // BUG: exits when template not yet applied
    outerBorder.Background = GetBrush("c-selected-bg");
    // Also shows pin/close buttons
}
```

<!-- Key flow: TabList_SelectionChanged (line 294) calls ApplyActiveHighlight -->
<!-- Key flow: RebuildTabList (line 54) ends with SelectTabByNote which triggers SelectionChanged -->
<!-- Key flow: LoadTabsAsync (line 21) calls RebuildTabList then TabList.SelectedIndex = 0 -->
<!-- Key flow: TogglePinAsync (line 581) calls RebuildTabList -->

<!-- Existing pattern for deferred operations (from MainWindow.Tabs.cs line 373-377): -->
```csharp
_ = ContentEditor.Dispatcher.BeginInvoke(() =>
{
    var sv = GetScrollViewer(ContentEditor);
    sv?.ScrollToVerticalOffset(tab.EditorScrollOffset);
}, System.Windows.Threading.DispatcherPriority.Loaded);
```
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Defer ApplyActiveHighlight when DataTemplate visual tree is not ready</name>
  <files>JoJot/Views/MainWindow.Tabs.cs</files>
  <action>
Modify `ApplyActiveHighlight` to handle the case where `FindNamedDescendant<Border>(item, "OuterBorder")` returns null because the DataTemplate hasn't been applied yet. When null, defer the entire highlight application to `DispatcherPriority.Loaded` so the visual tree is ready.

Specifically, change `ApplyActiveHighlight` from:

```csharp
private void ApplyActiveHighlight(ListBoxItem item)
{
    var outerBorder = FindNamedDescendant<Border>(item, "OuterBorder");
    if (outerBorder is null) return;
    outerBorder.Background = GetBrush("c-selected-bg");
    var pinBtn = FindNamedDescendant<Border>(item, "PinBtn");
    var closeBtn = FindNamedDescendant<Border>(item, "CloseBtn");
    if (pinBtn is not null) { pinBtn.Visibility = Visibility.Visible; pinBtn.Opacity = 1; }
    if (closeBtn is not null) { closeBtn.Visibility = Visibility.Visible; closeBtn.Opacity = 1; }
}
```

To:

```csharp
private void ApplyActiveHighlight(ListBoxItem item)
{
    var outerBorder = FindNamedDescendant<Border>(item, "OuterBorder");
    if (outerBorder is null)
    {
        // DataTemplate not yet applied — defer until layout pass completes
        Dispatcher.BeginInvoke(() => ApplyActiveHighlight(item),
            System.Windows.Threading.DispatcherPriority.Loaded);
        return;
    }

    outerBorder.Background = GetBrush("c-selected-bg");

    var pinBtn = FindNamedDescendant<Border>(item, "PinBtn");
    var closeBtn = FindNamedDescendant<Border>(item, "CloseBtn");

    if (pinBtn is not null)
    {
        pinBtn.Visibility = Visibility.Visible;
        pinBtn.Opacity = 1;
    }
    if (closeBtn is not null)
    {
        closeBtn.Visibility = Visibility.Visible;
        closeBtn.Opacity = 1;
    }
}
```

This uses `DispatcherPriority.Loaded` which is the same priority used elsewhere in this file (line 377 for scroll restore, line 459 for ScrollIntoView). The deferred call only happens when the template is not yet ready (the null check), so it does not change behavior for the normal case where the template is already applied (e.g., clicking an existing tab).

Guard against infinite recursion: If after deferral the template is still null (should not happen but defensive), the method will defer once more. WPF guarantees templates are applied after the Loaded pass, so this should resolve in a single deferral. However, to be safe against pathological cases, add a boolean guard parameter:

```csharp
private void ApplyActiveHighlight(ListBoxItem item, bool isDeferred = false)
{
    var outerBorder = FindNamedDescendant<Border>(item, "OuterBorder");
    if (outerBorder is null)
    {
        if (!isDeferred)
        {
            // DataTemplate not yet applied — defer until layout pass completes
            Dispatcher.BeginInvoke(() => ApplyActiveHighlight(item, isDeferred: true),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        return;
    }

    outerBorder.Background = GetBrush("c-selected-bg");

    var pinBtn = FindNamedDescendant<Border>(item, "PinBtn");
    var closeBtn = FindNamedDescendant<Border>(item, "CloseBtn");

    if (pinBtn is not null)
    {
        pinBtn.Visibility = Visibility.Visible;
        pinBtn.Opacity = 1;
    }
    if (closeBtn is not null)
    {
        closeBtn.Visibility = Visibility.Visible;
        closeBtn.Opacity = 1;
    }
}
```

Also verify that the single call site `ApplyActiveHighlight(newItem)` in `TabList_SelectionChanged` (line 358) does NOT need changes -- it passes `isDeferred: false` by default, which is correct.

Do NOT change any other methods. The fix is entirely within `ApplyActiveHighlight`.
  </action>
  <verify>
    <automated>cd P:/projects/JoJot && dotnet build JoJot/JoJot.slnx --no-restore 2>&1 | tail -5</automated>
  </verify>
  <done>
    - `ApplyActiveHighlight` defers to `DispatcherPriority.Loaded` when the DataTemplate visual tree is not yet available
    - Build succeeds with no errors or warnings in modified file
    - The fix covers both scenarios: initial window load (LoadTabsAsync -> RebuildTabList -> SelectedIndex=0) and pin toggle (TogglePinAsync -> RebuildTabList -> SelectTabByNote)
  </done>
</task>

</tasks>

<verification>
1. `dotnet build JoJot/JoJot.slnx` succeeds
2. `dotnet test JoJot.Tests/JoJot.Tests.csproj` passes (no regressions)
3. Manual verification: Launch app, observe first tab is highlighted on open
4. Manual verification: Pin a tab, observe active tab retains highlight
5. Manual verification: Unpin a tab, observe active tab retains highlight
</verification>

<success_criteria>
- Selected tab always shows `c-selected-bg` background highlight when editor has content
- No visual regressions in tab hover, pin/unpin, close button behavior
- Build and all tests pass
</success_criteria>

<output>
After completion, create `.planning/quick/14-fix-tab-highlight-missing-on-window-open/14-SUMMARY.md`
</output>
