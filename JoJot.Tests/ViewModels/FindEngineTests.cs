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

    // ─── FindAllMatches — CaseSensitive ──────────────────────────

    [Fact]
    public void FindAllMatches_CaseSensitive_ExactCase_Matches()
    {
        var result = MainWindowViewModel.FindAllMatches("Hello HELLO hello", "Hello", caseSensitive: true);
        result.Should().Equal([0]);
    }

    [Fact]
    public void FindAllMatches_CaseSensitive_WrongCase_NoMatch()
    {
        var result = MainWindowViewModel.FindAllMatches("hello world", "Hello", caseSensitive: true);
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindAllMatches_CaseSensitive_False_StillCaseInsensitive()
    {
        var result = MainWindowViewModel.FindAllMatches("Hello HELLO hello", "hello", caseSensitive: false);
        result.Should().Equal([0, 6, 12]);
    }

    [Fact]
    public void FindAllMatches_CaseSensitive_MultipleExactMatches()
    {
        var result = MainWindowViewModel.FindAllMatches("abc ABC abc", "abc", caseSensitive: true);
        result.Should().Equal([0, 8]);
    }

    // ─── FindAllMatches — WholeWord ───────────────────────────────

    [Fact]
    public void FindAllMatches_WholeWord_MatchesStandaloneWord()
    {
        var result = MainWindowViewModel.FindAllMatches("the cat", "the", wholeWord: true);
        result.Should().Equal([0]);
    }

    [Fact]
    public void FindAllMatches_WholeWord_NoMatchInsideWord()
    {
        var result = MainWindowViewModel.FindAllMatches("them bathe together", "the", wholeWord: true);
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindAllMatches_WholeWord_MatchAtEndOfString()
    {
        var result = MainWindowViewModel.FindAllMatches("cat the", "the", wholeWord: true);
        result.Should().Equal([4]);
    }

    [Fact]
    public void FindAllMatches_WholeWord_WordBoundaryByPunctuation()
    {
        var result = MainWindowViewModel.FindAllMatches("cat,the,dog", "the", wholeWord: true);
        result.Should().Equal([4]);
    }

    [Fact]
    public void FindAllMatches_WholeWord_WordBoundaryBySpace()
    {
        var result = MainWindowViewModel.FindAllMatches("say the word", "the", wholeWord: true);
        result.Should().Equal([4]);
    }

    [Fact]
    public void FindAllMatches_WholeWord_MultipleWholeWordMatches()
    {
        var result = MainWindowViewModel.FindAllMatches("the cat and the dog", "the", wholeWord: true);
        result.Should().Equal([0, 12]);
    }

    [Fact]
    public void FindAllMatches_WholeWord_DigitNotWordBoundary()
    {
        // "5the" — the preceded by digit, not a word boundary
        var result = MainWindowViewModel.FindAllMatches("5the end", "the", wholeWord: true);
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindAllMatches_WholeWord_AlphaAfterMatch_NoMatch()
    {
        var result = MainWindowViewModel.FindAllMatches("then", "the", wholeWord: true);
        result.Should().BeEmpty();
    }

    // ─── FindAllMatches — CaseSensitive + WholeWord ───────────────

    [Fact]
    public void FindAllMatches_CaseSensitiveAndWholeWord_ExactMatchOnly()
    {
        var result = MainWindowViewModel.FindAllMatches("The the THE", "the", caseSensitive: true, wholeWord: true);
        result.Should().Equal([4]);
    }

    [Fact]
    public void FindAllMatches_CaseSensitiveAndWholeWord_NoMatchWhenPartial()
    {
        var result = MainWindowViewModel.FindAllMatches("these the them", "the", caseSensitive: true, wholeWord: true);
        result.Should().Equal([6]);
    }

    // ─── ReplaceAll ───────────────────────────────────────────────

    [Fact]
    public void ReplaceAll_BasicReplacement()
    {
        var (content, count) = MainWindowViewModel.ReplaceAll("hello world hello", "hello", "hi");
        content.Should().Be("hi world hi");
        count.Should().Be(2);
    }

    [Fact]
    public void ReplaceAll_NoMatches_ReturnsOriginalAndZero()
    {
        var (content, count) = MainWindowViewModel.ReplaceAll("hello world", "xyz", "abc");
        content.Should().Be("hello world");
        count.Should().Be(0);
    }

    [Fact]
    public void ReplaceAll_EmptyReplacement_DeletesMatches()
    {
        var (content, count) = MainWindowViewModel.ReplaceAll("hello world hello", "hello", "");
        content.Should().Be(" world ");
        count.Should().Be(2);
    }

    [Fact]
    public void ReplaceAll_CaseSensitive_OnlyExactCaseReplaced()
    {
        var (content, count) = MainWindowViewModel.ReplaceAll("Hello hello HELLO", "hello", "hi", caseSensitive: true);
        content.Should().Be("Hello hi HELLO");
        count.Should().Be(1);
    }

    [Fact]
    public void ReplaceAll_WholeWord_OnlyWholeWordReplaced()
    {
        var (content, count) = MainWindowViewModel.ReplaceAll("the theme bathe the", "the", "a", wholeWord: true);
        content.Should().Be("a theme bathe a");
        count.Should().Be(2);
    }

    [Fact]
    public void ReplaceAll_CaseSensitiveAndWholeWord()
    {
        var (content, count) = MainWindowViewModel.ReplaceAll("The the THE", "the", "X", caseSensitive: true, wholeWord: true);
        content.Should().Be("The X THE");
        count.Should().Be(1);
    }

    [Fact]
    public void ReplaceAll_SingleMatch()
    {
        var (content, count) = MainWindowViewModel.ReplaceAll("abc def", "def", "xyz");
        content.Should().Be("abc xyz");
        count.Should().Be(1);
    }

    // ─── ReplaceSingle ────────────────────────────────────────────

    [Fact]
    public void ReplaceSingle_ReplacesAtSpecificIndex()
    {
        var result = MainWindowViewModel.ReplaceSingle("hello world hello", 6, 5, "there");
        result.Should().Be("hello there hello");
    }

    [Fact]
    public void ReplaceSingle_ReplaceAtStart()
    {
        var result = MainWindowViewModel.ReplaceSingle("hello world", 0, 5, "hi");
        result.Should().Be("hi world");
    }

    [Fact]
    public void ReplaceSingle_ReplaceAtEnd()
    {
        var result = MainWindowViewModel.ReplaceSingle("hello world", 6, 5, "there");
        result.Should().Be("hello there");
    }

    [Fact]
    public void ReplaceSingle_EmptyReplacement_DeletesMatch()
    {
        var result = MainWindowViewModel.ReplaceSingle("hello world", 5, 6, "");
        result.Should().Be("hello");
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
