namespace JoJot.Themes;

/// <summary>
/// Constants for theme resource dictionary keys. Use these instead of string literals
/// to catch typos at compile time and enable refactoring.
/// </summary>
public static class ThemeKeys
{
    // Window & Layout
    public const string WindowBackground = "c-win-bg";
    public const string SidebarBackground = "c-sidebar-bg";
    public const string EditorBackground = "c-editor-bg";
    public const string Border = "c-border";

    // Text
    public const string TextPrimary = "c-text-primary";
    public const string TextMuted = "c-text-muted";

    // Interactive
    public const string Accent = "c-accent";
    public const string HoverBackground = "c-hover-bg";
    public const string SelectedBackground = "c-selected-bg";

    // Toolbar
    public const string ToolbarIcon = "c-toolbar-icon";
    public const string ToolbarIconHover = "c-toolbar-icon-hover";

    // Containers
    public const string TabBackground = "c-tab-bg";

    // Supplementary
    public const string ToastBackground = "c-toast-bg";
    public const string ToastForeground = "c-toast-fg";

    // Overlays & Modals
    public const string OverlayBackground = "c-overlay-bg";
    public const string TooltipBackground = "c-tooltip-bg";
    public const string Danger = "c-danger";

    // Find match highlights
    public const string FindMatchBackground = "c-find-match-bg";
    public const string FindMatchActiveBackground = "c-find-match-active-bg";
}
