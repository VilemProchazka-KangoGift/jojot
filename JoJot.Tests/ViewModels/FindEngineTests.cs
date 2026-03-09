using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

/// <summary>
/// Tests for MainWindowViewModel.FindAllMatches, CycleIndex, and FormatFindCountText.
/// </summary>
public class FindEngineTests
{
    // ─── FindAllMatches ──────────────────────────────────────────

    [Fact]
    public void FindAllMatches_SingleMatch()
    {
        var result = MainWindowViewModel.FindAllMatches("hello world", "world");
        result.Should().Equal([6]);
    }

    [Fact]
    public void FindAllMatches_MultipleMatches()
    {
        var result = MainWindowViewModel.FindAllMatches("abcabcabc", "abc");
        result.Should().Equal([0, 3, 6]);
    }

    [Fact]
    public void FindAllMatches_CaseInsensitive()
    {
        var result = MainWindowViewModel.FindAllMatches("Hello HELLO hello", "hello");
        result.Should().Equal([0, 6, 12]);
    }

    [Fact]
    public void FindAllMatches_NoMatch()
    {
        var result = MainWindowViewModel.FindAllMatches("hello world", "xyz");
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindAllMatches_EmptyQuery_ReturnsEmpty()
    {
        var result = MainWindowViewModel.FindAllMatches("hello", "");
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindAllMatches_NullQuery_ReturnsEmpty()
    {
        var result = MainWindowViewModel.FindAllMatches("hello", null!);
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindAllMatches_EmptyContent_ReturnsEmpty()
    {
        var result = MainWindowViewModel.FindAllMatches("", "hello");
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindAllMatches_NullContent_ReturnsEmpty()
    {
        var result = MainWindowViewModel.FindAllMatches(null!, "hello");
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindAllMatches_OverlappingPattern_NonOverlapping()
    {
        // "aaa" in "aaaa" — non-overlapping: finds at 0, then skips to 3
        var result = MainWindowViewModel.FindAllMatches("aaaa", "aaa");
        result.Should().Equal([0]);
    }

    [Fact]
    public void FindAllMatches_QueryLongerThanContent()
    {
        var result = MainWindowViewModel.FindAllMatches("hi", "hello world");
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindAllMatches_QueryEqualsContent()
    {
        var result = MainWindowViewModel.FindAllMatches("hello", "hello");
        result.Should().Equal([0]);
    }

    [Fact]
    public void FindAllMatches_SingleCharQuery()
    {
        var result = MainWindowViewModel.FindAllMatches("abba", "b");
        result.Should().Equal([1, 2]);
    }

    // ─── CycleIndex ──────────────────────────────────────────────

    [Fact]
    public void CycleIndex_Forward_Increments()
    {
        MainWindowViewModel.CycleIndex(0, 5, forward: true).Should().Be(1);
    }

    [Fact]
    public void CycleIndex_Forward_WrapsAround()
    {
        MainWindowViewModel.CycleIndex(4, 5, forward: true).Should().Be(0);
    }

    [Fact]
    public void CycleIndex_Backward_Decrements()
    {
        MainWindowViewModel.CycleIndex(2, 5, forward: false).Should().Be(1);
    }

    [Fact]
    public void CycleIndex_Backward_WrapsAround()
    {
        MainWindowViewModel.CycleIndex(0, 5, forward: false).Should().Be(4);
    }

    [Fact]
    public void CycleIndex_ZeroTotal_ReturnsNegativeOne()
    {
        MainWindowViewModel.CycleIndex(0, 0, forward: true).Should().Be(-1);
    }

    [Fact]
    public void CycleIndex_SingleItem_ForwardWraps()
    {
        MainWindowViewModel.CycleIndex(0, 1, forward: true).Should().Be(0);
    }

    [Fact]
    public void CycleIndex_SingleItem_BackwardWraps()
    {
        MainWindowViewModel.CycleIndex(0, 1, forward: false).Should().Be(0);
    }

    // ─── FormatFindCountText ─────────────────────────────────────

    [Fact]
    public void FormatFindCountText_WithMatches()
    {
        MainWindowViewModel.FormatFindCountText(0, 5).Should().Be("1/5");
    }

    [Fact]
    public void FormatFindCountText_LastMatch()
    {
        MainWindowViewModel.FormatFindCountText(4, 5).Should().Be("5/5");
    }

    [Fact]
    public void FormatFindCountText_NoMatches()
    {
        MainWindowViewModel.FormatFindCountText(-1, 0).Should().Be("No matches");
    }

    [Fact]
    public void FormatFindCountText_SingleMatch()
    {
        MainWindowViewModel.FormatFindCountText(0, 1).Should().Be("1/1");
    }
}
