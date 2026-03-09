using JoJot.Services;

namespace JoJot.Tests.Helpers;

/// <summary>
/// Synchronous fake timer for testing <see cref="AutosaveService"/>.
/// Call <see cref="Fire"/> to simulate a tick.
/// </summary>
public sealed class TestTimer : IDebounceTimer
{
    public bool IsEnabled { get; private set; }
    public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(500);

    public event EventHandler? Tick;

    public void Start() => IsEnabled = true;

    public void Stop() => IsEnabled = false;

    /// <summary>Simulates a timer tick. Raises the <see cref="Tick"/> event.</summary>
    public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
}
