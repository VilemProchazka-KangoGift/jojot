using JoJot.Controls;

namespace JoJot.Tests.Controls;

public class FontSizeToPercentTests
{
    [Fact]
    public void FontSizeToPercent_DefaultSize13_Returns100()
    {
        PreferencesPanel.FontSizeToPercent(13).Should().Be("100%");
    }

    [Fact]
    public void FontSizeToPercent_Size8_ReturnsRounded()
    {
        // 8 * 100 / 13 = 61.538... → rounds to 62
        PreferencesPanel.FontSizeToPercent(8).Should().Be("62%");
    }

    [Fact]
    public void FontSizeToPercent_Size32_ReturnsRounded()
    {
        // 32 * 100 / 13 = 246.15... → rounds to 246
        PreferencesPanel.FontSizeToPercent(32).Should().Be("246%");
    }

    [Fact]
    public void FontSizeToPercent_Size0_Returns0()
    {
        PreferencesPanel.FontSizeToPercent(0).Should().Be("0%");
    }

    [Fact]
    public void FontSizeToPercent_Size26_ExactlyDivisible()
    {
        // 26 * 100 / 13 = 200
        PreferencesPanel.FontSizeToPercent(26).Should().Be("200%");
    }

    [Fact]
    public void FontSizeToPercent_Size1_ReturnsSmallPercent()
    {
        // 1 * 100 / 13 = 7.692... → 8
        PreferencesPanel.FontSizeToPercent(1).Should().Be("8%");
    }
}
