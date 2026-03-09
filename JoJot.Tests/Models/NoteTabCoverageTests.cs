using JoJot.Models;

namespace JoJot.Tests.Models;

/// <summary>
/// Additional NoteTab tests targeting uncovered branches:
/// CreatedDisplay/UpdatedDisplay instance properties, tooltip instance properties.
/// </summary>
public class NoteTabCoverageTests
{
    [Fact]
    public void CreatedDisplay_ReturnsFormattedDate()
    {
        var tab = new NoteTab
        {
            CreatedAt = new DateTime(2025, 6, 15, 9, 30, 0)
        };

        // CreatedDisplay uses SystemClock.Instance, so we just verify it returns non-empty
        tab.CreatedDisplay.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void UpdatedDisplay_ReturnsFormattedTime()
    {
        var tab = new NoteTab
        {
            UpdatedAt = DateTime.Now.AddMinutes(-5)
        };

        tab.UpdatedDisplay.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CreatedTooltipText_ReturnsFormattedTooltip()
    {
        var dt = new DateTime(2025, 3, 10, 14, 0, 0);
        var tab = new NoteTab { CreatedAt = dt };

        tab.CreatedTooltipText.Should().Be("Created: Mar 10, 2025 2:00 PM");
    }

    [Fact]
    public void UpdatedTooltipText_ReturnsFormattedTooltip()
    {
        var dt = new DateTime(2025, 3, 10, 14, 0, 0);
        var tab = new NoteTab { UpdatedAt = dt };

        tab.UpdatedTooltipText.Should().Be("Last updated: Mar 10, 2025 2:00 PM");
    }

    [Fact]
    public void UpdatedAt_Change_RaisesUpdatedDisplay()
    {
        var tab = new NoteTab { UpdatedAt = new DateTime(2025, 1, 1) };
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.UpdatedAt = new DateTime(2025, 6, 15, 10, 0, 0);

        raised.Should().Contain(nameof(NoteTab.UpdatedDisplay));
        raised.Should().Contain(nameof(NoteTab.UpdatedTooltipText));
    }
}
