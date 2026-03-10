---
phase: quick-3
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Views/MainWindow.TabDrag.cs
  - JoJot/Views/MainWindow.xaml.cs
autonomous: true
requirements: [QUICK-3]
must_haves:
  truths:
    - "Dragged tab fades to 50% opacity when drag starts"
    - "Accent-colored horizontal line appears at the drop target position during drag"
    - "Drop indicator line disappears after drop completes"
    - "Moved tab fades from 50% to 100% opacity at its new position after drop"
    - "Tab reorder still works (functional behavior unchanged)"
  artifacts:
    - path: "JoJot/Views/MainWindow.TabDrag.cs"
      provides: "Visual indicator logic using FindNamedDescendant instead of Content type checks"
    - path: "JoJot/Views/MainWindow.xaml.cs"
      provides: "LostMouseCapture abort handler using FindNamedDescendant"
  key_links:
    - from: "MainWindow.TabDrag.cs"
      to: "OuterBorder in TabItemTemplate"
      via: "FindNamedDescendant<Border>(item, \"OuterBorder\")"
      pattern: "FindNamedDescendant.*OuterBorder"
---

<objective>
Fix drag-and-drop reorder visual indicators that regressed after the MVVM Phase 8 DataTemplate migration.

Purpose: The MVVM migration changed ListBoxItem.Content from a directly-created Border to a NoteTab model object with a ContentTemplate. All visual indicator code checks `item.Content is Border` or `item.Content is FrameworkElement`, which now always fails because Content is a NoteTab (not a visual element). The drag reorder still works functionally but all visual feedback is gone: no opacity fade on the dragged tab, no accent drop indicator line, no fade-in animation on completion.

Output: Restored visual indicators using FindNamedDescendant to locate the rendered OuterBorder within the DataTemplate visual tree, instead of relying on Content type checks.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@JoJot/Views/MainWindow.TabDrag.cs
@JoJot/Views/MainWindow.xaml.cs
@JoJot/Views/MainWindow.Tabs.cs
@JoJot/Views/MainWindow.xaml (TabItemTemplate DataTemplate at line 35)
</context>

<tasks>

<task type="auto">
  <name>Task 1: Fix all Content type checks to use FindNamedDescendant for visual tree access</name>
  <files>JoJot/Views/MainWindow.TabDrag.cs, JoJot/Views/MainWindow.xaml.cs</files>
  <action>
The root cause: `CreateTabListItem` sets `Content = tab` (a NoteTab model) with a `ContentTemplate`. WPF renders the DataTemplate visually but `ListBoxItem.Content` remains the NoteTab object. All `item.Content is Border` and `item.Content is FrameworkElement` checks fail silently.

Fix every location that tries to access the visual Border through Content, replacing with `FindNamedDescendant<Border>(item, "OuterBorder")`:

**In MainWindow.TabDrag.cs:**

1. `StartDrag()` (around line 45): Replace `if (_dragItem.Content is FrameworkElement dragContent)` with:
   ```csharp
   var dragBorder = FindNamedDescendant<Border>(_dragItem, "OuterBorder");
   if (dragBorder is not null)
   {
       dragBorder.Opacity = 0.5;
   }
   ```

2. `UpdateDropIndicator()` (around line 131-179): Replace ALL `Content is Border` checks. The method searches TabList.Items for ListBoxItems and checks their Content. Replace the pattern throughout:
   - Line 134: `targetItem.Content is Border targetBorder` -> use `FindNamedDescendant<Border>(targetItem, "OuterBorder")` and assign to `targetBorder`
   - Line 145: `nextItem.Content is Border nextBorder` -> same pattern with `FindNamedDescendant`
   - Line 159: `prevItem.Content is Border prevBorder` -> same pattern with `FindNamedDescendant`
   - Line 173: `lastItem.Content is Border lastBorder` -> same pattern with `FindNamedDescendant`

3. `CompleteDrag()` (around line 197): Replace `if (_dragItem?.Content is FrameworkElement oldContent) oldContent.Opacity = 1.0;` with:
   ```csharp
   if (_dragItem is not null)
   {
       var oldBorder = FindNamedDescendant<Border>(_dragItem, "OuterBorder");
       if (oldBorder is not null) oldBorder.Opacity = 1.0;
   }
   ```

4. `CompleteDrag()` fade-in animation (around line 218-230): Replace `item.Content is FrameworkElement content` with:
   ```csharp
   var content = FindNamedDescendant<Border>(item, "OuterBorder");
   if (content is not null)
   ```
   Keep the rest of the animation code identical (it operates on the element's Opacity/BeginAnimation).

5. `ResetDragState()` (around line 276): Replace `if (_dragItem?.Content is FrameworkElement resetContent) resetContent.Opacity = 1.0;` with:
   ```csharp
   if (_dragItem is not null)
   {
       var resetBorder = FindNamedDescendant<Border>(_dragItem, "OuterBorder");
       if (resetBorder is not null) resetBorder.Opacity = 1.0;
   }
   ```

**In MainWindow.xaml.cs (LostMouseCapture handler, around line 245):**

6. Replace `if (_dragItem?.Content is FrameworkElement abortContent) abortContent.Opacity = 1.0;` with:
   ```csharp
   if (_dragItem is not null)
   {
       var abortBorder = FindNamedDescendant<Border>(_dragItem, "OuterBorder");
       if (abortBorder is not null) abortBorder.Opacity = 1.0;
   }
   ```

IMPORTANT: `FindNamedDescendant` is a static method defined in MainWindow.xaml.cs (line 336). It is accessible from both partial class files without qualification since they share the same class.

Do NOT change any functional drag logic (mouse capture, insert index calculation, collection reorder, sort order persistence). Only fix the visual tree access pattern.
  </action>
  <verify>
    <automated>cd P:/projects/JoJot && dotnet build JoJot/JoJot.slnx --no-restore 2>&1 | tail -5</automated>
  </verify>
  <done>All 6 Content type check sites replaced with FindNamedDescendant calls. Build succeeds with no errors. No functional drag logic changed.</done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <what-built>Restored drag-and-drop reorder visual indicators: opacity fade on dragged tab, accent drop indicator line at target position, fade-in animation on completion.</what-built>
  <how-to-verify>
    1. Run the app: `dotnet run --project JoJot/JoJot.csproj`
    2. Create at least 3 tabs with some text content
    3. Start dragging a tab by clicking and holding on it, then move the mouse vertically
    4. Verify: The dragged tab should fade to ~50% opacity
    5. While dragging, move between tabs — verify: An accent-colored horizontal line should appear at the drop target position
    6. The indicator should move as you drag between different positions
    7. The indicator should NOT appear when hovering near the tab's original position (no-op suppression)
    8. Drop the tab at a new position — verify: The tab appears at the new position and fades from 50% to full opacity
    9. Test with pinned tabs: pin a tab, verify drag only shows indicators within the pinned zone (not between pinned/unpinned)
  </how-to-verify>
  <resume-signal>Type "approved" or describe issues</resume-signal>
</task>

</tasks>

<verification>
- `dotnet build JoJot/JoJot.slnx` succeeds
- `dotnet test JoJot.Tests/JoJot.Tests.csproj` passes (302 tests, no regressions)
- Visual manual check confirms all 3 visual indicators restored (opacity fade, drop line, fade-in)
</verification>

<success_criteria>
- Dragged tab visually fades to 50% opacity when drag threshold is exceeded
- Accent-colored horizontal line appears at the nearest valid drop position
- Drop indicator suppressed at no-op positions (original index, original+1)
- After drop, tab fades in from 50% to 100% at its new position
- All existing drag reorder functionality unchanged (zone enforcement, sort persistence)
- Build and all 302 tests pass
</success_criteria>

<output>
After completion, create `.planning/quick/3-the-drag-and-drop-reorder-feature-regres/3-SUMMARY.md`
</output>
