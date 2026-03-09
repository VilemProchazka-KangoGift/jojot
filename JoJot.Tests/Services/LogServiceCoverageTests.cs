using JoJot.Services;
using Serilog.Events;

namespace JoJot.Tests.Services;

/// <summary>
/// Tests for LogService covering InitializeNoop, level control, and structured template overloads.
/// </summary>
public class LogServiceCoverageTests
{
    [Fact]
    public void InitializeNoop_DoesNotThrow()
    {
        LogService.InitializeNoop();
    }

    [Fact]
    public void SetMinimumLevel_And_GetMinimumLevel_RoundTrips()
    {
        LogService.InitializeNoop();

        LogService.SetMinimumLevel(LogEventLevel.Debug);
        LogService.GetMinimumLevel().Should().Be(LogEventLevel.Debug);

        LogService.SetMinimumLevel(LogEventLevel.Warning);
        LogService.GetMinimumLevel().Should().Be(LogEventLevel.Warning);

        // Restore default
        LogService.SetMinimumLevel(LogEventLevel.Information);
    }

    [Fact]
    public void StructuredLogging_AllOverloads_DoNotThrow()
    {
        LogService.InitializeNoop();

        // Simple messages
        LogService.Debug("debug msg");
        LogService.Info("info msg");
        LogService.Warn("warn msg");
        LogService.Error("error msg");

        // 1 param
        LogService.Debug("debug {A}", 1);
        LogService.Info("info {A}", 1);
        LogService.Warn("warn {A}", 1);
        LogService.Error("error {A}", 1);

        // 2 params
        LogService.Debug("debug {A} {B}", 1, 2);
        LogService.Info("info {A} {B}", 1, 2);
        LogService.Warn("warn {A} {B}", 1, 2);
        LogService.Error("error {A} {B}", 1, 2);

        // 3 params
        LogService.Debug("debug {A} {B} {C}", 1, 2, 3);
        LogService.Info("info {A} {B} {C}", 1, 2, 3);
        LogService.Error("error {A} {B} {C}", 1, 2, 3);

        // 4 params
        LogService.Info("info {A} {B} {C} {D}", 1, 2, 3, 4);

        // 5 params
        LogService.Info("info {A} {B} {C} {D} {E}", 1, 2, 3, 4, 5);

        // With exceptions
        var ex = new InvalidOperationException("test");
        LogService.Warn("warn {A}", 1, ex);
        LogService.Error("error {A}", 1, ex);
        LogService.Warn("warn", ex);
        LogService.Error("error", ex);
    }

    [Fact]
    public void Shutdown_DoesNotThrow()
    {
        LogService.InitializeNoop();
        LogService.Shutdown();
        // Re-initialize for other tests
        LogService.InitializeNoop();
    }
}
