using JoJot.Services;
using Serilog.Events;

namespace JoJot.Tests.Services;

public class LogServiceTests
{
    [Fact]
    public void InitializeNoop_DoesNotThrow()
    {
        LogService.InitializeNoop();
    }

    [Fact]
    public void SetMinimumLevel_ChangesLevel()
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
    public void GetMinimumLevel_ReturnsCurrentLevel()
    {
        LogService.InitializeNoop();
        LogService.SetMinimumLevel(LogEventLevel.Error);

        LogService.GetMinimumLevel().Should().Be(LogEventLevel.Error);

        // Restore default
        LogService.SetMinimumLevel(LogEventLevel.Information);
    }

    // ─── Simple log calls do not throw ───────────────────────────────

    [Fact]
    public void Debug_DoesNotThrow()
    {
        LogService.InitializeNoop();
        LogService.Debug("test debug message");
    }

    [Fact]
    public void Info_DoesNotThrow()
    {
        LogService.InitializeNoop();
        LogService.Info("test info message");
    }

    [Fact]
    public void Warn_DoesNotThrow()
    {
        LogService.InitializeNoop();
        LogService.Warn("test warn message");
        LogService.Warn("test warn with exception", new InvalidOperationException("test"));
    }

    [Fact]
    public void Error_DoesNotThrow()
    {
        LogService.InitializeNoop();
        LogService.Error("test error message");
        LogService.Error("test error with exception", new InvalidOperationException("test"));
    }

    // ─── Structured template overloads do not throw ──────────────────

    [Fact]
    public void StructuredDebug_DoesNotThrow()
    {
        LogService.InitializeNoop();
        LogService.Debug("value={Value}", 42);
        LogService.Debug("a={A}, b={B}", 1, 2);
        LogService.Debug("a={A}, b={B}, c={C}", 1, 2, 3);
    }

    [Fact]
    public void StructuredInfo_DoesNotThrow()
    {
        LogService.InitializeNoop();
        LogService.Info("value={Value}", "hello");
        LogService.Info("a={A}, b={B}", "x", "y");
        LogService.Info("a={A}, b={B}, c={C}", 1, 2, 3);
        LogService.Info("a={A}, b={B}, c={C}, d={D}", 1, 2, 3, 4);
        LogService.Info("a={A}, b={B}, c={C}, d={D}, e={E}", 1, 2, 3, 4, 5);
    }

    [Fact]
    public void StructuredWarn_DoesNotThrow()
    {
        LogService.InitializeNoop();
        LogService.Warn("value={Value}", 42);
        LogService.Warn("a={A}, b={B}", 1, 2);
    }

    [Fact]
    public void StructuredError_DoesNotThrow()
    {
        LogService.InitializeNoop();
        LogService.Error("value={Value}", 42);
        LogService.Error("a={A}, b={B}", 1, 2);
        LogService.Error("a={A}, b={B}, c={C}", 1, 2, 3);
    }

    [Fact]
    public void Shutdown_DoesNotThrow()
    {
        LogService.InitializeNoop();
        LogService.Shutdown();
        // Reinitialize for other tests
        LogService.InitializeNoop();
    }
}
