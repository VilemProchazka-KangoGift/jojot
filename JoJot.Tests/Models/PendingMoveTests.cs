using JoJot.Models;

namespace JoJot.Tests.Models;

public class PendingMoveTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var move = new PendingMove(1, "win-1", "desk-from", "desk-to", "2025-06-15 10:30:00");

        move.Id.Should().Be(1);
        move.WindowId.Should().Be("win-1");
        move.FromDesktop.Should().Be("desk-from");
        move.ToDesktop.Should().Be("desk-to");
        move.DetectedAt.Should().Be("2025-06-15 10:30:00");
    }

    [Fact]
    public void Constructor_AllowsNullToDesktop()
    {
        var move = new PendingMove(2, "win-2", "desk-from", null, "2025-06-15");

        move.ToDesktop.Should().BeNull();
    }

    [Fact]
    public void ValueEquality_EqualRecords()
    {
        var a = new PendingMove(1, "w", "f", "t", "ts");
        var b = new PendingMove(1, "w", "f", "t", "ts");
        a.Should().Be(b);
    }

    [Fact]
    public void ValueEquality_DifferentId()
    {
        var a = new PendingMove(1, "w", "f", "t", "ts");
        var b = new PendingMove(2, "w", "f", "t", "ts");
        a.Should().NotBe(b);
    }

    [Fact]
    public void ValueEquality_DifferentWindowId()
    {
        var a = new PendingMove(1, "w1", "f", "t", "ts");
        var b = new PendingMove(1, "w2", "f", "t", "ts");
        a.Should().NotBe(b);
    }

    [Fact]
    public void With_CreatesModifiedCopy()
    {
        var original = new PendingMove(1, "w", "f", "t", "ts");
        var modified = original with { ToDesktop = "new-desk" };

        modified.ToDesktop.Should().Be("new-desk");
        modified.Id.Should().Be(1);
        original.ToDesktop.Should().Be("t");
    }

    [Fact]
    public void ToString_ContainsAllFields()
    {
        var move = new PendingMove(42, "win-x", "from-d", "to-d", "2025-01-01");
        var str = move.ToString();

        str.Should().Contain("42");
        str.Should().Contain("win-x");
        str.Should().Contain("from-d");
        str.Should().Contain("to-d");
    }
}
