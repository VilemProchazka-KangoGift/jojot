using JoJot.Services;

namespace JoJot.Tests.Services;

public class StartupServiceEscapeSqlTests
{
    [Fact]
    public void EscapeSql_NoQuotes_ReturnsUnchanged()
    {
        StartupService.EscapeSql("hello world").Should().Be("hello world");
    }

    [Fact]
    public void EscapeSql_SingleQuote_Doubled()
    {
        StartupService.EscapeSql("it's").Should().Be("it''s");
    }

    [Fact]
    public void EscapeSql_MultipleQuotes_AllDoubled()
    {
        StartupService.EscapeSql("'hello' 'world'").Should().Be("''hello'' ''world''");
    }

    [Fact]
    public void EscapeSql_EmptyString_ReturnsEmpty()
    {
        StartupService.EscapeSql("").Should().Be("");
    }

    [Fact]
    public void EscapeSql_OnlySingleQuote_Doubled()
    {
        StartupService.EscapeSql("'").Should().Be("''");
    }

    [Fact]
    public void EscapeSql_ConsecutiveQuotes_AllDoubled()
    {
        StartupService.EscapeSql("''").Should().Be("''''");
    }
}
