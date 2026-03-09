using JoJot.Services;

namespace JoJot.Tests.Helpers;

/// <summary>
/// Controllable clock for deterministic tests.
/// Set <see cref="Now"/> and <see cref="UtcNow"/> directly, or use <see cref="Advance"/> to move time forward.
/// </summary>
public sealed class TestClock : IClock
{
    public DateTime Now { get; set; } = new(2025, 6, 15, 10, 30, 0);
    public DateTime UtcNow { get; set; } = new(2025, 6, 15, 14, 30, 0, DateTimeKind.Utc);

    /// <summary>Advances both Now and UtcNow by the specified duration.</summary>
    public void Advance(TimeSpan duration)
    {
        Now += duration;
        UtcNow += duration;
    }
}
