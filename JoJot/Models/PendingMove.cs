namespace JoJot.Models;

/// <summary>
/// Represents a pending window drag between desktops for crash recovery.
/// Written to the pending_moves table when a drag is detected, deleted after resolution.
/// </summary>
public sealed record PendingMove(long Id, string WindowId, string FromDesktop, string? ToDesktop, string DetectedAt);
