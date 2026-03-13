using JoJot.Models;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Models;

public class NoteTabTests
{
    // ─── DisplayLabel ──────────────────────────────────────────────────

    [Fact]
    public void DisplayLabel_ReturnsName_WhenNameIsSet()
    {
        var tab = new NoteTab { Name = "My Note", Content = "Some content" };
        tab.DisplayLabel.Should().Be("My Note");
    }

    [Fact]
    public void DisplayLabel_ReturnsTrimmedContent_WhenNameIsNull()
    {
        var tab = new NoteTab { Name = null, Content = "  Hello world  " };
        tab.DisplayLabel.Should().Be("Hello world");
    }

    [Fact]
    public void DisplayLabel_TruncatesAt45Chars_WhenContentIsLong()
    {
        var longContent = new string('A', 60);
        var tab = new NoteTab { Name = null, Content = longContent };
        tab.DisplayLabel.Should().HaveLength(48);
        tab.DisplayLabel.Should().Be(new string('A', 45) + "...");
    }

    [Fact]
    public void DisplayLabel_ReturnsFullContent_WhenContentIs45CharsOrLess()
    {
        var content = new string('B', 45);
        var tab = new NoteTab { Name = null, Content = content };
        tab.DisplayLabel.Should().Be(content);
    }

    [Fact]
    public void DisplayLabel_ReturnsPlaceholder_WhenNameAndContentEmpty()
    {
        var tab = new NoteTab { Name = null, Content = "" };
        tab.DisplayLabel.Should().Be("New note");
    }

    [Fact]
    public void DisplayLabel_ReturnsPlaceholder_WhenContentIsWhitespace()
    {
        var tab = new NoteTab { Name = null, Content = "   " };
        tab.DisplayLabel.Should().Be("New note");
    }

    [Fact]
    public void DisplayLabel_PrefersName_OverContent()
    {
        var tab = new NoteTab { Name = "Named", Content = "Has content too" };
        tab.DisplayLabel.Should().Be("Named");
    }

    [Fact]
    public void DisplayLabel_ReplacesNewlines_InContentFallback()
    {
        var tab = new NoteTab { Name = null, Content = "Hello\nWorld" };
        tab.DisplayLabel.Should().Be("Hello World");
    }

    [Fact]
    public void DisplayLabel_ReplacesCRLF_InContentFallback()
    {
        var tab = new NoteTab { Name = null, Content = "Line1\r\nLine2" };
        tab.DisplayLabel.Should().Be("Line1 Line2");
    }

    [Fact]
    public void DisplayLabel_CollapsesMultipleNewlines()
    {
        var tab = new NoteTab { Name = null, Content = "A\n\n\nB" };
        tab.DisplayLabel.Should().Be("A B");
    }

    [Fact]
    public void DisplayLabel_TruncatesAfterNewlineStripping()
    {
        // Content with newlines where the cleaned text exceeds 45 chars
        // 4 groups of 15 chars separated by newlines -> 63 chars after cleaning -> truncated to 45 + "..."
        var tab = new NoteTab { Name = null, Content = new string('A', 15) + "\n" + new string('B', 15) + "\n" + new string('C', 15) + "\n" + new string('D', 15) };
        tab.DisplayLabel.Should().HaveLength(48);
        tab.DisplayLabel.Should().Be("AAAAAAAAAAAAAAA BBBBBBBBBBBBBBB CCCCCCCCCCCCC...");
    }

    [Fact]
    public void DisplayLabel_PreservesNewlinesInCustomName()
    {
        var tab = new NoteTab { Name = "Has\nNewline", Content = "whatever" };
        tab.DisplayLabel.Should().Be("Has\nNewline");
    }

    // ─── IsPlaceholder ─────────────────────────────────────────────────

    [Fact]
    public void IsPlaceholder_True_WhenNameNullAndContentEmpty()
    {
        var tab = new NoteTab { Name = null, Content = "" };
        tab.IsPlaceholder.Should().BeTrue();
    }

    [Fact]
    public void IsPlaceholder_True_WhenNameNullAndContentWhitespace()
    {
        var tab = new NoteTab { Name = null, Content = "   " };
        tab.IsPlaceholder.Should().BeTrue();
    }

    [Fact]
    public void IsPlaceholder_False_WhenNameSet()
    {
        var tab = new NoteTab { Name = "Test", Content = "" };
        tab.IsPlaceholder.Should().BeFalse();
    }

    [Fact]
    public void IsPlaceholder_False_WhenContentNotEmpty()
    {
        var tab = new NoteTab { Name = null, Content = "hello" };
        tab.IsPlaceholder.Should().BeFalse();
    }

    // ─── FormatRelativeDate ────────────────────────────────────────────

    [Fact]
    public void FormatCreatedDisplay_Today_ShowsTime()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 14, 30, 0) };
        var dt = new DateTime(2025, 6, 15, 9, 15, 0);

        NoteTab.FormatCreatedDisplay(dt, clock).Should().Be("9:15 AM");
    }

    [Fact]
    public void FormatCreatedDisplay_Yesterday_ShowsYesterday()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 14, 30, 0) };
        var dt = new DateTime(2025, 6, 14, 20, 0, 0);

        NoteTab.FormatCreatedDisplay(dt, clock).Should().Be("Yesterday");
    }

    [Fact]
    public void FormatCreatedDisplay_SameYear_ShowsMonthDay()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 14, 30, 0) };
        var dt = new DateTime(2025, 3, 10, 8, 0, 0);

        NoteTab.FormatCreatedDisplay(dt, clock).Should().Be("Mar 10");
    }

    [Fact]
    public void FormatCreatedDisplay_DifferentYear_ShowsFullDate()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 14, 30, 0) };
        var dt = new DateTime(2024, 12, 25, 10, 0, 0);

        NoteTab.FormatCreatedDisplay(dt, clock).Should().Be("Dec 25, 2024");
    }

    // ─── FormatRelativeTime ────────────────────────────────────────────

    [Fact]
    public void FormatUpdatedDisplay_JustNow_WhenUnderOneMinute()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 14, 30, 30) };
        var dt = new DateTime(2025, 6, 15, 14, 30, 0);

        NoteTab.FormatUpdatedDisplay(dt, clock).Should().Be("Just now");
    }

    [Fact]
    public void FormatUpdatedDisplay_Today_ShowsTodayWithTime()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 14, 30, 0) };
        var dt = new DateTime(2025, 6, 15, 9, 15, 0);

        NoteTab.FormatUpdatedDisplay(dt, clock).Should().Be("Today 9:15 AM");
    }

    [Fact]
    public void FormatUpdatedDisplay_Yesterday_ShowsYesterdayWithTime()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 14, 30, 0) };
        var dt = new DateTime(2025, 6, 14, 20, 45, 0);

        NoteTab.FormatUpdatedDisplay(dt, clock).Should().Be("Yesterday 8:45 PM");
    }

    [Fact]
    public void FormatUpdatedDisplay_SameYear_ShowsMonthDayTime()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 14, 30, 0) };
        var dt = new DateTime(2025, 3, 10, 8, 30, 0);

        NoteTab.FormatUpdatedDisplay(dt, clock).Should().Be("Mar 10, 8:30 AM");
    }

    [Fact]
    public void FormatUpdatedDisplay_DifferentYear_ShowsFullDateTime()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 14, 30, 0) };
        var dt = new DateTime(2024, 12, 25, 10, 0, 0);

        NoteTab.FormatUpdatedDisplay(dt, clock).Should().Be("Dec 25, 2024 10:00 AM");
    }

    // ─── Tooltips ──────────────────────────────────────────────────────

    [Fact]
    public void CreatedTooltip_FormatsCorrectly()
    {
        var dt = new DateTime(2025, 6, 15, 14, 30, 0);
        NoteTab.CreatedTooltip(dt).Should().Be("Created: Jun 15, 2025 2:30 PM");
    }

    [Fact]
    public void UpdatedTooltip_FormatsCorrectly()
    {
        var dt = new DateTime(2025, 6, 15, 14, 30, 0);
        NoteTab.UpdatedTooltip(dt).Should().Be("Last updated: Jun 15, 2025 2:30 PM");
    }
}
