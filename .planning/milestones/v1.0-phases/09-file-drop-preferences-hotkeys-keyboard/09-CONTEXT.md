# Phase 9: File Drop, Preferences, Hotkeys & Keyboard - Context

**Gathered:** 2026-03-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Users can drag text files into JoJot to open them as tabs, configure all preferences live via a slide-in panel, activate JoJot from anywhere with a global hotkey, and operate the app entirely from the keyboard. Creating new file formats, cloud sync, or new editor capabilities are out of scope.

</domain>

<decisions>
## Implementation Decisions

### File drop visual feedback
- Full overlay covering the content area during drag-over: semi-transparent with "Drop file here" message and icon
- Overlay appears on DragEnter, disappears on DragLeave/Drop
- Window border highlight (DROP-05) handled by the overlay itself

### File drop error handling
- Errors displayed via the existing toast notification bar (reuse current toast pattern)
- Multiple invalid files in a single drop: one combined toast (e.g., "2 files opened, 1 skipped (binary content)")
- Toast auto-dismisses after 4 seconds per spec (DROP-06)

### File drop tab behavior
- When dropping multiple files: last valid file becomes active tab, others open in background
- When dropping a single file: that tab becomes active immediately
- Tab name set to filename including extension (DROP-04)

### Preferences dialog style
- Right-side slide-in panel within the main window (not a modal dialog or overlay)
- Panel slides in from the right edge; tab list remains visible on the left
- Opened from the existing hamburger menu "Preferences" item (MenuPreferences_Click stub)

### Preferences layout
- Settings grouped into sections:
  - **Appearance**: Theme toggle (Light/System/Dark), Font size (+/- buttons, 8-32pt range, reset to 13pt)
  - **Editor**: Autosave debounce interval (200-2000ms, default 500ms)
  - **Shortcuts**: Global hotkey picker
- All changes apply live, no restart required (PREF-01)

### Hotkey picker
- Record mode: user clicks "Record" button, presses desired key combination, it's captured and displayed
- Visual feedback shows the recorded combo in the picker field
- Default: Win+Shift+N

### Global hotkey behavior
- Toggle behavior: hotkey focuses JoJot if hidden/unfocused, minimizes if already focused
- Activates the JoJot window belonging to the current virtual desktop
- If no window exists for current desktop: create one and restore last session for that desktop
- Uses Win32 RegisterHotKey API

### Global hotkey conflict handling
- If the hotkey is already registered by another app, show a toast notification on startup
- Toast message: "Global hotkey [combo] is in use by another app. Change it in Preferences."
- Log the conflict via LogService
- App continues without global hotkey; user can change it in Preferences

### Ctrl+F context-dependent behavior
- When editor is focused: Ctrl+F opens an in-editor find bar (search within the active note)
- When tab list is focused: Ctrl+F focuses the tab search box (existing behavior)

### Font size shortcuts
- Ctrl+= increase, Ctrl+- decrease, Ctrl+0 reset to 13pt (KEYS-02)
- Ctrl+Scroll over editor area changes font size; over tab list scrolls normally (KEYS-03)
- All font size changes are persistent (saved to preferences database immediately)
- Brief auto-dismissing tooltip shows current font size (e.g., "14pt") near the editor (~1s)

### Keyboard shortcuts reference
- Help overlay accessible via Ctrl+? showing all keyboard shortcuts as a reference card
- Existing tooltips on toolbar buttons and context menu hints continue to show shortcuts

### Claude's Discretion
- Slide-in panel animation timing and easing
- Drop overlay visual design (icon choice, opacity, animation)
- In-editor find bar design and behavior details
- Help overlay layout and styling
- Font size tooltip positioning and animation
- Exact section spacing and typography in preferences panel

</decisions>

<specifics>
## Specific Ideas

- Preferences panel should feel integrated, not like a separate dialog — slide-in from the right keeps the user in context
- Global hotkey should feel like a "summon" action — toggle show/hide for quick access
- Font size tooltip similar to how VS Code shows zoom level briefly
- Help overlay as a quick-reference card, not a full help system

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `DatabaseService.GetPreferenceAsync/SetPreferenceAsync`: Preferences table already exists with key/value storage — use for theme, font_size, debounce_ms, hotkey settings
- `ThemeService` (Light/Dark/System + `SetThemeAsync`): Theme switching fully wired — preferences panel just needs to call existing methods
- `AutosaveService.DebounceMs` property: Already configurable in code — wire to preferences UI
- `MenuPreferences_Click` stub in MainWindow.xaml.cs:2125: Menu item and handler exist, ready for implementation
- Existing toast notification pattern in MainWindow: Reuse for file drop errors and hotkey conflict alerts
- `Window_PreviewKeyDown` handler: 12+ shortcuts already implemented — add font size shortcuts (Ctrl+=/-/0) and Ctrl+? here

### Established Patterns
- Theme system: `Themes/LightTheme.xaml` + `Themes/DarkTheme.xaml` with DynamicResource tokens — new UI (preferences panel, drop overlay, find bar, help overlay) should use these tokens
- PreviewKeyDown pattern: All keyboard handling goes through `Window_PreviewKeyDown` with explicit `e.Handled = true` — follow same pattern for new shortcuts
- Toast notifications: Existing auto-dismiss pattern in MainWindow — reuse for drop errors and hotkey conflicts
- Services pattern: Static service classes in `JoJot/Services/` — new services (HotkeyService, FileDropService) should follow same pattern

### Integration Points
- `MainWindow.xaml`: Add AllowDrop="True", DragEnter/DragLeave/Drop event handlers, preferences slide-in panel XAML, find bar XAML, help overlay XAML
- `MainWindow.xaml.cs`: Wire file drop handlers, extend Window_PreviewKeyDown with font size and Ctrl+? shortcuts, add Ctrl+F context routing
- `App.xaml.cs`: Register global hotkey on startup, unregister on shutdown
- `JoJot.csproj`: May need P/Invoke for RegisterHotKey/UnregisterHotKey (Win32 interop)

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 09-file-drop-preferences-hotkeys-keyboard*
*Context gathered: 2026-03-03*
