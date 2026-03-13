using JoJot.Models;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Models;

/// <summary>
/// Boundary and edge case tests for NoteTab: display label truncation, time formatting boundaries,
/// property set/get, sort order extremes.
/// </summary>
public class NoteTabBoundaryTests
{
    // ─── DisplayLabel truncation boundary ──────────────────────────

    [Fact]
    public void DisplayLabel_Content45Chars_NoTruncation()
    {
        var content = new string('X', 45);
        var tab = new NoteTab { Content = content };
        tab.DisplayLabel.Should().Be(content);
        tab.DisplayLabel.Length.Should().Be(45);
    }

    [Fact]
    public void DisplayLabel_Content46Chars_Truncated()
    {
        var content = new string('Y', 46);
        var tab = new NoteTab { Content = content };
        tab.DisplayLabel.Length.Should().Be(45);
        tab.DisplayLabel.Should().Be(new string('Y', 45));
    }

    [Fact]
    public void DisplayLabel_Content44Chars_NoTruncation()
    {
        var content = new string('Z', 44);
        var tab = new NoteTab { Content = content };
        tab.DisplayLabel.Length.Should().Be(44);
    }

    // ─── DisplayLabel whitespace variants ──────────────────────────

    [Fact]
    public void DisplayLabel_TabOnlyContent_IsPlaceholder()
    {
        var tab = new NoteTab { Content = "\t\t" };
        tab.DisplayLabel.Should().Be("New note");
        tab.IsPlaceholder.Should().BeTrue();
    }

    [Fact]
    public void DisplayLabel_NewlineOnlyContent_IsPlaceholder()
    {
        var tab = new NoteTab { Content = "\n\n" };
        tab.DisplayLabel.Should().Be("New note");
    }

    [Fact]
    public void DisplayLabel_CrLfContent_IsPlaceholder()
    {
        var tab = new NoteTab { Content = "\r\n\r\n" };
        tab.DisplayLabel.Should().Be("New note");
    }

    [Fact]
    public void DisplayLabel_MixedWhitespace_IsPlaceholder()
    {
        var tab = new NoteTab { Content = " \t \n \r\n " };
        tab.DisplayLabel.Should().Be("New note");
    }

    // ─── Whitespace-only Name falls through to content ─────────────

    [Fact]
    public void DisplayLabel_WhitespaceOnlyName_FallsToContent()
    {
        var tab = new NoteTab { Name = "   ", Content = "Hello" };
        tab.DisplayLabel.Should().Be("Hello");
    }

    [Fact]
    public void DisplayLabel_TabName_FallsToContent()
    {
        var tab = new NoteTab { Name = "\t", Content = "Content" };
        tab.DisplayLabel.Should().Be("Content");
    }

    // ─── Content set to null ───────────────────────────────────────

    [Fact]
    public void Content_SetToNull_SetsToNullString()
    {
        var tab = new NoteTab { Content = "Something" };
        // Content is a non-nullable string, but the property doesn't guard against null assignment
        tab.Content = null!;
        tab.Content.Should().BeNull();
    }

    // ─── EditorScrollOffset and CursorPosition ─────────────────────

    [Fact]
    public void EditorScrollOffset_DefaultIsZero()
    {
        var tab = new NoteTab();
        tab.EditorScrollOffset.Should().Be(0);
    }

    [Fact]
    public void EditorScrollOffset_SetAndGet()
    {
        var tab = new NoteTab { EditorScrollOffset = 150 };
        tab.EditorScrollOffset.Should().Be(150);
    }

    [Fact]
    public void CursorPosition_DefaultIsZero()
    {
        var tab = new NoteTab();
        tab.CursorPosition.Should().Be(0);
    }

    [Fact]
    public void CursorPosition_SetAndGet()
    {
        var tab = new NoteTab { CursorPosition = 42 };
        tab.CursorPosition.Should().Be(42);
    }

    // ─── SortOrder boundary values ─────────────────────────────────

    [Fact]
    public void SortOrder_NegativeValue()
    {
        var tab = new NoteTab { SortOrder = -1 };
        tab.SortOrder.Should().Be(-1);
    }

    [Fact]
    public void SortOrder_MaxValue()
    {
        var tab = new NoteTab { SortOrder = int.MaxValue };
        tab.SortOrder.Should().Be(int.MaxValue);
    }

    // ─── FormatRelativeDate across midnight ────────────────────────

    [Fact]
    public void FormatRelativeDate_AcrossMidnight_LastSecondOfYesterday()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 0, 0, 1) };
        var dt = new DateTime(2025, 6, 14, 23, 59, 59);

        // dt.Date = June 14, now.Date = June 15 → yesterday
        NoteTab.FormatCreatedDisplay(dt, clock).Should().Be("Yesterday");
    }

    [Fact]
    public void FormatRelativeDate_Midnight_SameDay()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 23, 59, 59) };
        var dt = new DateTime(2025, 6, 15, 0, 0, 0);

        // Same date → time format
        NoteTab.FormatCreatedDisplay(dt, clock).Should().Be("12:00 AM");
    }

    // ─── FormatRelativeTime JustNow threshold ──────────────────────

    [Fact]
    public void FormatRelativeTime_At59Seconds_JustNow()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 14, 30, 59) };
        var dt = new DateTime(2025, 6, 15, 14, 30, 0);

        // diff = 59 seconds < 1 minute → "Just now"
        NoteTab.FormatUpdatedDisplay(dt, clock).Should().Be("Just now");
    }

    [Fact]
    public void FormatRelativeTime_At60Seconds_TodayTime()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 14, 31, 0) };
        var dt = new DateTime(2025, 6, 15, 14, 30, 0);

        // diff = 60 seconds = 1 minute → NOT "Just now"
        NoteTab.FormatUpdatedDisplay(dt, clock).Should().Be("Today 2:30 PM");
    }

    [Fact]
    public void FormatRelativeTime_At61Seconds_TodayTime()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 14, 31, 1) };
        var dt = new DateTime(2025, 6, 15, 14, 30, 0);

        // diff = 61 seconds > 1 minute → "Today ..."
        NoteTab.FormatUpdatedDisplay(dt, clock).Should().Be("Today 2:30 PM");
    }

    // ─── AM/PM time formatting ─────────────────────────────────────

    [Fact]
    public void FormatRelativeDate_Noon()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 14, 0, 0) };
        var dt = new DateTime(2025, 6, 15, 12, 0, 0);

        NoteTab.FormatCreatedDisplay(dt, clock).Should().Be("12:00 PM");
    }

    [Fact]
    public void FormatRelativeDate_Midnight_12AM()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 14, 0, 0) };
        var dt = new DateTime(2025, 6, 15, 0, 0, 0);

        NoteTab.FormatCreatedDisplay(dt, clock).Should().Be("12:00 AM");
    }

    [Fact]
    public void FormatRelativeDate_1159PM()
    {
        var clock = new TestClock { Now = new DateTime(2025, 6, 15, 23, 59, 59) };
        var dt = new DateTime(2025, 6, 15, 23, 59, 0);

        NoteTab.FormatCreatedDisplay(dt, clock).Should().Be("11:59 PM");
    }

    // ─── Year boundary ─────────────────────────────────────────────

    [Fact]
    public void FormatRelativeDate_YearBoundary_Dec31ToJan1_IsYesterday()
    {
        var clock = new TestClock { Now = new DateTime(2025, 1, 1, 0, 0, 0) };
        var dt = new DateTime(2024, 12, 31, 23, 59, 59);

        // Yesterday check fires before year check
        NoteTab.FormatCreatedDisplay(dt, clock).Should().Be("Yesterday");
    }

    [Fact]
    public void FormatRelativeDate_DifferentYear_NotYesterday()
    {
        var clock = new TestClock { Now = new DateTime(2025, 1, 2, 0, 0, 0) };
        var dt = new DateTime(2024, 12, 30, 10, 0, 0);

        // Not yesterday, different year → full date
        NoteTab.FormatCreatedDisplay(dt, clock).Should().Be("Dec 30, 2024");
    }

    [Fact]
    public void FormatRelativeTime_YearBoundary()
    {
        var clock = new TestClock { Now = new DateTime(2025, 1, 1, 0, 5, 0) };
        var dt = new DateTime(2024, 12, 31, 23, 55, 0);

        // dt.Date = Dec 31, now.Date = Jan 1 → yesterday
        NoteTab.FormatUpdatedDisplay(dt, clock).Should().Be("Yesterday 11:55 PM");
    }

    // ─── Id ────────────────────────────────────────────────────────

    [Fact]
    public void Id_Default_IsZero()
    {
        var tab = new NoteTab();
        tab.Id.Should().Be(0);
    }

    // ─── DesktopGuid ───────────────────────────────────────────────

    [Fact]
    public void DesktopGuid_Default_IsEmptyString()
    {
        var tab = new NoteTab();
        tab.DesktopGuid.Should().Be("");
    }
}
