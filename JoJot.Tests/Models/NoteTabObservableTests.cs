using JoJot.Models;

namespace JoJot.Tests.Models;

public class NoteTabObservableTests
{
    // ─── Name notifications ──────────────────────────────────────────

    [Fact]
    public void Name_Change_RaisesPropertyChanged()
    {
        var tab = new NoteTab();
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.Name = "My Note";

        raised.Should().Contain(nameof(NoteTab.Name));
    }

    [Fact]
    public void Name_Change_RaisesDisplayLabel()
    {
        var tab = new NoteTab();
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.Name = "My Note";

        raised.Should().Contain(nameof(NoteTab.DisplayLabel));
    }

    [Fact]
    public void Name_Change_RaisesIsPlaceholder()
    {
        var tab = new NoteTab();
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.Name = "My Note";

        raised.Should().Contain(nameof(NoteTab.IsPlaceholder));
    }

    [Fact]
    public void Name_SameValue_DoesNotRaise()
    {
        var tab = new NoteTab { Name = "Test" };
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.Name = "Test";

        raised.Should().BeEmpty();
    }

    // ─── Content notifications ───────────────────────────────────────

    [Fact]
    public void Content_Change_RaisesPropertyChanged()
    {
        var tab = new NoteTab();
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.Content = "Hello world";

        raised.Should().Contain(nameof(NoteTab.Content));
    }

    [Fact]
    public void Content_Change_RaisesDisplayLabel()
    {
        var tab = new NoteTab();
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.Content = "Hello world";

        raised.Should().Contain(nameof(NoteTab.DisplayLabel));
    }

    [Fact]
    public void Content_Change_RaisesIsPlaceholder()
    {
        var tab = new NoteTab();
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.Content = "Hello world";

        raised.Should().Contain(nameof(NoteTab.IsPlaceholder));
    }

    [Fact]
    public void Content_SameValue_DoesNotRaise()
    {
        var tab = new NoteTab { Content = "same" };
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.Content = "same";

        raised.Should().BeEmpty();
    }

    // ─── Pinned notifications ────────────────────────────────────────

    [Fact]
    public void Pinned_Change_RaisesPropertyChanged()
    {
        var tab = new NoteTab();
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.Pinned = true;

        raised.Should().ContainSingle().Which.Should().Be(nameof(NoteTab.Pinned));
    }

    [Fact]
    public void Pinned_SameValue_DoesNotRaise()
    {
        var tab = new NoteTab { Pinned = true };
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.Pinned = true;

        raised.Should().BeEmpty();
    }

    // ─── UpdatedAt notifications ─────────────────────────────────────

    [Fact]
    public void UpdatedAt_Change_RaisesPropertyChanged()
    {
        var tab = new NoteTab();
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.UpdatedAt = new DateTime(2026, 1, 1);

        raised.Should().Contain(nameof(NoteTab.UpdatedAt));
    }

    [Fact]
    public void UpdatedAt_Change_RaisesUpdatedDisplay()
    {
        var tab = new NoteTab();
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.UpdatedAt = new DateTime(2026, 1, 1);

        raised.Should().Contain(nameof(NoteTab.UpdatedDisplay));
    }

    [Fact]
    public void UpdatedAt_Change_RaisesUpdatedTooltipText()
    {
        var tab = new NoteTab();
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.UpdatedAt = new DateTime(2026, 1, 1);

        raised.Should().Contain(nameof(NoteTab.UpdatedTooltipText));
    }

    [Fact]
    public void UpdatedAt_SameValue_DoesNotRaise()
    {
        var dt = new DateTime(2026, 1, 1);
        var tab = new NoteTab { UpdatedAt = dt };
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.UpdatedAt = dt;

        raised.Should().BeEmpty();
    }

    // ─── SortOrder notifications ─────────────────────────────────────

    [Fact]
    public void SortOrder_Change_RaisesPropertyChanged()
    {
        var tab = new NoteTab();
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.SortOrder = 5;

        raised.Should().ContainSingle().Which.Should().Be(nameof(NoteTab.SortOrder));
    }

    [Fact]
    public void SortOrder_SameValue_DoesNotRaise()
    {
        var tab = new NoteTab { SortOrder = 3 };
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.SortOrder = 3;

        raised.Should().BeEmpty();
    }

    // ─── Tooltip computed properties ─────────────────────────────────

    [Fact]
    public void CreatedTooltipText_ReturnsFormattedCreatedAt()
    {
        var tab = new NoteTab { CreatedAt = new DateTime(2026, 3, 15, 14, 30, 0) };

        tab.CreatedTooltipText.Should().Be("Created: Mar 15, 2026 2:30 PM");
    }

    [Fact]
    public void UpdatedTooltipText_ReturnsFormattedUpdatedAt()
    {
        var tab = new NoteTab { UpdatedAt = new DateTime(2026, 3, 15, 14, 30, 0) };

        tab.UpdatedTooltipText.Should().Be("Last updated: Mar 15, 2026 2:30 PM");
    }

    [Fact]
    public void UpdatedTooltipText_ReflectsPropertyChange()
    {
        var tab = new NoteTab { UpdatedAt = new DateTime(2026, 1, 1) };
        var original = tab.UpdatedTooltipText;

        tab.UpdatedAt = new DateTime(2026, 6, 15, 10, 0, 0);

        tab.UpdatedTooltipText.Should().NotBe(original);
        tab.UpdatedTooltipText.Should().Be("Last updated: Jun 15, 2026 10:00 AM");
    }

    // ─── Non-observable properties don't notify ──────────────────────

    [Fact]
    public void Id_Set_DoesNotRaise()
    {
        var tab = new NoteTab();
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.Id = 99;

        raised.Should().BeEmpty();
    }

    [Fact]
    public void DesktopGuid_Set_DoesNotRaise()
    {
        var tab = new NoteTab();
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.DesktopGuid = "abc-123";

        raised.Should().BeEmpty();
    }

    [Fact]
    public void CreatedAt_Set_DoesNotRaise()
    {
        var tab = new NoteTab();
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.CreatedAt = DateTime.Now;

        raised.Should().BeEmpty();
    }
}
