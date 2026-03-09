using System.Windows.Threading;

namespace JoJot.Services;

/// <summary>
/// Abstraction over a debounce timer for testability.
/// Production code uses <see cref="DispatcherDebounceTimer"/>; tests use a synchronous fake.
/// </summary>
public interface IDebounceTimer
{
    /// <summary>Whether the timer is currently running.</summary>
    bool IsEnabled { get; }

    /// <summary>Interval between start and tick.</summary>
    TimeSpan Interval { get; set; }

    /// <summary>Fired when the interval elapses.</summary>
    event EventHandler Tick;

    /// <summary>Starts (or restarts) the timer.</summary>
    void Start();

    /// <summary>Stops the timer without firing.</summary>
    void Stop();
}

/// <summary>
/// Production implementation backed by WPF <see cref="DispatcherTimer"/>.
/// </summary>
public sealed class DispatcherDebounceTimer : IDebounceTimer
{
    private readonly DispatcherTimer _timer = new();

    /// <inheritdoc />
    public bool IsEnabled => _timer.IsEnabled;

    /// <inheritdoc />
    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    /// <inheritdoc />
    public event EventHandler Tick
    {
        add => _timer.Tick += value;
        remove => _timer.Tick -= value;
    }

    /// <inheritdoc />
    public void Start() => _timer.Start();

    /// <inheritdoc />
    public void Stop() => _timer.Stop();
}
