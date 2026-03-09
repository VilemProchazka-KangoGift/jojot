using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

/// <summary>
/// Tests for MainWindowViewModel.ParseFontSize and ClampFontSize.
/// </summary>
public class FontSizeTests
{
    // ─── ParseFontSize ───────────────────────────────────────────

    [Fact]
    public void ParseFontSize_ValidInRange_ReturnsValue()
    {
        MainWindowViewModel.ParseFontSize("14").Should().Be(14);
    }

    [Fact]
    public void ParseFontSize_AtMin_ReturnsMin()
    {
        MainWindowViewModel.ParseFontSize("8").Should().Be(8);
    }

    [Fact]
    public void ParseFontSize_AtMax_ReturnsMax()
    {
        MainWindowViewModel.ParseFontSize("32").Should().Be(32);
    }

    [Fact]
    public void ParseFontSize_BelowMin_ClampsToMin()
    {
        MainWindowViewModel.ParseFontSize("5").Should().Be(8);
    }

    [Fact]
    public void ParseFontSize_AboveMax_ClampsToMax()
    {
        MainWindowViewModel.ParseFontSize("50").Should().Be(32);
    }

    [Fact]
    public void ParseFontSize_Null_ReturnsDefault()
    {
        MainWindowViewModel.ParseFontSize(null).Should().Be(13);
    }

    [Fact]
    public void ParseFontSize_Empty_ReturnsDefault()
    {
        MainWindowViewModel.ParseFontSize("").Should().Be(13);
    }

    [Fact]
    public void ParseFontSize_NonNumeric_ReturnsDefault()
    {
        MainWindowViewModel.ParseFontSize("abc").Should().Be(13);
    }

    [Fact]
    public void ParseFontSize_Zero_ClampsToMin()
    {
        MainWindowViewModel.ParseFontSize("0").Should().Be(8);
    }

    [Fact]
    public void ParseFontSize_Negative_ClampsToMin()
    {
        MainWindowViewModel.ParseFontSize("-5").Should().Be(8);
    }

    // ─── ClampFontSize ───────────────────────────────────────────

    [Fact]
    public void ClampFontSize_DeltaUp_InRange()
    {
        MainWindowViewModel.ClampFontSize(13, 1).Should().Be(14);
    }

    [Fact]
    public void ClampFontSize_DeltaDown_InRange()
    {
        MainWindowViewModel.ClampFontSize(13, -1).Should().Be(12);
    }

    [Fact]
    public void ClampFontSize_AtMin_DeltaDown_ClampsToMin()
    {
        MainWindowViewModel.ClampFontSize(8, -1).Should().Be(8);
    }

    [Fact]
    public void ClampFontSize_AtMax_DeltaUp_ClampsToMax()
    {
        MainWindowViewModel.ClampFontSize(32, 1).Should().Be(32);
    }

    [Fact]
    public void ClampFontSize_LargeDelta_ClampsToMax()
    {
        MainWindowViewModel.ClampFontSize(13, 100).Should().Be(32);
    }

    [Fact]
    public void ClampFontSize_LargeNegativeDelta_ClampsToMin()
    {
        MainWindowViewModel.ClampFontSize(13, -100).Should().Be(8);
    }

    [Fact]
    public void ClampFontSize_ZeroDelta_Unchanged()
    {
        MainWindowViewModel.ClampFontSize(20, 0).Should().Be(20);
    }
}
