# Phase 7: Theming & Toolbar - Context

**Gathered:** 2026-03-03
**Status:** Ready for planning

<domain>
## Phase Boundary

The app renders with Light, Dark, and System themes using a complete set of 10 color tokens (`c-win-bg` through `c-toolbar-icon-hover`), switches instantly without restart, and provides a fully functional toolbar above the editor. All hardcoded colors in existing XAML and code-behind are replaced with token references. Preferences dialog and menus are separate phases.

</domain>

<decisions>
## Implementation Decisions

### Color palette
- Light mode: warm white backgrounds (#FAFAFA/#F5F5F5), gentle gray borders — quality notebook feel
- Dark mode: true dark (#1E1E1E editor bg, ~#252526 sidebar) — VS Code style, high contrast for low-light
- Accent color: keep existing #2196F3 blue — already used for toast link, active tab indicator, search highlight
- Active tab indicator: left-edge accent bar (3px colored bar on left side) — VS Code / sidebar app style

### Theme switching
- System theme: instant follow via SystemEvents.UserPreferenceChanged — switches in real-time when Windows changes
- Theme scope: global across all JoJot windows (stored in preferences table, not per-session)
- Default on first launch: System (follows whatever Windows is set to)
- Transition: instant ResourceDictionary swap, no animation

### Toolbar layout
- Button grouping: spec order with thin vertical line separators — Undo, Redo | Pin, Clone | Copy, Paste | Save as TXT | spacer | Delete
- Toolbar height: compact (28-32px) — maximize editor space
- Toolbar span: editor column only (column 2) — tab panel keeps its own header with search + new tab button
- Button states: visually disable (gray out) when action is unavailable — Undo grays when nothing to undo, Pin shows Unpin state, etc.

### Toolbar icon style
- Icon approach: Segoe Fluent Icons — Windows 11 built-in, no external dependencies, scales perfectly, all needed glyphs exist
- Labels: icons only, tooltips on hover (600ms delay per TOOL-03) with action name + keyboard shortcut
- Default icon color: muted gray (#666 light / #AAA dark) — subtle until hovered, keeps toolbar visually light
- Hover behavior: subtle rounded-rectangle background appears behind icon on hover — matches Windows 11 behavior and existing tab HoverBrush pattern
- Delete button: right-aligned via flex spacer, #e74c3c at 70% opacity, 100% on hover (per TOOL-02)

### Claude's Discretion
- Exact hex values for the 10 color tokens (within the warm-white / true-dark direction)
- Specific Segoe Fluent Icons glyph codepoints for each button
- Toolbar button padding and separator styling
- Disabled state opacity level
- How to structure the ResourceDictionary files (single file vs split Light.xaml/Dark.xaml)

</decisions>

<specifics>
## Specific Ideas

- Active tab should have a left-edge accent bar like VS Code sidebar, not a background highlight
- Toolbar buttons should feel like Windows 11 native — Segoe Fluent Icons with subtle hover backgrounds
- The delete button standing alone on the right (red, muted) is a deliberate design choice — separation signals caution
- Tab panel hover behavior already uses #F0F0F0 — toolbar hover should be consistent with this in light mode

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AccentBrush` (#2196F3), `MutedTextBrush` (#888888), `HoverBrush` (#F0F0F0): static brushes in MainWindow.xaml.cs — will be replaced with DynamicResource token references
- `App.xaml` has empty `Application.Resources` block — ready for ResourceDictionary theme infrastructure
- Toast overlay already uses #333333 bg with white text — represents implicit dark-on-dark pattern to preserve

### Established Patterns
- All UI is in MainWindow.xaml with code-behind in MainWindow.xaml.cs (single window, partial class pattern)
- Colors are currently hardcoded in ~8 XAML locations and 3 C# static brush fields (all marked "Phase 7 replaces")
- Tab items are built programmatically in code-behind (CreateTabItem method) — brush references will need to use FindResource/DynamicResource
- Window.Resources contains a custom ListBoxItem template — will need theme-aware colors

### Integration Points
- `MainWindow.xaml` Grid column 2 needs a toolbar row inserted above the editor
- `App.xaml.cs` OnAppStartup needs theme initialization (read preference, detect system theme, load correct ResourceDictionary)
- `DatabaseService` likely needs a preferences table or similar for persisting theme choice
- All existing `Background=`, `Foreground=`, `BorderBrush=` attributes in XAML need replacement with `{DynamicResource TokenName}`
- Code-behind static brushes need replacement with resource lookups that update on theme switch

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 07-theming-toolbar*
*Context gathered: 2026-03-03*
