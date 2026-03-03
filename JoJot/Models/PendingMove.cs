namespace JoJot.Models
{
    /// <summary>
    /// Represents a pending window drag between desktops (DRAG-09 crash recovery).
    /// Written to pending_moves table when drag detected, deleted after resolution.
    /// </summary>
    public sealed record PendingMove(long Id, string WindowId, string FromDesktop, string? ToDesktop, string DetectedAt);
}
