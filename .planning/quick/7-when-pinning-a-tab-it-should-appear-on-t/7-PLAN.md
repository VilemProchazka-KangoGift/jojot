---
phase: quick-7
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/ViewModels/MainWindowViewModel.cs
  - JoJot.Tests/ViewModels/TabCrudTests.cs
autonomous: true
requirements: [QUICK-7]

must_haves:
  truths:
    - "When pinning a tab, it appears at the TOP of the pinned group (first pinned tab)"
    - "When unpinning a tab, it appears at the TOP of the unpinned group (first unpinned tab)"
    - "Existing pinned tab ordering is preserved (they shift down by one position)"
    - "Existing unpinned tab ordering is preserved when a tab is pinned"
  artifacts:
    - path: "JoJot/ViewModels/MainWindowViewModel.cs"
      provides: "ReorderAfterPinToggle with justToggled parameter"
      contains: "ReorderAfterPinToggle"
    - path: "JoJot.Tests/ViewModels/TabCrudTests.cs"
      provides: "Tests for pin-to-top and unpin-to-top behavior"
  key_links:
    - from: "JoJot/Views/MainWindow.Tabs.cs"
      to: "MainWindowViewModel.ReorderAfterPinToggle"
      via: "ViewModel.ReorderAfterPinToggle(tab)"
      pattern: "ReorderAfterPinToggle"
---

<objective>
When a user pins a tab, it should appear at the TOP of the pinned tabs group (position 0), not at the bottom. Currently, `ReorderAfterPinToggle` sorts pinned tabs by their original `SortOrder`, which places a newly-pinned tab at the bottom of the pinned group since it had a high sort order from the unpinned zone.

Purpose: More intuitive pinning UX -- the tab you just pinned should be the most prominent pinned tab, not buried under existing pins.
Output: Modified `ReorderAfterPinToggle` that accepts the just-toggled tab and places it at the top of its new group.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@JoJot/ViewModels/MainWindowViewModel.cs
@JoJot/Views/MainWindow.Tabs.cs
@JoJot.Tests/ViewModels/TabCrudTests.cs
</context>

<interfaces>
<!-- Key types and contracts the executor needs -->

From JoJot/ViewModels/MainWindowViewModel.cs (line 238):
```csharp
internal void ReorderAfterPinToggle()
{
    var sorted = Tabs.OrderByDescending(t => t.Pinned).ThenBy(t => t.SortOrder).ToList();
    Tabs.Clear();
    foreach (var t in sorted) Tabs.Add(t);

    for (int i = 0; i < Tabs.Count; i++)
        Tabs[i].SortOrder = i;
}
```

From JoJot/Views/MainWindow.Tabs.cs (line 581-591):
```csharp
private async Task TogglePinAsync(NoteTab tab)
{
    tab.Pinned = !tab.Pinned;
    await NoteStore.UpdateNotePinnedAsync(tab.Id, tab.Pinned);

    ViewModel.ReorderAfterPinToggle();
    await NoteStore.UpdateNoteSortOrdersAsync(_tabs.Select(t => (t.Id, t.SortOrder)));

    RebuildTabList();
    UpdateToolbarState();
}
```
</interfaces>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Make ReorderAfterPinToggle place the just-toggled tab at the top of its group</name>
  <files>JoJot/ViewModels/MainWindowViewModel.cs, JoJot/Views/MainWindow.Tabs.cs, JoJot.Tests/ViewModels/TabCrudTests.cs</files>
  <behavior>
    - Test: Pinning a tab with existing pinned tabs places the newly-pinned tab at position 0 (top of pinned group)
    - Test: Pinning a tab when no other pinned tabs exist places it at position 0
    - Test: Unpinning a tab places it at the top of the unpinned group (first position after all pinned tabs)
    - Test: Existing pinned tabs maintain their relative order after a new tab is pinned (they shift down by 1)
    - Test: Existing unpinned tabs maintain their relative order after a tab is pinned
    - Test: Calling ReorderAfterPinToggle without a justToggled argument (null) preserves the existing sort-by-SortOrder behavior (backward compat)
  </behavior>
  <action>
1. Add an optional `NoteTab? justToggled = null` parameter to `ReorderAfterPinToggle` in `MainWindowViewModel.cs`.

2. Change the sort logic: when `justToggled` is not null, place it first in its group (pinned or unpinned), then sort the remaining tabs in that group by their existing SortOrder. The other group sorts normally by SortOrder. When `justToggled` is null, keep the existing behavior (sort both groups by SortOrder).

   Implementation approach:
   ```csharp
   internal void ReorderAfterPinToggle(NoteTab? justToggled = null)
   {
       var pinned = Tabs.Where(t => t.Pinned).ToList();
       var unpinned = Tabs.Where(t => !t.Pinned).ToList();

       if (justToggled is not null)
       {
           if (justToggled.Pinned)
           {
               pinned.Remove(justToggled);
               pinned.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
               pinned.Insert(0, justToggled);
               unpinned.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
           }
           else
           {
               unpinned.Remove(justToggled);
               unpinned.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
               unpinned.Insert(0, justToggled);
               pinned.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
           }
       }
       else
       {
           pinned.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
           unpinned.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
       }

       Tabs.Clear();
       foreach (var t in pinned) Tabs.Add(t);
       foreach (var t in unpinned) Tabs.Add(t);

       for (int i = 0; i < Tabs.Count; i++)
           Tabs[i].SortOrder = i;
   }
   ```

3. Update the call site in `MainWindow.Tabs.cs` `TogglePinAsync` to pass the tab:
   Change `ViewModel.ReorderAfterPinToggle();` to `ViewModel.ReorderAfterPinToggle(tab);`

4. Update existing tests that assert specific ordering to account for the null-argument backward-compat path (they should still pass since they don't pass a justToggled argument).

5. Add new tests in `TabCrudTests.cs` for the justToggled behavior.
  </action>
  <verify>
    <automated>dotnet test JoJot.Tests/JoJot.Tests.csproj --filter "FullyQualifiedName~ReorderAfterPinToggle" --no-build</automated>
  </verify>
  <done>
    - ReorderAfterPinToggle(tab) places the just-pinned tab at position 0 in the pinned group
    - ReorderAfterPinToggle(tab) places the just-unpinned tab at position 0 in the unpinned group (right after all pinned tabs)
    - ReorderAfterPinToggle() without argument preserves existing sort-by-SortOrder behavior
    - All existing tests pass
    - All new tests pass
  </done>
</task>

</tasks>

<verification>
dotnet test JoJot.Tests/JoJot.Tests.csproj
dotnet build JoJot/JoJot.slnx
</verification>

<success_criteria>
- Pinning a tab places it at the top of the pinned group
- Unpinning a tab places it at the top of the unpinned group
- All existing tests continue to pass (backward compatibility via null default)
- New tests cover pin-to-top and unpin-to-top scenarios
</success_criteria>

<output>
After completion, create `.planning/quick/7-when-pinning-a-tab-it-should-appear-on-t/7-SUMMARY.md`
</output>
