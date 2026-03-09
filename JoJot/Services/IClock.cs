namespace JoJot.Services;

/// <summary>
/// Abstraction over system clock for testability.
/// Production code uses <see cref="SystemClock"/>; tests inject a controllable implementation.
/// </summary>
public interface IClock
{
    /// <summary>Gets the current local date and time.</summary>
    DateTime Now { get; }

    /// <summary>Gets the current UTC date and time.</summary>
    DateTime UtcNow { get; }
}

/// <summary>
/// Default clock implementation that delegates to <see cref="DateTime"/>.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly SystemClock Instance = new();

    /// <inheritdoc />
    public DateTime Now => DateTime.Now;

    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;
}
