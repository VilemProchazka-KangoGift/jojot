namespace JoJot.Models;

/// <summary>
/// Captured geometry for a desktop's window. Persisted to app_state.
/// Left/Top/Width/Height are in workspace coordinates from WINDOWPLACEMENT
/// (consistent with GetWindowPlacement/SetWindowPlacement coordinate system).
/// </summary>
public sealed record WindowGeometry(
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized);
