# Phase 7: Theming & Toolbar - Research

**Researched:** 2026-03-03
**Domain:** WPF theming (ResourceDictionary, DynamicResource, system theme detection) and toolbar UI
**Confidence:** HIGH

## Summary

WPF .NET 9+ includes a built-in `ThemeMode` property for Fluent light/dark/system theming. However, JoJot requires 10 custom color tokens with specific hex values (warm white light theme, true dark theme). The optimal approach is **custom ResourceDictionary files** (LightTheme.xaml, DarkTheme.xaml) containing the 10 color token brushes, swapped at runtime via `Application.Current.Resources.MergedDictionaries`. All existing hardcoded colors in XAML and C# code-behind must be replaced with `{DynamicResource TokenName}` references. System theme detection uses `Microsoft.Win32.SystemEvents.UserPreferenceChanged` to follow Windows dark/light mode in real-time.

The toolbar uses Segoe Fluent Icons (built-in on Windows 11) for icon buttons, rendered as `TextBlock` elements with the font family set to "Segoe Fluent Icons". Each button is a standard WPF `Button` with a `TextBlock` content displaying the glyph codepoint.

**Primary recommendation:** Create two XAML ResourceDictionary files with 10 color token brushes each, a ThemeService static class to manage switching, and replace all hardcoded colors with DynamicResource references. Build the toolbar as a Grid row above the editor in column 2.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Light mode: warm white backgrounds (#FAFAFA/#F5F5F5), gentle gray borders
- Dark mode: true dark (#1E1E1E editor bg, ~#252526 sidebar) — VS Code style
- Accent color: keep existing #2196F3 blue
- Active tab indicator: left-edge accent bar (3px colored bar on left side)
- System theme: instant follow via SystemEvents.UserPreferenceChanged
- Theme scope: global across all JoJot windows (stored in preferences table)
- Default on first launch: System (follows Windows)
- Transition: instant ResourceDictionary swap, no animation
- Button grouping: Undo, Redo | Pin, Clone | Copy, Paste | Save as TXT | spacer | Delete
- Toolbar height: compact (28-32px)
- Toolbar span: editor column only (column 2)
- Button states: visually disable (gray out) when action unavailable
- Icon approach: Segoe Fluent Icons
- Labels: icons only, tooltips on hover (600ms delay)
- Default icon color: muted gray (#666 light / #AAA dark)
- Hover behavior: subtle rounded-rectangle background on hover
- Delete button: right-aligned via flex spacer, #e74c3c at 70% opacity, 100% on hover

### Claude's Discretion
- Exact hex values for the 10 color tokens (within the warm-white / true-dark direction)
- Specific Segoe Fluent Icons glyph codepoints for each button
- Toolbar button padding and separator styling
- Disabled state opacity level
- How to structure the ResourceDictionary files (single file vs split Light.xaml/Dark.xaml)

### Deferred Ideas (OUT OF SCOPE)
None
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| THME-01 | Three themes: Light, Dark, System (follows Windows app mode) | ThemeService with enum, SystemEvents.UserPreferenceChanged for System mode |
| THME-02 | Instant theme switching via WPF ResourceDictionary swap | MergedDictionaries.Clear() + Add() pattern; DynamicResource for all colors |
| THME-03 | System theme re-evaluates on SystemEvents.UserPreferenceChanged | Microsoft.Win32.SystemEvents.UserPreferenceChanged + Registry check |
| THME-04 | 10 color tokens defined for both light and dark themes | Two ResourceDictionary XAML files with SolidColorBrush resources |
| TOOL-01 | Toolbar above editor with specified button layout | Grid row 0 in column 2 Grid; StackPanel with separators |
| TOOL-02 | Delete button right-aligned, #e74c3c at 70% opacity, hover 1.0 | DockPanel LastChildFill + right-dock delete button with opacity trigger |
| TOOL-03 | Tooltip delay 600ms with shortcut info | ToolTipService.InitialShowDelay="600" + descriptive tooltip strings |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WPF ResourceDictionary | .NET 10 built-in | Theme color token definitions | Native WPF theming mechanism |
| DynamicResource | .NET 10 built-in | Runtime-updating color references | Required for live theme switching |
| SystemEvents.UserPreferenceChanged | .NET built-in | Windows dark/light mode detection | Official Microsoft API for theme change notifications |
| Segoe Fluent Icons | Windows 11 built-in | Toolbar icon glyphs | Microsoft's recommended icon font for Windows 11 apps |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Win32.Registry | .NET built-in | Read Windows AppsUseLightTheme setting | Detecting current system theme on startup |
| ToolTipService | WPF built-in | Tooltip delay configuration | 600ms delay per TOOL-03 |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Custom ResourceDictionary | .NET 9 ThemeMode=System | ThemeMode applies Fluent theme but doesn't support custom token colors — we need both custom tokens AND system follow |
| Segoe Fluent Icons | SVG paths / DrawingImage | Font glyphs are simpler, scale perfectly, single FontFamily declaration |
| SystemEvents.UserPreferenceChanged | WMI WqlEventQuery | SystemEvents is simpler and sufficient; WMI adds unnecessary complexity |

## Architecture Patterns

### Recommended File Structure
```
JoJot/
├── Themes/
│   ├── LightTheme.xaml      # Light mode color token ResourceDictionary
│   └── DarkTheme.xaml        # Dark mode color token ResourceDictionary
├── Services/
│   └── ThemeService.cs       # Static theme management service
├── App.xaml                  # MergedDictionaries reference (initial theme)
├── App.xaml.cs               # Theme initialization on startup
├── MainWindow.xaml           # All hardcoded colors → DynamicResource
└── MainWindow.xaml.cs        # Static brushes → FindResource calls
```

### Pattern 1: ResourceDictionary Swap for Theme Switching
**What:** Clear and replace the theme ResourceDictionary in MergedDictionaries at runtime
**When to use:** When switching between Light and Dark themes
**Example:**
```csharp
// ThemeService.cs
public static class ThemeService
{
    public enum AppTheme { Light, Dark, System }

    private static AppTheme _currentSetting = AppTheme.System;

    public static void ApplyTheme(AppTheme theme)
    {
        _currentSetting = theme;
        var effectiveTheme = theme == AppTheme.System ? GetSystemTheme() : theme;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        // Remove existing theme dictionary (always at index 0)
        if (dictionaries.Count > 0)
            dictionaries.RemoveAt(0);

        var uri = effectiveTheme == AppTheme.Dark
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

        dictionaries.Insert(0, new ResourceDictionary { Source = uri });
    }

    private static AppTheme GetSystemTheme()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var value = key?.GetValue("AppsUseLightTheme");
        return value is int i && i == 0 ? AppTheme.Dark : AppTheme.Light;
    }
}
```

### Pattern 2: System Theme Change Detection
**What:** Listen for Windows theme changes and re-apply if in System mode
**When to use:** When user has selected "System" theme preference
**Example:**
```csharp
// In App.xaml.cs OnAppStartup or ThemeService.Initialize
Microsoft.Win32.SystemEvents.UserPreferenceChanged += (sender, args) =>
{
    if (args.Category == Microsoft.Win32.UserPreferenceCategory.General)
    {
        if (_currentSetting == AppTheme.System)
        {
            Application.Current.Dispatcher.InvokeAsync(() => ApplyTheme(AppTheme.System));
        }
    }
};
```

### Pattern 3: DynamicResource for All Colors
**What:** Replace every hardcoded color with `{DynamicResource TokenName}`
**When to use:** Every color reference in XAML and code-behind
**Example:**
```xml
<!-- XAML: Before -->
<Border Background="White">
<!-- XAML: After -->
<Border Background="{DynamicResource c-sidebar-bg}">

<!-- Code-behind: Before -->
private static readonly SolidColorBrush AccentBrush = new(...);
<!-- Code-behind: After -->
var accentBrush = (SolidColorBrush)FindResource("c-accent");
```

### Anti-Patterns to Avoid
- **Using StaticResource for theme colors:** StaticResource evaluates once at load-time and will NOT update when theme changes. Must use DynamicResource everywhere.
- **Setting ThemeMode AND custom dictionaries:** ThemeMode applies the full Fluent theme which overrides custom styles. Since we need custom token colors, use custom ResourceDictionaries only.
- **Creating new SolidColorBrush in code-behind:** Instead of `new SolidColorBrush(...)`, use `FindResource("token-name")` to get the DynamicResource-managed brush.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| System theme detection | Custom Win32 API calls | SystemEvents.UserPreferenceChanged + Registry | Built-in, reliable, handles all Windows versions |
| Icon rendering | SVG parsing, custom drawing | Segoe Fluent Icons font | Zero dependencies, perfect scaling, Windows 11 native |
| Tooltip delay | Custom timer-based tooltip | ToolTipService.InitialShowDelay | WPF built-in property, works on any element |
| Theme persistence | Custom file-based config | DatabaseService preferences table (DATA-06) | Already specified in requirements, reuses existing infrastructure |

## Common Pitfalls

### Pitfall 1: StaticResource vs DynamicResource
**What goes wrong:** Colors don't update when theme switches
**Why it happens:** StaticResource is resolved once at XAML load time
**How to avoid:** Use `{DynamicResource ...}` for ALL color tokens. Grep for `StaticResource` + color token names after implementation.
**Warning signs:** Some elements stay in old theme colors after switching

### Pitfall 2: Code-Behind Brush References Not Updating
**What goes wrong:** Static readonly brush fields in C# don't change when theme changes
**Why it happens:** `new SolidColorBrush(...)` creates a fixed brush; ResourceDictionary swap doesn't affect it
**How to avoid:** Replace all `private static readonly SolidColorBrush` with `FindResource("token")` calls at point of use, or use `SetResourceReference` for element properties
**Warning signs:** Tab items or programmatic UI elements retain old theme colors

### Pitfall 3: SystemEvents.UserPreferenceChanged fires on Non-Theme Changes
**What goes wrong:** Theme re-applies unnecessarily when user changes non-theme Windows settings
**Why it happens:** UserPreferenceChanged fires for many preference categories, not just theme
**How to avoid:** Check `args.Category == UserPreferenceCategory.General` before reacting. The theme change fires under "General" category.
**Warning signs:** Flickering or unnecessary dictionary swaps

### Pitfall 4: MergedDictionaries Order Matters
**What goes wrong:** Theme tokens get overridden by other resource dictionaries
**Why it happens:** Later entries in MergedDictionaries take precedence
**How to avoid:** Always insert theme dictionary at index 0, and ensure no other dictionaries redefine the same keys
**Warning signs:** Some tokens show wrong colors despite correct theme file

### Pitfall 5: Segoe Fluent Icons Not Available on Windows 10
**What goes wrong:** Icons show as empty squares on Windows 10
**Why it happens:** Segoe Fluent Icons is Windows 11 only
**How to avoid:** Use fallback font family: `FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets"`. Most glyphs exist in both fonts.
**Warning signs:** Missing icons on older Windows versions

### Pitfall 6: Thread Safety for SystemEvents Callback
**What goes wrong:** UI update from background thread throws InvalidOperationException
**Why it happens:** SystemEvents.UserPreferenceChanged may fire on a non-UI thread
**How to avoid:** Always use `Dispatcher.InvokeAsync()` to marshal ResourceDictionary changes to the UI thread
**Warning signs:** Crash when Windows theme changes while app is running

## Code Examples

### 10 Color Tokens — Light Theme
```xml
<!-- Themes/LightTheme.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Window & Layout -->
    <SolidColorBrush x:Key="c-win-bg" Color="#FAFAFA"/>
    <SolidColorBrush x:Key="c-sidebar-bg" Color="#F5F5F5"/>
    <SolidColorBrush x:Key="c-editor-bg" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="c-border" Color="#E0E0E0"/>

    <!-- Text -->
    <SolidColorBrush x:Key="c-text-primary" Color="#1A1A1A"/>
    <SolidColorBrush x:Key="c-text-muted" Color="#888888"/>

    <!-- Interactive -->
    <SolidColorBrush x:Key="c-accent" Color="#2196F3"/>
    <SolidColorBrush x:Key="c-hover-bg" Color="#F0F0F0"/>

    <!-- Toolbar -->
    <SolidColorBrush x:Key="c-toolbar-icon" Color="#666666"/>
    <SolidColorBrush x:Key="c-toolbar-icon-hover" Color="#333333"/>
</ResourceDictionary>
```

### 10 Color Tokens — Dark Theme
```xml
<!-- Themes/DarkTheme.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Window & Layout -->
    <SolidColorBrush x:Key="c-win-bg" Color="#1E1E1E"/>
    <SolidColorBrush x:Key="c-sidebar-bg" Color="#252526"/>
    <SolidColorBrush x:Key="c-editor-bg" Color="#1E1E1E"/>
    <SolidColorBrush x:Key="c-border" Color="#3C3C3C"/>

    <!-- Text -->
    <SolidColorBrush x:Key="c-text-primary" Color="#D4D4D4"/>
    <SolidColorBrush x:Key="c-text-muted" Color="#808080"/>

    <!-- Interactive -->
    <SolidColorBrush x:Key="c-accent" Color="#2196F3"/>
    <SolidColorBrush x:Key="c-hover-bg" Color="#2D2D2D"/>

    <!-- Toolbar -->
    <SolidColorBrush x:Key="c-toolbar-icon" Color="#AAAAAA"/>
    <SolidColorBrush x:Key="c-toolbar-icon-hover" Color="#FFFFFF"/>
</ResourceDictionary>
```

### Segoe Fluent Icons Glyph Codepoints
```
Undo:       \uE7A7
Redo:       \uE7A6
Pin:        \uE718
Unpin:      \uE77A
Clone:      \uE8C8 (using Copy glyph — no dedicated clone icon)
Copy:       \uE8C8
Paste:      \uE77F
Save:       \uE74E (FloppyDisk/Save)
Delete:     \uE74D
```

Note: For Clone, use the "Copy" glyph (\uE8C8) since there is no dedicated "clone" icon in Segoe Fluent Icons. Alternative: \uE8D7 (BranchFork) could also work.

### Toolbar Button Pattern
```xml
<Button ToolTip="Undo (Ctrl+Z)" ToolTipService.InitialShowDelay="600"
        Background="Transparent" BorderThickness="0"
        Width="28" Height="28" Padding="0"
        IsEnabled="{Binding CanUndo}">
    <TextBlock Text="&#xE7A7;" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets"
               FontSize="14" Foreground="{DynamicResource c-toolbar-icon}"
               HorizontalAlignment="Center" VerticalAlignment="Center"/>
</Button>
```

### System Theme Detection at Startup
```csharp
public static AppTheme GetSystemTheme()
{
    try
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var value = key?.GetValue("AppsUseLightTheme");
        return value is int i && i == 0 ? AppTheme.Dark : AppTheme.Light;
    }
    catch
    {
        return AppTheme.Light; // Safe default
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Custom theme dictionaries only | .NET 9 ThemeMode property | .NET 9 (Nov 2024) | Can use ThemeMode=System for Fluent theme, but custom tokens still need ResourceDictionary files |
| SystemParameters.HighContrastMode | SystemEvents.UserPreferenceChanged | Long-standing | UserPreferenceChanged is the standard way to detect theme changes |
| Segoe MDL2 Assets | Segoe Fluent Icons | Windows 11 | Newer icon set; MDL2 works as fallback for Windows 10 |

## Open Questions

1. **ThemeMode + Custom Dictionaries Interaction**
   - What we know: ThemeMode=System applies full Fluent theme. Custom ResourceDictionaries can override specific keys.
   - What's unclear: Whether combining ThemeMode with custom color tokens causes style conflicts on standard WPF controls.
   - Recommendation: Use custom ResourceDictionaries ONLY (no ThemeMode) to maintain full control. The 10 custom tokens plus explicit control styling gives us the exact look specified in CONTEXT.md without Fluent theme interference.

2. **Preferences Table Availability**
   - What we know: DATA-06 specifies a preferences table, and the schema was created in Phase 1.
   - What's unclear: Whether the preferences table already has a "theme" key.
   - Recommendation: ThemeService should read/write theme preference via DatabaseService. If "theme" key doesn't exist, default to "system".

## Sources

### Primary (HIGH confidence)
- [WPF .NET 9 What's New](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90) — ThemeMode API, Fluent theme details
- [Segoe Fluent Icons](https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font) — Glyph codepoints
- [dotnet/wpf Fluent theme docs](https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md) — ResourceDictionary approach

### Secondary (MEDIUM confidence)
- [WPF Themes and Skins Guide](https://michaelscodingspot.com/wpf-complete-guide-themes-skins/) — DynamicResource vs StaticResource patterns
- [Thomas Claudius Huber - WPF .NET 9 Theming](https://www.thomasclaudiushuber.com/2025/02/21/wpf-in-net-9-0-windows-11-theming/) — ThemeMode usage patterns

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — WPF ResourceDictionary/DynamicResource is the canonical approach, well-documented
- Architecture: HIGH — Custom theme dictionaries + SystemEvents is established pattern used across WPF ecosystem
- Pitfalls: HIGH — Well-known issues (StaticResource vs DynamicResource, thread safety) documented extensively

**Research date:** 2026-03-03
**Valid until:** 2026-04-03 (stable — WPF theming patterns don't change frequently)
