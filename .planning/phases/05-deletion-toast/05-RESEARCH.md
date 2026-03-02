# Phase 5: Deletion & Toast - Research

**Researched:** 2026-03-02
**Domain:** WPF tab deletion lifecycle, soft-delete state machine, Storyboard animation, CancellationToken timer pattern
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Toast visual design:**
- Toast spans editor area only (Grid column 2), not full window width
- Dark charcoal background (#333333) with white text
- Left-aligned text: `"tab name" deleted` (tab name in quotes/italic, truncated 30 chars per TOST-06)
- Right-aligned "Undo" link in accent blue (#2196F3) with underline
- 36px tall, full width of editor column, anchored to bottom
- Slide-up animation: translateY 100%→0, 150ms ease-out (WPF Storyboard)
- Auto-dismiss after 4 seconds with slide-down exit animation
- No countdown indicator or progress bar — clean disappearance
- New deletion while toast visible: instant content swap (no re-animation), timer resets, previous deletion becomes permanent

**Delete hover icon:**
- × (close/X) rendered as text, 12px font size
- Positioned upper-right of the tab item (per TDEL-03)
- Appears on hover with 100ms opacity fade-in (0→1)
- Shows on ALL tabs on hover, including the active (selected) tab
- Default color: muted gray (#888888)
- On hover over the × itself: red (#e74c3c), matching future toolbar delete button (TOOL-02)

**Focus rules after delete:**
- Deleting a NON-ACTIVE tab: active tab and editor content stay unchanged
- Deleting the ACTIVE tab: focus cascade per TDEL-05 — first tab below → last tab in list → auto-create new empty tab
- No empty state screen — always auto-create an empty tab when last tab is deleted (consistent with existing LoadTabsAsync pattern)
- Search stays active during delete: cascade applies within filtered results; if no filtered tabs remain, clear search then cascade on full list
- Undo restoration: restored tab becomes the active tab automatically and is selected in the tab list

**Bulk delete infrastructure:**
- Phase 5 builds the engine (DeleteMultipleAsync, multi-tab toast, multi-tab undo) but no bulk UI triggers
- Bulk triggers arrive in Phase 7 (toolbar) and Phase 8 (context menu: delete all below, delete older than, delete all except pinned)
- Bulk toast shows "N notes deleted" with single Undo button
- All-or-nothing undo: restores ALL tabs to their original positions, no selective restore

**Deletion model:**
- Soft delete: remove tab from `_tabs` collection and UI immediately, hold NoteTab object(s) in memory
- After 4 seconds (toast expires): commit hard delete via `DatabaseService.DeleteNoteAsync`
- On undo: re-insert NoteTab into `_tabs` at original sort_order position, rebuild tab list, select restored tab
- On new deletion replacing toast: immediately hard-delete the previous pending deletion, then soft-delete the new one

### Claude's Discretion
- Exact WPF Storyboard implementation for slide-up/slide-down animations
- Internal data structure for pending deletions (single field vs queue)
- Whether the toast is a UserControl or inline XAML in MainWindow
- Edge case handling for rapid successive deletes

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| TDEL-01 | Multiple delete triggers: Ctrl+W, toolbar button, tab hover icon, middle-click on tab, context menu | Ctrl+W in Window_PreviewKeyDown; middle-click via MouseButtonEventArgs.ChangedButton == Middle on ListBoxItem; hover icon added in CreateTabListItem; toolbar/context menu deferred to Phase 7/8 |
| TDEL-02 | All single-tab deletions are immediate with no confirmation dialog | Soft-delete removes from _tabs + UI instantly; no await before visual removal |
| TDEL-03 | Tab hover shows delete icon (12px, upper-right, fades in 100ms) with color change on hover | TextBlock "×" overlaid in Grid upper-right; DoubleAnimation on Opacity via Storyboard; outerBorder.MouseEnter/Leave already hooks for hover — extend to show/hide × |
| TDEL-04 | Middle-click on any tab deletes it immediately | PreviewMouseButtonDown with e.ChangedButton == MouseButton.Middle on ListBoxItem |
| TDEL-05 | Post-delete focus: first tab below → last tab in list → create new empty tab | Index math on _tabs after removal; delegate to CreateNewTabAsync for empty case |
| TDEL-06 | Pinned tabs are never deleted by bulk operations (silently skipped) | Filter on NoteTab.Pinned in DeleteMultipleAsync |
| TOST-01 | Toast appears at bottom of window on every deletion, 36px tall, full width | Border element in Grid column 2 with VerticalAlignment=Bottom; Height=36; managed Visibility |
| TOST-02 | Slides up from bottom (translateY 100%→0, 150ms ease-out), auto-dismisses after 4 seconds | WPF Storyboard with DoubleAnimation on TranslateTransform.Y; DispatcherTimer or CancellationTokenSource for 4s dismiss |
| TOST-03 | Undo button restores tab (same content, position, custom name), dismisses toast | Re-insert NoteTab at saved sort_order index; call RebuildTabList + SelectTabByNote |
| TOST-04 | New deletion while toast visible replaces toast; previous deletion becomes permanent | CommitPendingDeletion() immediately hard-deletes held NoteTab(s); then SoftDelete new; swap toast content without re-animating |
| TOST-05 | Bulk delete toast shows "N notes deleted" with single undo for all | PendingDeletion holds List<NoteTab>; toast message computed from count |
| TOST-06 | Toast styling: tab name in quotes/italic (truncated 30 chars), undo in accent color with underline | Inline TextBlock formatting with Italic Run; TextDecorations.Underline on Undo TextBlock |
</phase_requirements>

---

## Summary

Phase 5 is a pure WPF code-behind phase with no new NuGet dependencies. The work divides into three tightly coupled subsystems: (1) the deletion engine — a `DeleteTabAsync` / `DeleteMultipleAsync` pair that immediately removes tabs from the in-memory `_tabs` collection and UI, holding the evicted `NoteTab` objects in a `PendingDeletion` record; (2) the toast overlay — a XAML `Border` element in Grid column 2 animated by WPF `Storyboard` with a `CancellationTokenSource`-driven 4-second auto-dismiss; and (3) the trigger wiring — `Ctrl+W` in `Window_PreviewKeyDown`, middle-click via `PreviewMouseButtonDown` on each `ListBoxItem`, and a `×` text overlay per tab item in `CreateTabListItem`.

The key architectural tension is the **replace-not-re-animate** rule for TOST-04: when a second deletion arrives while the toast is visible, the UX contract requires instant content swap (no slide-up again) with timer reset, while committing the previous pending deletion to the database. This means the toast animation state machine must be decoupled from the data commit path. A `CancellationTokenSource` (cancel = commit previous, restart timer = new deletion) is the idiomatic .NET pattern for this; the Storyboard controls only slide-in/slide-out visuals, which fire once on first appearance and once on dismiss.

The hover `×` icon requires extending `CreateTabListItem` with a small overlay `Grid` whose opacity animates on `outerBorder.MouseEnter`. The active tab has the same hover behavior as all other tabs per the locked decisions. Middle-click detection uses `PreviewMouseButtonDown` (not `MouseDown`) to intercept before WPF selection logic runs, checking `e.ChangedButton == MouseButton.Middle`.

**Primary recommendation:** Implement as inline XAML in MainWindow (not a UserControl) and a single `PendingDeletion` record field on MainWindow — the single-replace model is simpler than a queue and matches the specified behavior exactly.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WPF (built-in) | .NET 10 | All UI, animation, event routing | Project stack; no alternatives |
| `System.Windows.Media.Animation` | .NET 10 | `Storyboard`, `DoubleAnimation` for toast slide | Built-in; no external animation lib needed |
| `System.Threading.CancellationTokenSource` | .NET 10 | 4-second toast timer with cancellable restart | Idiomatic .NET async timer; avoids `DispatcherTimer` lifecycle issues |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Windows.Threading.DispatcherTimer` | .NET 10 | Alternative timer that fires on UI thread natively | Use if CTS approach requires manual Dispatcher.Invoke; simpler if no async code needed on expiry |
| `Microsoft.Data.Sqlite` | Already in project | `DeleteNoteAsync` for hard delete on commit | Already wired; no new methods needed for single delete |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Inline XAML toast in MainWindow | Separate UserControl | UserControl adds a file; inline is fine for a single 36px overlay with no complex state |
| CancellationTokenSource timer | DispatcherTimer | DispatcherTimer.Stop/Start is simpler to reason about; CTS is better when combined with async await patterns |

**Installation:** No new packages required.

---

## Architecture Patterns

### Recommended Project Structure

No new files required. All additions are within:
```
JoJot/
├── MainWindow.xaml        # Add toast Border overlay in Grid column 2
├── MainWindow.xaml.cs     # Add deletion engine, toast state machine, trigger wiring
└── Services/
    └── DatabaseService.cs # DeleteNoteAsync already exists — no changes needed
```

### Pattern 1: PendingDeletion Record

**What:** A value type that captures everything needed to commit or undo a deletion.
**When to use:** Whenever a soft-delete occurs; replace previous pending on new deletion.

```csharp
// Defined as private record inside MainWindow (or as a file-local record)
private record PendingDeletion(
    List<NoteTab> Tabs,          // All tabs pending hard delete
    List<int> OriginalIndexes,   // Index in _tabs at time of removal (for undo re-insert)
    CancellationTokenSource Cts  // Cancel = commit early; Dispose on undo
);

private PendingDeletion? _pendingDeletion;
```

### Pattern 2: Soft Delete + Commit Flow

**What:** Remove from UI immediately; hold in memory; hard-delete on timer expiry.
**When to use:** Every single-tab and bulk deletion.

```csharp
private async Task DeleteTabAsync(NoteTab tab)
{
    // 1. Commit any previous pending deletion immediately
    await CommitPendingDeletionAsync();

    // 2. Save original index before removing
    int originalIndex = _tabs.IndexOf(tab);

    // 3. Soft delete: remove from collection and UI
    _tabs.Remove(tab);
    RebuildTabList();

    // 4. Focus cascade (only if deleting active tab)
    if (_activeTab?.Id == tab.Id)
        await ApplyFocusCascadeAsync();

    // 5. Store pending deletion
    var cts = new CancellationTokenSource();
    _pendingDeletion = new PendingDeletion([tab], [originalIndex], cts);

    // 6. Show toast (content swap if already visible; no re-animation)
    ShowToast(BuildToastMessage(tab.DisplayLabel));

    // 7. Start 4s auto-dismiss timer
    _ = StartDismissTimerAsync(cts.Token);
}

private async Task StartDismissTimerAsync(CancellationToken token)
{
    try
    {
        await Task.Delay(4000, token);
        // Timer ran to completion — commit and dismiss
        await CommitPendingDeletionAsync();
        HideToast();
    }
    catch (OperationCanceledException)
    {
        // Cancelled by new deletion or undo — do nothing
    }
}

private async Task CommitPendingDeletionAsync()
{
    if (_pendingDeletion == null) return;
    var pending = _pendingDeletion;
    _pendingDeletion = null;
    pending.Cts.Cancel();
    pending.Cts.Dispose();

    // Hard delete all held tabs
    foreach (var tab in pending.Tabs)
        await DatabaseService.DeleteNoteAsync(tab.Id);
}
```

### Pattern 3: Undo Restoration

**What:** Re-insert NoteTab(s) at original sort_order position(s), rebuild UI, select restored tab.
**When to use:** User clicks "Undo" link in toast.

```csharp
private async Task UndoDeleteAsync()
{
    if (_pendingDeletion == null) return;
    var pending = _pendingDeletion;
    _pendingDeletion = null;

    // Cancel the dismiss timer (don't commit)
    pending.Cts.Cancel();
    pending.Cts.Dispose();

    // Re-insert tabs at original positions (reverse order for multiple)
    var pairs = pending.Tabs.Zip(pending.OriginalIndexes).OrderBy(p => p.Second).ToList();
    foreach (var (tab, index) in pairs)
    {
        int clampedIndex = Math.Min(index, _tabs.Count);
        _tabs.Insert(clampedIndex, tab);
    }

    RebuildTabList();
    // Select the first restored tab (or only tab for single delete)
    SelectTabByNote(pending.Tabs[0]);

    HideToast();
}
```

### Pattern 4: WPF Toast Animation (Storyboard)

**What:** Slide-up (100%→0 translateY over 150ms ease-out) on show; slide-down (0→100% translateY over 150ms) on hide.
**When to use:** `ShowToast` / `HideToast` calls.

```csharp
// In XAML: Toast is a Border with TranslateTransform, initially hidden
// <Border x:Name="ToastBorder" Grid.Column="2" VerticalAlignment="Bottom"
//         Height="36" Background="#333333" Visibility="Collapsed">
//     <Border.RenderTransform>
//         <TranslateTransform x:Name="ToastTranslate" Y="36"/>
//     </Border.RenderTransform>
//     ...
// </Border>

private void ShowToast(string message)
{
    // Update text content regardless of current visibility
    UpdateToastContent(message);

    bool alreadyVisible = ToastBorder.Visibility == Visibility.Visible;
    ToastBorder.Visibility = Visibility.Visible;

    if (!alreadyVisible)
    {
        // Only animate on first appearance; swap is instant (TOST-04)
        var storyboard = new Storyboard();
        var anim = new DoubleAnimation(36, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, ToastTranslate);
        Storyboard.SetTargetProperty(anim, new PropertyPath(TranslateTransform.YProperty));
        storyboard.Children.Add(anim);
        storyboard.Begin();
    }
    // If already visible: just update content, timer reset happens in DeleteTabAsync
}

private void HideToast()
{
    if (ToastBorder.Visibility != Visibility.Visible) return;

    var storyboard = new Storyboard();
    var anim = new DoubleAnimation(0, 36, TimeSpan.FromMilliseconds(150))
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };
    Storyboard.SetTarget(anim, ToastTranslate);
    Storyboard.SetTargetProperty(anim, new PropertyPath(TranslateTransform.YProperty));
    storyboard.Children.Add(anim);
    storyboard.Completed += (s, e) => ToastBorder.Visibility = Visibility.Collapsed;
    storyboard.Begin();
}
```

### Pattern 5: Middle-Click Detection

**What:** Intercept middle mouse button on ListBoxItem before WPF processes the event.
**When to use:** In `CreateTabListItem`, wire `PreviewMouseButtonDown` on the item.

```csharp
item.PreviewMouseButtonDown += (s, e) =>
{
    if (e.ChangedButton == MouseButton.Middle)
    {
        _ = DeleteTabAsync(tab);
        e.Handled = true; // Prevent default ListBox selection behavior
    }
};
```

### Pattern 6: Hover × Icon in CreateTabListItem

**What:** Overlay a `×` TextBlock in the upper-right of the tab item outer `Grid`; fade in/out on `outerBorder.MouseEnter/Leave`.
**When to use:** Added to every tab item in `CreateTabListItem`.

The outer `Border` wraps a `Grid`. To overlay the `×` in the upper-right, change the grid child layout or add an absolute-positioned element. Since the outer content is already a `Grid` with RowDefinitions, the simplest approach is to add the `×` as a floating element using `HorizontalAlignment=Right / VerticalAlignment=Top` and `Grid.RowSpan=2`.

```csharp
// Inside CreateTabListItem, after creating 'grid':
var deleteIcon = new TextBlock
{
    Text = "×",
    FontSize = 12,
    Foreground = MutedTextBrush,   // #888888 default
    Opacity = 0,                    // hidden until hover
    HorizontalAlignment = HorizontalAlignment.Right,
    VerticalAlignment = VerticalAlignment.Top,
    Margin = new Thickness(0, 2, 2, 0),
    Cursor = Cursors.Hand
};
Grid.SetRowSpan(deleteIcon, 2);
grid.Children.Add(deleteIcon);

// Fade-in on tab row hover (outerBorder.MouseEnter already fires)
outerBorder.MouseEnter += (s, e) =>
{
    // Existing hover bg logic...
    AnimateOpacity(deleteIcon, 0, 1, 100);
};
outerBorder.MouseLeave += (s, e) =>
{
    // Existing hover bg logic...
    AnimateOpacity(deleteIcon, 1, 0, 100);
};

// Color change on × hover itself
deleteIcon.MouseEnter += (s, e) =>
    deleteIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xe7, 0x4c, 0x3c));
deleteIcon.MouseLeave += (s, e) =>
    deleteIcon.Foreground = MutedTextBrush;

// Click × to delete
deleteIcon.MouseLeftButtonDown += (s, e) =>
{
    _ = DeleteTabAsync(tab);
    e.Handled = true;
};

// Helper:
private static void AnimateOpacity(UIElement el, double from, double to, int durationMs)
{
    var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs));
    el.BeginAnimation(UIElement.OpacityProperty, anim);
}
```

### Pattern 7: Focus Cascade After Active Tab Delete

**What:** When the deleted tab was active, select: first tab below → last tab in list → auto-create empty tab.
**When to use:** Inside `DeleteTabAsync` after removing from `_tabs`, but before storing pending deletion.

```csharp
private async Task ApplyFocusCascadeAsync()
{
    // After removal, _tabs no longer contains the deleted tab
    // Find tabs visible in current search filter
    var visible = _tabs.Where(t => MatchesSearch(t)).ToList();

    if (visible.Count > 0)
    {
        // "First tab below" means the tab now at the original index (or last)
        // After RebuildTabList, just select first visible as approximation,
        // but correct behavior is: the tab that was immediately below the deleted one.
        // Since _tabs is already updated, SelectTabByNote on the desired tab works.
        // The cascade logic should be computed BEFORE rebuild using saved index.
        SelectTabByNote(visible[0]); // Planner will refine exact cascade index logic
    }
    else if (!string.IsNullOrEmpty(_searchText))
    {
        // No visible tabs in search — clear search and cascade on full list
        SearchBox.Text = "";
        _searchText = "";
        RebuildTabList();
        await ApplyFocusCascadeAsync(); // Recurse once with clear search
    }
    else
    {
        // No tabs at all — auto-create empty tab
        await CreateNewTabAsync();
    }
}
```

### Anti-Patterns to Avoid

- **Re-animating on TOST-04 replacement:** When a second deletion arrives while the toast is visible, DO NOT call the slide-up Storyboard again. Only update content and reset timer. Visual re-animation on every rapid delete is jarring.
- **Hard-deleting on Ctrl+W without soft delete:** Never call `DatabaseService.DeleteNoteAsync` synchronously/immediately. Always go through the soft-delete path so undo remains possible.
- **Using `DispatcherTimer` with `Stop` racing:** If `DispatcherTimer.Tick` fires while being stopped on another path, you can get a double commit. `CancellationTokenSource` with `Task.Delay` avoids this race cleanly.
- **Closing CTS before checking pending:** Always null out `_pendingDeletion` before disposing the CTS to avoid double-dispose if two code paths converge.
- **Capturing stale tab reference in `×` click closure:** The lambda in `CreateTabListItem` captures `tab` from the method parameter — this is correct because each item gets its own `tab` binding. But be careful not to use the `ListBoxItem.Tag` inside the closure (use the captured `tab` directly).
- **Allowing middle-click to scroll the ListBox:** Without `e.Handled = true`, WPF may initiate auto-scroll on middle-click. Always set Handled.
- **Toast placed outside Grid column 2:** Per locked decisions, the toast spans editor area only. It must be a child of Grid column 2, not of the top-level Window Grid row or DockPanel.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Ease-out animation curve | Manual frame-by-frame timer | `CubicEase { EasingMode = EasingMode.EaseOut }` on `DoubleAnimation` | WPF provides full set of easing functions built-in; math is complex otherwise |
| Toast 4s timer with cancellation | `Thread.Sleep` or raw `Task.Delay` loop | `CancellationTokenSource` + `Task.Delay(4000, token)` | Composable, cancellable, no blocking threads |
| Opacity fade animation | Manual timer + incremental opacity set | `BeginAnimation(OpacityProperty, DoubleAnimation)` | WPF animation system handles interpolation, compositing, and thread marshalling |

**Key insight:** WPF's animation and easing system is mature and handles all interpolation internally. Custom timer-based approaches reinvent this poorly.

---

## Common Pitfalls

### Pitfall 1: CancellationTokenSource Disposal Race
**What goes wrong:** `CommitPendingDeletionAsync` cancels and disposes the CTS. If `StartDismissTimerAsync` catches `OperationCanceledException` and tries to access the CTS after disposal, you get an `ObjectDisposedException`.
**Why it happens:** The `catch (OperationCanceledException)` block in the timer fires after `Dispose()` if the task was mid-await.
**How to avoid:** The catch block should do nothing (just `return`). The CTS is disposed by the code that calls `Cancel()`, so the timer task should never touch the CTS after cancellation.
**Warning signs:** `ObjectDisposedException` in the timer task's catch block.

### Pitfall 2: WPF Animation Target Not in Visual Tree
**What goes wrong:** `Storyboard.Begin()` silently does nothing if the target `TranslateTransform` is not reachable from the Storyboard's name scope.
**Why it happens:** Named elements in code-behind don't automatically join the window's name scope.
**How to avoid:** Either (a) define the `TranslateTransform` and its `x:Name` in XAML so they're in the window's name scope, or (b) use `Storyboard.SetTarget(anim, transformObject)` (object reference, not name) and call `storyboard.Begin(this)` with the window as root. The code examples above use `SetTarget` with object reference — this approach bypasses name scope entirely.
**Warning signs:** Animation method runs with no visual effect; no exception thrown.

### Pitfall 3: Hover × MouseLeave Fires When Hovering Over × Itself
**What goes wrong:** The `×` TextBlock overlays the `outerBorder`. When the mouse moves from `outerBorder` to the `×`, `outerBorder.MouseLeave` fires and hides the `×`, causing flicker.
**Why it happens:** WPF `MouseLeave` on a parent fires when focus moves to a child element that does not relay the event.
**How to avoid:** Use `outerBorder.MouseEnter/Leave` for the background highlight only. For the `×` visibility, check `IsMouseOver` on `outerBorder` — since `×` is a visual child, `outerBorder.IsMouseOver` remains true when hovering over `×`. Alternatively, wire `MouseEnter` on `outerBorder` to show and use `outerBorder.MouseLeave` to check if the new focused element is the `×` itself before hiding.
**Best fix:** Set `deleteIcon.IsHitTestVisible = true` (default) and handle `outerBorder.MouseLeave` only — WPF `MouseLeave` on the outer border fires only when the mouse exits the entire border subtree (including children), so `deleteIcon` being a child means the leave doesn't fire until the mouse exits the tab row entirely. **This is actually the correct default WPF behavior — no workaround needed.** Verify this during implementation.
**Warning signs:** × flickers when moving mouse from tab label area to × icon.

### Pitfall 4: Ctrl+W Triggers Browser-Style Close in Some Contexts
**What goes wrong:** `Ctrl+W` may be partially handled by other WPF controls before reaching `Window_PreviewKeyDown`.
**Why it happens:** `PreviewKeyDown` tunnels from Window downward, so Window catches it first — this is actually correct behavior in WPF. No issue expected.
**How to avoid:** Confirm `e.Handled = true` is set in the Ctrl+W handler. Since this is `Preview`, it fires before the focused control's `KeyDown`.
**Warning signs:** Ctrl+W does nothing when focus is in the `ContentEditor`.

### Pitfall 5: Re-inserting Tabs at Wrong Index After Bulk Undo
**What goes wrong:** When undoing a bulk delete, inserting tabs at their original indexes in forward order causes subsequent inserts to land at wrong positions (each insert shifts later indexes).
**Why it happens:** Inserting at index 3 after inserting at index 1 means the actual index-3 slot has shifted.
**How to avoid:** Sort restored tabs by original index ascending and insert in order. For bulk undo, since all tabs were removed before any inserts, insert in ascending order of `OriginalIndex` with clamping:
```csharp
// Insert in ascending order — each insert shifts later intended positions by 1
// But since _tabs was emptied of EXACTLY these tabs, the math works if we clamp
```
Actually the safest approach is to rebuild `_tabs` from scratch after undo: re-add all items in their correct sort_order position using the saved indexes.
**Warning signs:** After bulk undo, tabs appear in wrong order.

### Pitfall 6: UseWindowsForms System.Drawing Conflicts
**What goes wrong:** `Color` resolves ambiguously between `System.Windows.Media.Color` and `System.Drawing.Color`.
**Why it happens:** `UseWindowsForms=true` pulls in `System.Drawing` via implicit usings (established in Phase 4, decision [04-02]).
**How to avoid:** Fully qualify: `System.Windows.Media.Color.FromRgb(...)` and `new System.Windows.Media.SolidColorBrush(...)` — already the established pattern in the codebase.
**Warning signs:** CS0104 ambiguity error on `Color` or `SolidColorBrush`.

---

## Code Examples

### Toast XAML Overlay in Grid Column 2

```xml
<!-- Add inside the main Grid, as a peer of the ContentEditor -->
<!-- Must be declared AFTER ContentEditor so it renders on top -->
<Border x:Name="ToastBorder" Grid.Column="2"
        VerticalAlignment="Bottom" Height="36"
        Background="#333333" Visibility="Collapsed"
        Panel.ZIndex="10">
    <Border.RenderTransform>
        <TranslateTransform x:Name="ToastTranslate" Y="36"/>
    </Border.RenderTransform>
    <Grid Margin="12,0">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <!-- Left: deletion message -->
        <TextBlock x:Name="ToastMessageBlock" Grid.Column="0"
                   VerticalAlignment="Center" Foreground="White" FontSize="13"
                   TextTrimming="CharacterEllipsis"/>
        <!-- Right: Undo link -->
        <TextBlock Grid.Column="1" VerticalAlignment="Center"
                   Foreground="#2196F3" FontSize="13"
                   TextDecorations="Underline" Cursor="Hand"
                   Margin="12,0,0,0"
                   MouseLeftButtonDown="UndoToast_Click">
            Undo
        </TextBlock>
    </Grid>
</Border>
```

### Toast Message Inline Formatting (TOST-06)

The `ToastMessageBlock` displays `"tab name" deleted` with the name in italic. Since XAML `TextBlock` Inlines cannot be set via simple string, format in code-behind:

```csharp
private void UpdateToastContent(string rawLabel)
{
    // Truncate to 30 chars per TOST-06
    string truncated = rawLabel.Length > 30 ? rawLabel[..30] + "…" : rawLabel;

    ToastMessageBlock.Inlines.Clear();
    ToastMessageBlock.Inlines.Add(new Run("\u201C")); // opening "
    ToastMessageBlock.Inlines.Add(new Run(truncated) { FontStyle = FontStyles.Italic });
    ToastMessageBlock.Inlines.Add(new Run("\u201D deleted")); // " deleted
}

private void UpdateToastContentBulk(int count)
{
    ToastMessageBlock.Inlines.Clear();
    ToastMessageBlock.Inlines.Add(new Run($"{count} notes deleted"));
}
```

### Ctrl+W Handler Addition to Window_PreviewKeyDown

```csharp
// Ctrl+W: Delete active tab (TDEL-01)
if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
{
    if (_activeTab != null)
    {
        _ = DeleteTabAsync(_activeTab);
        e.Handled = true;
    }
    return;
}
```

### DeleteMultipleAsync for Bulk Infrastructure (TOST-05, TDEL-06)

```csharp
/// <summary>
/// Soft-deletes multiple tabs at once. Pinned tabs are silently skipped (TDEL-06).
/// Shows a single "N notes deleted" toast with one Undo for all (TOST-05).
/// </summary>
public async Task DeleteMultipleAsync(IEnumerable<NoteTab> candidates)
{
    var toDelete = candidates.Where(t => !t.Pinned).ToList();
    if (toDelete.Count == 0) return;

    // Commit any in-flight pending deletion first
    await CommitPendingDeletionAsync();

    // Save original indexes before any removal
    var originalIndexes = toDelete.Select(t => _tabs.IndexOf(t)).ToList();

    // Remove from collection
    foreach (var tab in toDelete)
        _tabs.Remove(tab);

    // Handle active tab focus cascade if active tab was deleted
    if (_activeTab != null && toDelete.Any(t => t.Id == _activeTab.Id))
        await ApplyFocusCascadeAsync();

    RebuildTabList();

    // Store pending deletion
    var cts = new CancellationTokenSource();
    _pendingDeletion = new PendingDeletion(toDelete, originalIndexes, cts);

    // Show toast
    ShowToast(null); // null signals bulk → use count variant
    UpdateToastContentBulk(toDelete.Count);

    // Start 4s timer
    _ = StartDismissTimerAsync(cts.Token);
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `DispatcherTimer` for UI timers | `CancellationTokenSource` + `Task.Delay` | .NET Core / async era | Composable cancellation; no Stop/Tick race |
| `Storyboard` in XAML ResourceDictionary | `Storyboard` created in code-behind | Always valid | Code-behind creation avoids XAML name scope issues with dynamic elements |
| Confirmation dialogs for delete | Immediate delete + undo toast | Modern UX pattern (GMail 2009 → VS Code, Slack) | Faster workflow; undo is recoverable |

**Deprecated/outdated:**
- `MessageBox.Show("Are you sure?")` before delete: Out of scope by locked decision (TDEL-02).
- `Window.RegisterName` for code-created transforms: Not needed when using `Storyboard.SetTarget` with object reference.

---

## Open Questions

1. **Exact focus cascade index after deletion**
   - What we know: "First tab below" = the tab that was immediately below the deleted one in list order.
   - What's unclear: After `_tabs.Remove(tab)`, the tab that was at `originalIndex + 1` is now at `originalIndex`. The cascade logic should capture `originalIndex` before removal and then select `_tabs[Math.Min(originalIndex, _tabs.Count - 1)]` filtered by search.
   - Recommendation: Planner should specify: save `originalIndex` before removal; after removal, if `originalIndex < _tabs.Count`, select `_tabs[originalIndex]` (which is now the tab that was "below"); else select `_tabs[_tabs.Count - 1]` (last tab).

2. **Toast Visibility vs Collapse during animation**
   - What we know: `Visibility.Collapsed` removes the element from layout; during slide-down animation the element must remain `Visible` until animation completes, then collapse.
   - What's unclear: If `HideToast` is called rapidly (undo then immediate new deletion), the Storyboard.Completed callback may fire after the new toast has appeared.
   - Recommendation: Track a `_toastAnimating` bool flag; if a new deletion arrives during hide animation, cancel the hide and re-show immediately.

3. **Search-active + bulk delete cascade**
   - What we know: If search is active and all visible tabs are deleted, clear search then cascade on full list.
   - What's unclear: Which tab gets focus after clearing search? The tab that was at position 0 in the unfiltered list is probably correct.
   - Recommendation: After search clear + `RebuildTabList()`, select `_tabs.FirstOrDefault()` (first in sort order, which is first pinned or first unpinned).

---

## Sources

### Primary (HIGH confidence)
- Direct code reading: `MainWindow.xaml.cs`, `MainWindow.xaml`, `DatabaseService.cs`, `NoteTab.cs` — established patterns, existing APIs, WPF control topology
- `.planning/phases/05-deletion-toast/05-CONTEXT.md` — locked decisions and discretion areas
- WPF documentation (training knowledge, HIGH confidence for stable APIs): `Storyboard`, `DoubleAnimation`, `CancellationTokenSource`, `TranslateTransform`, `PreviewMouseButtonDown`

### Secondary (MEDIUM confidence)
- WPF MouseLeave/MouseEnter child propagation behavior: Standard documented WPF behavior; verified by established codebase pattern (outerBorder.MouseEnter/Leave already used in CreateTabListItem)
- `Task.Delay` with `CancellationToken` pattern: Standard .NET async idiom, documented in Microsoft Docs

### Tertiary (LOW confidence)
- None — all claims grounded in direct code inspection or well-established WPF/NET APIs

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new dependencies; WPF animation APIs are stable and well-documented
- Architecture: HIGH — deletion model, toast state machine, and trigger patterns are well-understood from direct codebase inspection
- Pitfalls: HIGH — MouseLeave/child interaction and animation target issues are established WPF gotchas; USE-Windows-Forms conflict is a documented project decision

**Research date:** 2026-03-02
**Valid until:** 2026-06-02 (stable WPF APIs; no fast-moving ecosystem)
