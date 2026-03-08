namespace JoJot.Models;

/// <summary>
/// Entity for the app_state table. Stores per-desktop session info and window geometry.
/// </summary>
public class AppState
{
    public long Id { get; set; }
    public string DesktopGuid { get; set; } = "";
    public string? DesktopName { get; set; }
    public int? DesktopIndex { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public long? ActiveTabId { get; set; }
    public double? ScrollOffset { get; set; }
    public string? WindowState { get; set; }
}
