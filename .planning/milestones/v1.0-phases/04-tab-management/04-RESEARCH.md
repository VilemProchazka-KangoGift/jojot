# Phase 4: Tab Management - Research

**Researched:** 2026-03-02
**Domain:** WPF UI — ListBox-based tab panel, drag-to-reorder, inline editing, search filtering
**Confidence:** HIGH

## Summary

Phase 4 is the first real UI phase. It introduces a 180px side panel with a vertically scrollable tab list, a search/filter box, and a simple TextBox content area. The entire implementation uses stock WPF controls with code-behind (no MVVM framework, consistent with the project's established patterns). The key technical challenges are: (1) drag-to-reorder within pinned/unpinned zones, (2) inline rename with correct focus management, (3) real-time search filtering that hides non-matches, and (4) smart label fallback with relative date formatting.

**Primary recommendation:** Use an ObservableCollection-backed ListBox for the tab list with DataTemplate for the two-row layout. Implement drag-to-reorder via mouse event handlers (PreviewMouseLeftButtonDown/Move/Up) with a visual drop indicator adorner. Keep the model-to-UI binding manual via code-behind property setters — do not introduce a data binding framework.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Tab panel on the LEFT side of the window, 180px fixed width
- 1px subtle vertical border line separates panel from content area (works before theming in Phase 7)
- Distinct fixed header bar at top of panel containing search box and + button, with subtle bottom border separating it from the scrollable tab list
- + (new tab) button sits to the right of the search box in the header; search box takes remaining width
- Two-row layout per tab entry (~40-48px tall): Row 1: pin icon (if pinned) + label (truncated with ellipsis), Row 2: created date (left-aligned) + updated time (right-aligned)
- "New note" placeholder label in muted italic per spec (TABS-02)
- Relative smart date formatting: "Just now", "5 min ago", "Today 2:30 PM", "Yesterday", "Mar 1", "Jan 15, 2025"
- Pinned tabs: pin icon before label + subtle zone separator line/label between pinned and unpinned zones
- Simple WPF TextBox for content (Phase 6 replaces with full editor)
- Content saves on tab switch and window close — no background autosave timer
- Monospace font from day one: Consolas 13pt, word-wrap on, no horizontal scrollbar
- Auto-create first empty tab when window opens with no tabs — no empty state screen
- Drag-to-reorder: thin horizontal accent-colored drop indicator line between tabs, dragged tab at 0.6 opacity
- Tab hover: subtle background color highlight on non-active tabs
- Search box: always visible in header with placeholder text; Ctrl+F focuses it; real-time filtering; Escape clears and returns focus to editor
- Search filtering: non-matching tabs hidden entirely (not dimmed)

### Claude's Discretion
- Exact pixel dimensions and spacing within tab entries
- Accent color values (pre-theming hardcoded values, Phase 7 replaces with tokens)
- Animation easing curves and exact durations for drag feedback
- Internal TextBox implementation details (scroll behavior, selection handling)
- Keyboard focus management specifics beyond what's in requirements

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| TABS-01 | Tab list panel, 180px fixed width, vertically scrollable | ListBox in ScrollViewer with fixed-width Grid column |
| TABS-02 | Tab label with 3-tier fallback: custom name -> first ~30 chars -> "New note" | Computed property in NoteTab model with styling toggle |
| TABS-03 | Tab entry shows pin icon, label, created date, updated time | Two-row DataTemplate with TextBlock elements |
| TABS-04 | Active tab highlighted with 2px left accent border | DataTemplate trigger on IsSelected, Border element |
| TABS-05 | Drag-to-reorder within zones (pinned/unpinned separately) | Mouse event handlers + adorner drop indicator |
| TABS-06 | Tab rename via double-click, F2; inline editable field | TextBox overlay toggled by double-click/F2 handler |
| TABS-07 | Empty rename clears custom name, reverts to fallback | Rename commit logic checks IsNullOrWhiteSpace |
| TABS-08 | New tab (Ctrl+T / + button): create note, focus editor | DatabaseService insert + collection add + focus |
| TABS-09 | Clone tab (Ctrl+K): duplicate content into new tab below | Copy note fields, insert after current, select |
| TABS-10 | Pin/unpin toggle (Ctrl+P): pinned always sorted to top | Toggle pinned field, re-sort collection, persist |
| TABS-11 | Tab search box (Ctrl+F): filters by label and content | CollectionViewSource with Filter predicate |
| TABS-12 | Search box takes width minus + button; Escape clears | XAML layout with star-width + auto-width columns |
| TABS-13 | Ctrl+Tab / Ctrl+Shift+Tab: next/prev navigation | KeyDown handler cycling SelectedIndex |
</phase_requirements>

## Standard Stack

### Core
| Component | Type | Purpose | Why Standard |
|-----------|------|---------|--------------|
| ListBox | WPF built-in | Tab list with selection, scrolling, keyboard nav | Native selection model, virtualizing, customizable via DataTemplate |
| ObservableCollection<NoteTab> | System.Collections.ObjectModel | Tab data source with change notification | Automatic UI sync on add/remove/clear without manual refresh |
| CollectionViewSource | System.Windows.Data | Search filtering | Built-in filter/sort/group without modifying source collection |
| TextBox | WPF built-in | Content editor placeholder (Phase 6 replaces) | Simple, sufficient for save-on-switch behavior |
| ScrollViewer | WPF built-in | Scrollable tab list | Native scroll with virtualizing panel |

### Supporting
| Component | Type | Purpose | When to Use |
|-----------|------|---------|-------------|
| Adorner | System.Windows.Documents | Drag drop indicator line | During drag-to-reorder to show insertion point |
| VisualTreeHelper | System.Windows.Media | Hit-testing during drag | To find ListBoxItem under cursor for drop target |
| FocusManager | System.Windows.Input | Editor focus after operations | After tab creation, search clear, rename commit |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| ListBox | ItemsControl + custom selection | More control but must reimplement selection, keyboard nav, accessibility |
| ObservableCollection manual sort | ICollectionView.SortDescriptions | Sort descriptions don't support zone-aware ordering (pinned/unpinned) |
| Adorner for drag indicator | Canvas overlay | Canvas easier but doesn't track scroll position; Adorner stays with visual tree |

## Architecture Patterns

### Recommended File Structure
```
JoJot/
├── Models/
│   └── NoteTab.cs              # In-memory tab model (id, content, pinned, label, dates, sort_order)
├── Services/
│   └── DatabaseService.cs      # Add: notes CRUD (GetNotesForDesktop, InsertNote, UpdateNote, etc.)
├── Controls/
│   └── TabPanelControl.xaml     # UserControl: tab list panel (header + list)
│   └── TabPanelControl.xaml.cs  # Code-behind: drag-reorder, rename, search, selection
├── MainWindow.xaml              # Grid layout: tab panel (180px) | separator | content area
└── MainWindow.xaml.cs           # Orchestration: load tabs, save content, keyboard shortcuts
```

### Pattern 1: ObservableCollection + Manual Sort for Zone Ordering
**What:** The tab list is backed by an ObservableCollection<NoteTab> that is sorted with pinned tabs first, then by sort_order. When pinned state changes, the item is moved within the collection (not re-sorted globally).
**When to use:** Whenever tab order changes (pin/unpin, drag-reorder).
**Example:**
```csharp
// Sort order: pinned DESC, sort_order ASC
private void RefreshTabOrder()
{
    var sorted = _tabs.OrderByDescending(t => t.Pinned)
                      .ThenBy(t => t.SortOrder)
                      .ToList();
    _tabs.Clear();
    foreach (var tab in sorted) _tabs.Add(tab);
}
```
**Note:** For drag-reorder, use Move() on the ObservableCollection instead of Clear+Re-add to preserve smooth UI (Move fires a single notification vs N notifications).

### Pattern 2: DataTemplate with Trigger-Based Styling
**What:** Tab entries use a DataTemplate with triggers for active state (2px left border), pinned icon visibility, and "New note" italic styling.
**When to use:** All tab list rendering.
**Example:**
```xml
<DataTemplate x:Key="TabItemTemplate">
    <Border x:Name="TabBorder" Padding="6,4" BorderThickness="2,0,0,0" BorderBrush="Transparent">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>
            <!-- Row 1: Pin icon + Label -->
            <StackPanel Orientation="Horizontal" Grid.Row="0">
                <TextBlock x:Name="PinIcon" Text="📌" Visibility="Collapsed" Margin="0,0,4,0"/>
                <TextBlock Text="{Binding DisplayLabel}" TextTrimming="CharacterEllipsis"/>
            </StackPanel>
            <!-- Row 2: Created + Updated -->
            <Grid Grid.Row="1">
                <TextBlock Text="{Binding CreatedDisplay}" HorizontalAlignment="Left" FontSize="10" Opacity="0.6"/>
                <TextBlock Text="{Binding UpdatedDisplay}" HorizontalAlignment="Right" FontSize="10" Opacity="0.6"/>
            </Grid>
        </Grid>
    </Border>
    <DataTemplate.Triggers>
        <DataTrigger Binding="{Binding Pinned}" Value="True">
            <Setter TargetName="PinIcon" Property="Visibility" Value="Visible"/>
        </DataTrigger>
        <DataTrigger Binding="{Binding IsPlaceholder}" Value="True">
            <Setter TargetName="TabBorder" Property="TextBlock.FontStyle" Value="Italic"/>
            <Setter TargetName="TabBorder" Property="TextBlock.Foreground" Value="#888888"/>
        </DataTrigger>
    </DataTemplate.Triggers>
</DataTemplate>
```

### Pattern 3: Inline Rename via TextBox Overlay
**What:** Double-click or F2 swaps the label TextBlock for a TextBox. Enter commits, Escape cancels, LostFocus commits.
**When to use:** TABS-06, TABS-07.
**Example:**
```csharp
private void BeginRename(ListBoxItem item, NoteTab tab)
{
    var labelBlock = FindChild<TextBlock>(item, "LabelText");
    var renameBox = FindChild<TextBox>(item, "RenameBox");
    labelBlock.Visibility = Visibility.Collapsed;
    renameBox.Text = tab.Name ?? "";
    renameBox.Visibility = Visibility.Visible;
    renameBox.SelectAll();
    renameBox.Focus();
}

private async void CommitRename(TextBox box, NoteTab tab)
{
    box.Visibility = Visibility.Collapsed;
    var labelBlock = /* find sibling */;
    labelBlock.Visibility = Visibility.Visible;

    string newName = box.Text.Trim();
    tab.Name = string.IsNullOrWhiteSpace(newName) ? null : newName;  // TABS-07
    tab.RefreshDisplayLabel();
    await DatabaseService.UpdateNoteNameAsync(tab.Id, tab.Name);
}
```

### Pattern 4: CollectionViewSource for Search Filtering
**What:** The ListBox.ItemsSource is a CollectionViewSource.View that applies a filter predicate based on the search text. Non-matching items are hidden entirely.
**When to use:** TABS-11, TABS-12.
**Example:**
```csharp
private ICollectionView _tabsView;

private void InitializeTabView()
{
    _tabsView = CollectionViewSource.GetDefaultView(_tabs);
    _tabsView.Filter = TabFilter;
    TabList.ItemsSource = _tabsView;
}

private bool TabFilter(object obj)
{
    if (string.IsNullOrEmpty(_searchText)) return true;
    var tab = (NoteTab)obj;
    return tab.DisplayLabel.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
        || tab.Content.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
}

private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
{
    _searchText = SearchBox.Text;
    _tabsView.Refresh();
}
```

### Pattern 5: Drag-to-Reorder with Zone Boundaries
**What:** Mouse event handlers on ListBoxItems track drag start (PreviewMouseLeftButtonDown + minimum distance threshold), show a drop indicator adorner between items, and enforce zone boundaries (pinned items stay in pinned zone, unpinned in unpinned).
**When to use:** TABS-05.
**Key aspects:**
- Minimum 5px drag distance before initiating (prevents accidental drags on click)
- Dragged item rendered at 0.6 opacity
- Drop indicator: thin horizontal line in accent color, positioned between items
- Zone enforcement: if dragging an unpinned tab, drop targets are only between unpinned tabs (and vice versa)
- On drop: update sort_order for affected items, persist to database

### Anti-Patterns to Avoid
- **MVVM framework introduction:** Project uses code-behind. Do not introduce INotifyPropertyChanged boilerplate, relay commands, or ViewModels. Use direct property setters + manual UI refresh.
- **DragDrop.DoDragDrop for reorder:** The built-in WPF drag-drop API is designed for inter-application drag (OLE drag). For intra-ListBox reorder, use raw mouse events — cleaner, more responsive, no DragDropEffects overhead.
- **VirtualizingStackPanel with drag:** If the list virtualizes, items outside viewport don't have containers. For Phase 4 with reasonable tab counts, disable virtualization (VirtualizingStackPanel.IsVirtualizing="False") to ensure drag works correctly. Can optimize later if 500+ tabs become common.
- **Binding to database directly:** Always go through the in-memory NoteTab collection. Database operations are fire-and-forget persistence, never the source of truth for the UI during a session.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Collection change notification | Custom event system | ObservableCollection<T> | Built-in INotifyCollectionChanged, proven with WPF binding |
| List filtering | Manual visible/collapsed toggling | ICollectionView.Filter | Handles all edge cases (add/remove while filtered, refresh) |
| Scroll-to-selected | Manual scroll offset calculation | ListBox.ScrollIntoView() | Handles virtualization, variable-height items, smooth scrolling |
| Date formatting | Custom date math | Relative formatter utility method | Simple but error-prone (timezone, DST, edge cases at midnight) |
| Text truncation | Substring + "..." | TextTrimming="CharacterEllipsis" | WPF renders ellipsis at exact pixel boundary, respects font metrics |

## Common Pitfalls

### Pitfall 1: Focus Fights Between Search Box and Editor
**What goes wrong:** Ctrl+F focuses search box, but TextBox in content area steals focus on click. Escape from search should return to editor, but if editor was never focused, Focus() fails silently.
**Why it happens:** WPF focus is split between logical focus (FocusManager) and keyboard focus (Keyboard.Focus). They can disagree.
**How to avoid:** Always use `Keyboard.Focus(element)` not just `element.Focus()`. After Escape from search, explicitly call `Keyboard.Focus(_contentEditor)`. Track "last editor had focus" state.
**Warning signs:** Keyboard shortcuts stop working after search; editor appears focused but doesn't receive keypresses.

### Pitfall 2: ObservableCollection.Move() Not Available Pre-.NET 5
**What goes wrong:** Move(oldIndex, newIndex) is available on ObservableCollection since .NET 5.
**Why it happens:** Old documentation references RemoveAt + Insert pattern.
**How to avoid:** .NET 10 target — Move() is fully available. Use it for single-notification drag-reorder instead of Remove+Insert (which fires two notifications and can cause flickering).
**Warning signs:** Double animation or flicker during drag-drop.

### Pitfall 3: Save-on-Switch Race Condition
**What goes wrong:** Selecting a new tab triggers save of old tab's content AND load of new tab's content. If both are async, the load can complete before the save, and then the UI shows stale data.
**Why it happens:** SelectionChanged fires before the old tab's content is persisted.
**How to avoid:** Save is synchronous from the UI perspective — capture content from the TextBox (already on UI thread), then fire-and-forget the database write. Load the new tab's content from the in-memory model (NoteTab.Content), not from the database. Database is for persistence, not source of truth.
**Warning signs:** Switching tabs rapidly shows wrong content momentarily.

### Pitfall 4: Drag Adorner Not Removed on Edge Cases
**What goes wrong:** If the mouse leaves the window during a drag, MouseUp may never fire, leaving the adorner visible permanently.
**Why it happens:** WPF doesn't capture mouse by default. Mouse events stop when cursor leaves.
**How to avoid:** Call Mouse.Capture(element) on drag start, Mouse.Capture(null) on end. Also handle MouseLeave and LostMouseCapture as drag-cancel events. Always remove adorner in a finally-style cleanup.
**Warning signs:** Ghost drop indicator line stays visible after botched drag.

### Pitfall 5: ListBox Keyboard Navigation Conflicts
**What goes wrong:** Arrow keys in the ListBox move selection, but Ctrl+Tab should cycle tabs and F2 should rename. Default ListBox key handling intercepts keys before custom handlers.
**Why it happens:** WPF routed events bubble through the visual tree. ListBox handles certain keys in OnKeyDown.
**How to avoid:** Use PreviewKeyDown (tunneling) for custom keyboard shortcuts. Mark `e.Handled = true` to prevent ListBox default behavior for keys you handle.
**Warning signs:** F2 or Ctrl+Tab do nothing because ListBox swallowed the event.

### Pitfall 6: Content TextBox AcceptsReturn and AcceptsTab
**What goes wrong:** Default TextBox doesn't accept Enter or Tab, treating them as dialog navigation keys. Content appears single-line.
**Why it happens:** WPF TextBox is designed for form fields. Multi-line editing requires explicit opt-in.
**How to avoid:** Set `AcceptsReturn="True"` and `AcceptsTab="True"` on the content TextBox. Also set `TextWrapping="Wrap"` and `VerticalScrollBarVisibility="Auto"`.
**Warning signs:** Enter key moves focus instead of inserting newline.

## Code Examples

### NoteTab Model Class
```csharp
public class NoteTab
{
    public long Id { get; set; }
    public string DesktopGuid { get; set; } = "";
    public string? Name { get; set; }
    public string Content { get; set; } = "";
    public bool Pinned { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int SortOrder { get; set; }
    public int EditorScrollOffset { get; set; }
    public int CursorPosition { get; set; }

    /// <summary>
    /// TABS-02: 3-tier label fallback
    /// </summary>
    public string DisplayLabel => Name
        ?? (string.IsNullOrWhiteSpace(Content) ? "New note" : Content[..Math.Min(Content.Length, 30)].Trim());

    /// <summary>
    /// True when using "New note" placeholder (for italic styling)
    /// </summary>
    public bool IsPlaceholder => Name == null && string.IsNullOrWhiteSpace(Content);

    public string CreatedDisplay => FormatRelativeDate(CreatedAt);
    public string UpdatedDisplay => FormatRelativeTime(UpdatedAt);

    private static string FormatRelativeDate(DateTime dt)
    {
        var now = DateTime.Now;
        if (dt.Date == now.Date) return dt.ToString("h:mm tt");
        if (dt.Date == now.Date.AddDays(-1)) return "Yesterday";
        if (dt.Year == now.Year) return dt.ToString("MMM d");
        return dt.ToString("MMM d, yyyy");
    }

    private static string FormatRelativeTime(DateTime dt)
    {
        var diff = DateTime.Now - dt;
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min ago";
        if (dt.Date == DateTime.Now.Date) return $"Today {dt:h:mm tt}";
        if (dt.Date == DateTime.Now.Date.AddDays(-1)) return "Yesterday";
        if (dt.Year == DateTime.Now.Year) return dt.ToString("MMM d");
        return dt.ToString("MMM d, yyyy");
    }
}
```

### DatabaseService Notes CRUD
```csharp
// GetNotesForDesktopAsync: loads all tabs for a desktop, ordered by pinned DESC, sort_order ASC
public static async Task<List<NoteTab>> GetNotesForDesktopAsync(string desktopGuid)
{
    var notes = new List<NoteTab>();
    await _writeLock.WaitAsync();
    try
    {
        var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT id, desktop_guid, name, content, pinned, created_at, updated_at, sort_order, editor_scroll_offset, cursor_position FROM notes WHERE desktop_guid = @guid ORDER BY pinned DESC, sort_order ASC;";
        cmd.Parameters.AddWithValue("@guid", desktopGuid);
        using var reader = await cmd.ExecuteReaderAsync();
        while (reader.Read())
        {
            notes.Add(new NoteTab
            {
                Id = reader.GetInt64(0),
                DesktopGuid = reader.GetString(1),
                Name = reader.IsDBNull(2) ? null : reader.GetString(2),
                Content = reader.GetString(3),
                Pinned = reader.GetInt32(4) != 0,
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                UpdatedAt = DateTime.Parse(reader.GetString(6)),
                SortOrder = reader.GetInt32(7),
                EditorScrollOffset = reader.GetInt32(8),
                CursorPosition = reader.GetInt32(9)
            });
        }
    }
    finally { _writeLock.Release(); }
    return notes;
}

// InsertNoteAsync: creates a new note, returns its ID
public static async Task<long> InsertNoteAsync(string desktopGuid, string? name, string content, bool pinned, int sortOrder)
{
    // ... parameterized INSERT, return last_insert_rowid()
}

// UpdateNoteContentAsync: updates content and updated_at
public static async Task UpdateNoteContentAsync(long noteId, string content)
{
    // ... parameterized UPDATE notes SET content=@c, updated_at=datetime('now') WHERE id=@id
}

// UpdateNoteSortOrderAsync: batch-updates sort_order for multiple notes
public static async Task UpdateNoteSortOrdersAsync(IEnumerable<(long Id, int SortOrder)> updates)
{
    // ... single transaction with multiple UPDATE statements
}
```

### Main Layout XAML
```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="180"/>   <!-- Tab panel -->
        <ColumnDefinition Width="1"/>     <!-- Separator -->
        <ColumnDefinition Width="*"/>     <!-- Content area -->
    </Grid.ColumnDefinitions>

    <!-- Tab Panel -->
    <local:TabPanelControl x:Name="TabPanel" Grid.Column="0"/>

    <!-- Separator -->
    <Border Grid.Column="1" Background="#E0E0E0"/>

    <!-- Content Area -->
    <TextBox x:Name="ContentEditor" Grid.Column="2"
             AcceptsReturn="True" AcceptsTab="True"
             TextWrapping="Wrap" FontFamily="Consolas" FontSize="13"
             VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"
             BorderThickness="0" Padding="12,8"/>
</Grid>
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| INotifyPropertyChanged on every model | Direct property + manual Refresh() | N/A (project pattern) | Less boilerplate, simpler code-behind, no binding overhead |
| DragDrop.DoDragDrop for list reorder | Raw mouse events + adorner | Always been better for intra-control | Cleaner UX, no OLE overhead, precise drop indicator |
| CollectionView.Filter with string | Lambda predicate | .NET Framework 3.5+ | Type-safe, inline, no converter needed |

## Open Questions

1. **Pin icon glyph choice**
   - What we know: CONTEXT.md says "pin icon" before label
   - What's unclear: Unicode character, custom drawn, or path geometry
   - Recommendation: Use Unicode pushpin U+1F4CC (📌) initially, replace with vector Path in Phase 7 theming. Simple, visible, no custom assets needed.

2. **Zone separator visual**
   - What we know: "subtle zone separator line/label between pinned and unpinned zones"
   - What's unclear: Text label ("Pinned"/"Other"), or just a line
   - Recommendation: Thin line (1px, same as panel border color) with no text label. Keeps the 180px width clean. The pin icons already communicate the distinction.

3. **Content save timing on window close**
   - What we know: "Content saves on tab switch and window close" — Phase 4 adds save-on-switch, FlushAndClose needs update
   - What's unclear: Whether OnClosing should block for save or fire-and-forget
   - Recommendation: Save is fast (single UPDATE). Do it synchronously in OnClosing (already on UI thread) using .GetAwaiter().GetResult() pattern already established for CloseAsync in App.OnExit. The content is already in memory — just write it.

## Sources

### Primary (HIGH confidence)
- WPF framework built-in controls (ListBox, ObservableCollection, CollectionViewSource, Adorner) — Microsoft .NET documentation
- Project codebase: DatabaseService.cs, MainWindow.xaml.cs, App.xaml.cs — established patterns
- CONTEXT.md — user design decisions

### Secondary (MEDIUM confidence)
- WPF drag-to-reorder pattern with mouse events — well-established community pattern, multiple consistent implementations

### Tertiary (LOW confidence)
- None — all patterns are stock WPF

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All stock WPF, no external dependencies
- Architecture: HIGH - Follows established project patterns (code-behind, static services)
- Pitfalls: HIGH - Well-known WPF focus/drag edge cases from framework documentation

**Research date:** 2026-03-02
**Valid until:** Indefinite (stable WPF APIs)
