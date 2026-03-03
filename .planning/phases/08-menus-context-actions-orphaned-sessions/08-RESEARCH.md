# Phase 8: Menus, Context Actions & Orphaned Sessions - Research

**Researched:** 2026-03-03
**Domain:** WPF custom menus, context menus, popup flyouts, orphaned session recovery
**Confidence:** HIGH

## Summary

Phase 8 adds three command surfaces to JoJot: a hamburger menu for window-level operations, a right-click context menu for tab operations, and an orphaned session recovery panel. The project uses code-behind pattern (no MVVM) with static services and programmatic UI construction, established across Phases 1-7. All three surfaces use standard WPF primitives (Popup, ContextMenu) styled with DynamicResource theme tokens and Segoe Fluent Icons, matching the toolbar aesthetic from Phase 7.

The orphan recovery panel requires new DatabaseService methods to query orphaned sessions and migrate tabs between desktops. VirtualDesktopService already identifies orphaned sessions during MatchSessionsAsync but does not expose the orphan list — this needs a small addition to surface that data.

**Primary recommendation:** Build custom-styled Popup elements for the hamburger menu (not WPF Menu control) to achieve the exact visual treatment described in CONTEXT.md. Use standard WPF ContextMenu for tab right-click (it handles positioning automatically). Implement the recovery panel as a contained overlay within the sidebar area.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Recovery panel: Sidebar flyout that slides out from the left sidebar when triggered from hamburger menu
- Each orphaned session as a card with desktop name, tab count, last updated date, and always-visible Adopt/Open/Delete buttons
- Adopted (merged) tabs append at bottom of current desktop's tab list (below pinned zone)
- Flyout stays open until explicitly dismissed via X button
- Badge dot disappears immediately when last orphan is processed
- Hamburger button placed left of the search box in the sidebar header
- Custom styled popup menu matching the app's theme (DynamicResource colors, Segoe Fluent Icons, hover highlights)
- Tab right-click context menu uses the same custom style as the hamburger menu
- Menu items show keyboard shortcuts right-aligned in muted text
- "Delete older than N days" uses submenu with presets: 7, 14, 30, 90 days — no custom input
- All bulk deletes use a custom modal confirmation dialog showing count of affected notes
- "Delete all" uses same confirmation dialog as other bulk deletes
- After confirmation, deletion toast appears with "N notes deleted" and single undo (reuses Phase 5 toast)
- Small accent-colored dot (6-8px) at top-right corner of hamburger icon when orphans exist
- Badge checks at startup only — not live-updated mid-session
- Badge disappears immediately when all orphaned sessions are resolved
- "Recover sessions" menu item text uses accent color when orphans exist

### Claude's Discretion
- Exact flyout animation (slide direction, duration, easing)
- Menu item spacing, padding, and separator styling
- Confirmation dialog layout and button placement
- Recovery card visual design details (borders, shadows, spacing)
- Context menu positioning relative to right-click location

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| MENU-01 | Window menu (hamburger icon) with: Recover sessions, Delete all older than, Delete all except pinned, Delete all, separator, Preferences, Exit | WPF Popup control with custom ItemsControl; ToolbarButtonStyle for hamburger button |
| MENU-02 | Recover sessions opens orphaned session panel (badge on menu button when orphaned sessions exist) | Badge via small Ellipse overlay; panel via animated Border in sidebar |
| MENU-03 | "Delete all older than N days" dialog; deletes non-pinned tabs by updated_at; confirmation required | Submenu pattern (nested Popup or ItemsControl); confirmation via custom modal overlay |
| MENU-04 | "Delete all except pinned" and "Delete all" with confirmation; pinned tabs always preserved | Existing DeleteMultipleAsync + ShowToast reuse; confirmation dialog |
| MENU-05 | Bulk deletes show single toast with "N notes deleted" and one undo | Existing toast system from Phase 5 — direct reuse |
| CTXM-01 | Right-click on tab shows: Rename, Pin/Unpin, Clone, Save as TXT, Delete, Delete all below | WPF ContextMenu attached to ListBoxItem; handlers delegate to existing methods |
| CTXM-02 | "Delete all below" deletes non-pinned tabs below this one; pinned tabs silently skipped | Filter _tabs by sort_order > context tab, exclude pinned, use DeleteMultipleAsync |
| ORPH-01 | Sessions with no desktop match become orphaned (stay in DB until user acts) | Already implemented in VirtualDesktopService.MatchSessionsAsync — sessions that fail all 3 tiers stay in DB |
| ORPH-02 | Recovery panel lists orphaned sessions with desktop name, tab count, last updated date | New DatabaseService.GetOrphanedSessionsAsync method; UI cards in flyout panel |
| ORPH-03 | Actions per session: Adopt into current desktop (merge tabs), Open as new window, Delete | Adopt = DatabaseService.MigrateTabsAsync; Open = App.OpenWindowForDesktop; Delete = DatabaseService.DeleteSessionAsync |
| ORPH-04 | Non-blocking badge on menu button when orphaned sessions exist (no dialog on startup) | Badge Ellipse visibility bound to orphan count; checked during MatchSessionsAsync |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WPF Popup | .NET 10 built-in | Custom dropdown menus | Native WPF; StaysOpen=false for auto-dismiss; Placement for positioning |
| WPF ContextMenu | .NET 10 built-in | Tab right-click menu | Native WPF; auto-positions at mouse; integrates with ListBoxItem |
| Storyboard animations | .NET 10 built-in | Flyout slide animation | Already used for toast (Phase 5); consistent approach |
| DynamicResource theming | .NET 10 built-in | Theme-consistent styling | Established pattern from Phase 7; all 12 tokens available |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Segoe Fluent Icons | System font | Menu item icons | Hamburger icon (\uE700), other menu glyphs |
| Microsoft.Data.Sqlite | Project dependency | Orphan queries | Existing dependency; new query methods |

## Architecture Patterns

### Pattern 1: Custom Popup Menu (not WPF Menu control)
**What:** Use WPF Popup containing an ItemsControl or StackPanel with custom-styled buttons/borders rather than the built-in Menu/MenuItem controls. The built-in WPF Menu has complex chrome that's difficult to style consistently with the app's flat design.
**When to use:** Hamburger menu and context menu (CONTEXT.md specifies "custom styled popup menu matching the app's theme")
**Approach:**
- Popup with StaysOpen="False" for auto-dismiss on click-away
- Border with CornerRadius, DropShadow, theme background
- StackPanel of custom menu items (Grid with icon, text, shortcut columns)
- MouseEnter/MouseLeave for hover highlight (same as tab hover pattern)
- Click handler closes popup and executes action

### Pattern 2: Sidebar Flyout Panel
**What:** Animated panel that slides over the sidebar content area when recovery is triggered.
**When to use:** Orphaned session recovery panel (CONTEXT.md: "sidebar flyout that slides out from the left sidebar")
**Approach:**
- Grid overlay in sidebar column with Panel.ZIndex above tab list
- TranslateTransform animation for slide-in (similar to toast slide-up from Phase 5)
- ScrollViewer containing session cards
- Header with title + X close button
- Cards with desktop name, tab count, date, action buttons

### Pattern 3: Confirmation Dialog as Custom Overlay
**What:** Modal confirmation overlay instead of MessageBox. MessageBox doesn't match the app theme and looks jarring.
**When to use:** Bulk delete confirmation (CONTEXT.md: "custom modal confirmation dialog showing count of affected notes")
**Approach:**
- Semi-transparent overlay covering entire window
- Centered card with message, count, and Cancel/Delete buttons
- Keyboard handling: Enter = confirm, Escape = cancel
- IsHitTestVisible=false on elements behind overlay

### Anti-Patterns to Avoid
- **Using WPF Menu/MenuItem for hamburger menu:** Default chrome is Windows-native looking; impossible to match the flat, themed design without extreme template overriding
- **Using MessageBox.Show for confirmations:** Blocks thread, doesn't match theme, looks like a system dialog
- **Using ContextMenu.Items.Add at runtime without clearing:** Can cause duplicate items if context menu is opened multiple times
- **Forgetting StaysOpen=false on Popup:** Menu stays open after clicking, requiring manual close logic

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Menu positioning | Custom screen-bounds logic | Popup.Placement + PlacementTarget | WPF handles multi-monitor, DPI, edge cases |
| Context menu mouse position | Calculate from mouse coords | ContextMenu (auto-positions) | WPF positions at right-click location automatically |
| Slide animation | Manual timer + Transform updates | Storyboard + DoubleAnimation | Smooth, GPU-accelerated, matches Phase 5 toast pattern |
| Focus trapping in modal | Manual PreviewKeyDown everywhere | Overlay IsHitTestVisible pattern | Simpler, proven in WPF |

## Common Pitfalls

### Pitfall 1: Popup Stays Open After Action
**What goes wrong:** Clicking a menu item executes the action but the popup remains visible.
**Why it happens:** StaysOpen property defaults to True for Popup. Also, if you handle the click on a child element, the Popup doesn't know to close.
**How to avoid:** Set StaysOpen="False" AND explicitly set IsOpen=false in click handlers.
**Warning signs:** Popup visible after action completes.

### Pitfall 2: ContextMenu DataContext Loss
**What goes wrong:** Right-clicking a tab shows the menu but the handler doesn't know which tab was clicked.
**Why it happens:** ContextMenu is not in the same visual tree as the ListBoxItem, so Tag/DataContext binding can be lost.
**How to avoid:** Set ContextMenu.Tag to the NoteTab in the right-click handler, or use the PlacementTarget property to find the originating element.
**Warning signs:** NullReferenceException when clicking context menu items.

### Pitfall 3: Orphan Badge Not Updating After Recovery
**What goes wrong:** Badge dot stays visible after all orphans are processed.
**Why it happens:** Badge visibility is set at startup but not re-checked after adopt/open/delete actions.
**How to avoid:** Re-check orphan count after every recovery action and update badge visibility immediately.
**Warning signs:** Badge visible with 0 orphans; badge gone with orphans remaining.

### Pitfall 4: Tab Migration Changes sort_order Conflicts
**What goes wrong:** Adopting orphaned tabs creates sort_order conflicts with existing tabs.
**Why it happens:** Orphaned tabs have sort_order values from their original desktop that may overlap with the current desktop's tabs.
**How to avoid:** When migrating tabs, set their sort_order to continue from the maximum existing sort_order on the target desktop.
**Warning signs:** Tabs appearing in wrong order or swapping positions after adopt.

### Pitfall 5: Confirmation Dialog Keyboard Focus
**What goes wrong:** User can still type in the editor while confirmation dialog is shown.
**Why it happens:** Overlay doesn't capture keyboard focus away from the editor.
**How to avoid:** Set focus to the Cancel button when dialog opens; use FocusManager.
**Warning signs:** Text appearing in editor while dialog is visible.

## Code Examples

### Custom Popup Menu Item Pattern
```xml
<!-- Menu item: Grid with icon, label, and shortcut columns -->
<Border x:Name="MenuItem_Recover" Padding="8,6" Cursor="Hand"
        Background="Transparent"
        MouseEnter="MenuItem_MouseEnter" MouseLeave="MenuItem_MouseLeave"
        MouseLeftButtonDown="MenuItem_Recover_Click">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="24"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="0" Text="&#xE72C;"
                   FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets"
                   FontSize="14" Foreground="{DynamicResource c-toolbar-icon}"
                   VerticalAlignment="Center"/>
        <TextBlock Grid.Column="1" Text="Recover sessions"
                   FontSize="13" Foreground="{DynamicResource c-text-primary}"
                   VerticalAlignment="Center" Margin="4,0,8,0"/>
    </Grid>
</Border>
```

### Orphan Query Pattern
```csharp
/// <summary>
/// Returns sessions that have no matching live desktop (orphaned).
/// Each session includes tab count and most recent updated_at across its notes.
/// </summary>
public static async Task<List<OrphanedSession>> GetOrphanedSessionsAsync(
    IEnumerable<string> liveDesktopGuids)
{
    // Query sessions not in the live desktop set
    // JOIN with notes to get tab count and max updated_at
}
```

### Tab Migration Pattern
```csharp
/// <summary>
/// Moves all notes from sourceGuid to targetGuid.
/// Reassigns sort_order starting after max existing sort_order on target.
/// </summary>
public static async Task MigrateTabsAsync(string sourceGuid, string targetGuid)
{
    // Get max sort_order on target
    // UPDATE notes SET desktop_guid = target, sort_order = max + offset WHERE desktop_guid = source
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| WPF Menu/MenuItem | Custom Popup menus | Project convention (Phase 7 toolbar) | Consistent flat design, full theme control |
| MessageBox.Show | Custom overlay dialogs | Modern WPF apps | Theme consistency, non-blocking feel |
| ContextMenu in XAML | ContextMenu built programmatically | Code-behind pattern | Consistent with project's programmatic UI construction |

## Open Questions

1. **Exit menu item — flush all windows**
   - What we know: Exit must flush all windows and terminate. App.xaml.cs has FlushAndClose path. Multiple windows may be open.
   - What's unclear: Whether App-level shutdown already handles all windows or needs explicit iteration.
   - Recommendation: Iterate WindowRegistry, call FlushAndClose on each, then Environment.Exit(0). Check App.xaml.cs shutdown path.

2. **"Open as new window" for orphaned session**
   - What we know: App.xaml.cs has OpenOrCreateWindowForDesktop. Orphaned session has a desktop_guid that doesn't match any live desktop.
   - What's unclear: Opening a window for a non-existent desktop — the window needs a GUID but there's no live desktop.
   - Recommendation: Create a window using the orphaned session's stored GUID. The window will show the notes but won't be tied to a live desktop (acceptable since user explicitly chose to open it).

## Sources

### Primary (HIGH confidence)
- Project codebase analysis — MainWindow.xaml, MainWindow.xaml.cs, DatabaseService.cs, VirtualDesktopService.cs, ThemeService.cs
- WPF Popup documentation — built-in control behavior verified from .NET documentation
- Phase 5 toast implementation — established animation and overlay patterns
- Phase 7 toolbar implementation — established ToolbarButtonStyle and Segoe Fluent Icons usage

### Secondary (MEDIUM confidence)
- WPF ContextMenu PlacementTarget pattern — standard WPF pattern for identifying right-clicked element

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all WPF built-in controls, same patterns as Phases 5-7
- Architecture: HIGH — extending established code-behind patterns, no new libraries
- Pitfalls: HIGH — documented from real WPF development experience

**Research date:** 2026-03-03
**Valid until:** 2026-04-03 (stable — WPF/.NET 10 is mature)
