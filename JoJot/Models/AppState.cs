namespace JoJot.Models;

/// <summary>
/// Entity for the app_state table. Stores per-desktop session info and window geometry.
/// </summary>
public class AppState
{
    /// <summary>
    /// Primary key (auto-incremented row ID).
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// GUID string identifying the virtual desktop this state belongs to.
    /// </summary>
    public string DesktopGuid { get; set; } = "";

    /// <summary>
    /// Human-readable name of the virtual desktop, if available.
    /// </summary>
    public string? DesktopName { get; set; }

    /// <summary>
    /// Zero-based positional index of the virtual desktop.
    /// </summary>
    public int? DesktopIndex { get; set; }

    /// <summary>
    /// Saved window left position in workspace coordinates.
    /// </summary>
    public double? WindowLeft { get; set; }

    /// <summary>
    /// Saved window top position in workspace coordinates.
    /// </summary>
    public double? WindowTop { get; set; }

    /// <summary>
    /// Saved window width in workspace coordinates.
    /// </summary>
    public double? WindowWidth { get; set; }

    /// <summary>
    /// Saved window height in workspace coordinates.
    /// </summary>
    public double? WindowHeight { get; set; }

    /// <summary>
    /// ID of the tab that was active when the session was last saved.
    /// </summary>
    public long? ActiveTabId { get; set; }

    /// <summary>
    /// Vertical scroll offset of the text editor at save time.
    /// </summary>
    public double? ScrollOffset { get; set; }

    /// <summary>
    /// Serialized window state (Normal, Maximized, etc.) for restore.
    /// </summary>
    public string? WindowState { get; set; }
}
