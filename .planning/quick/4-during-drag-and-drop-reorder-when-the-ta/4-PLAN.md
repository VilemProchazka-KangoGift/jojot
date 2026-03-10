---
phase: quick-4
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Views/MainWindow.TabDrag.cs
autonomous: true
requirements: [QUICK-4]

must_haves:
  truths:
    - "When a tab is dropped in a new position during drag-reorder, it fades in from 50% to 100% opacity over ~200ms"
    - "When a tab is dropped in its original position (no-move), no animation fires and opacity resets immediately"
  artifacts:
    - path: "JoJot/Views/MainWindow.TabDrag.cs"
      provides: "Fade-in animation after successful drag-reorder drop"
      contains: "DoubleAnimation"
  key_links:
    - from: "CompleteDrag"
      to: "DoubleAnimation on OuterBorder.Opacity"
      via: "Dispatcher.InvokeAsync deferred to DispatcherPriority.Loaded"
      pattern: "Dispatcher.*InvokeAsync|BeginInvoke"
---

<objective>
Fix the drag-and-drop reorder fade-in animation so it actually fires when a tab is dropped at a new position.

Purpose: The fade-in animation code (0.5 -> 1.0 opacity, 200ms, CubicEase) already exists in CompleteDrag() at lines 234-258 of MainWindow.TabDrag.cs, but it silently fails. After RebuildTabList() recreates all ListBoxItems, the visual tree for the new items is not yet realized, so FindNamedDescendant returns null and the animation never runs. The fix is to defer the animation lookup until after WPF has rendered the new items.

Output: Working fade-in animation on tab drop during reorder.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@.planning/quick/3-the-drag-and-drop-reorder-feature-regres/3-SUMMARY.md
@JoJot/Views/MainWindow.TabDrag.cs
@JoJot/Views/MainWindow.Tabs.cs

<interfaces>
From JoJot/Views/MainWindow.xaml.cs:
```csharp
// Visual tree helper used to find named elements inside DataTemplate instances
private static T? FindNamedDescendant<T>(DependencyObject parent, string name) where T : FrameworkElement
```

From JoJot/Views/MainWindow.TabDrag.cs (CompleteDrag, lines 202-268):
```csharp
// After successful MoveTab:
RebuildTabList();           // Clears and recreates all ListBoxItems
SelectTabByNote(_dragTab);  // Selects the moved tab
// Then tries to find OuterBorder on new items -- FAILS because visual tree not ready
```
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Defer fade-in animation until visual tree is ready</name>
  <files>JoJot/Views/MainWindow.TabDrag.cs</files>
  <action>
In the CompleteDrag() method, the fade-in animation block (lines 234-258) runs immediately after RebuildTabList(). At this point, the newly created ListBoxItems have not had their ContentTemplate applied yet, so FindNamedDescendant returns null and the animation silently doesn't fire.

Fix by wrapping the fade-in animation block in a Dispatcher.InvokeAsync call at DispatcherPriority.Loaded. This defers the animation lookup until after WPF has measured, arranged, and rendered the new items, ensuring the visual tree is fully built.

Specifically, replace the existing block (from the "Fade-in the moved tab" comment through the closing brace) with:

```csharp
// Fade-in the moved tab at its new position
// Deferred to Loaded priority so the visual tree from RebuildTabList is fully realized
var movedTab = _dragTab;
Dispatcher.InvokeAsync(() =>
{
    foreach (var obj in TabList.Items)
    {
        if (obj is ListBoxItem item && item.Tag == movedTab)
        {
            var content = FindNamedDescendant<Border>(item, "OuterBorder");
            if (content is not null)
            {
                content.Opacity = 0.5;
                var fadeIn = new DoubleAnimation
                {
                    From = 0.5, To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                fadeIn.Completed += (_, _) => { content.Opacity = 1.0; content.BeginAnimation(OpacityProperty, null); };
                content.BeginAnimation(OpacityProperty, fadeIn);
            }
            break;
        }
    }
}, System.Windows.Threading.DispatcherPriority.Loaded);
```

Key details:
- Capture `_dragTab` into local `movedTab` BEFORE ResetDragState() clears it (the Dispatcher callback runs later)
- Use `System.Windows.Threading.DispatcherPriority.Loaded` -- this fires after layout and render passes complete
- Keep the animation parameters identical: 0.5->1.0, 200ms, CubicEase EaseOut
- Keep the Completed handler that clears the animation clock to avoid holding references
- Add `using System.Windows.Threading;` at the top if not already present (check first)

Also add a `using System.Windows.Threading;` import if not already present at the top of the file.
  </action>
  <verify>
    <automated>cd P:/projects/JoJot && dotnet build JoJot/JoJot.slnx --no-restore 2>&1 | tail -5</automated>
  </verify>
  <done>CompleteDrag defers the fade-in animation via Dispatcher.InvokeAsync at Loaded priority. Build succeeds with 0 errors. The animation will now fire after RebuildTabList's visual tree is fully realized.</done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <what-built>Fade-in animation on tab drop during drag-reorder</what-built>
  <how-to-verify>
    1. Run the app: `dotnet run --project JoJot/JoJot.csproj`
    2. Create 3+ tabs
    3. Drag a tab to a different position in the list
    4. When you release the mouse, the tab should visibly fade in from 50% opacity to 100% over ~200ms at its new position
    5. Verify the animation is smooth (no flicker or flash of full opacity before fade starts)
    6. Drag a tab but drop it in its original position -- it should snap back to full opacity immediately (no animation)
  </how-to-verify>
  <resume-signal>Type "approved" or describe issues</resume-signal>
</task>

</tasks>

<verification>
- Build succeeds: `dotnet build JoJot/JoJot.slnx` completes with 0 errors
- Tests pass: `dotnet test JoJot.Tests/JoJot.Tests.csproj` -- all 302+ tests pass (no test changes needed, this is purely visual)
- Visual: tab fade-in animation visible on drop
</verification>

<success_criteria>
When a tab is dropped at a new position during drag-reorder, it visibly fades in from 50% to 100% opacity. The animation is smooth with no flash of full opacity before the fade begins.
</success_criteria>

<output>
After completion, create `.planning/quick/4-during-drag-and-drop-reorder-when-the-ta/4-SUMMARY.md`
</output>
