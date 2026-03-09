using JoJot.Services;
using Serilog;

namespace JoJot.Tests.Services;

/// <summary>
/// Additional LogService tests for ForContext methods.
/// </summary>
public class LogServiceCoverage2Tests
{
    [Fact]
    public void ForContext_Generic_ReturnsLogger()
    {
        LogService.InitializeNoop();
        var logger = LogService.ForContext<LogServiceCoverage2Tests>();
        logger.Should().NotBeNull();
    }

    [Fact]
    public void ForContext_String_ReturnsLogger()
    {
        LogService.InitializeNoop();
        var logger = LogService.ForContext("TestProperty", "TestValue");
        logger.Should().NotBeNull();
    }

    [Fact]
    public void ForContext_Generic_CanLog()
    {
        LogService.InitializeNoop();
        var logger = LogService.ForContext<LogServiceCoverage2Tests>();
        // Should not throw
        logger.Information("Test {Param}", "value");
    }
}
