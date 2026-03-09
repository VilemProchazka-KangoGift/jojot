using JoJot.Models;

namespace JoJot.Tests.Models;

public class WindowGeometryTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var geo = new WindowGeometry(100.5, 200.5, 800, 600, true);

        geo.Left.Should().Be(100.5);
        geo.Top.Should().Be(200.5);
        geo.Width.Should().Be(800);
        geo.Height.Should().Be(600);
        geo.IsMaximized.Should().BeTrue();
    }

    [Fact]
    public void ValueEquality_EqualRecords()
    {
        var a = new WindowGeometry(100, 200, 800, 600, false);
        var b = new WindowGeometry(100, 200, 800, 600, false);

        a.Should().Be(b);
    }

    [Fact]
    public void ValueEquality_DifferentLeft()
    {
        var a = new WindowGeometry(100, 200, 800, 600, false);
        var b = new WindowGeometry(101, 200, 800, 600, false);

        a.Should().NotBe(b);
    }

    [Fact]
    public void ValueEquality_DifferentMaximized()
    {
        var a = new WindowGeometry(100, 200, 800, 600, false);
        var b = new WindowGeometry(100, 200, 800, 600, true);

        a.Should().NotBe(b);
    }

    [Fact]
    public void With_CreatesModifiedCopy()
    {
        var original = new WindowGeometry(100, 200, 800, 600, false);
        var modified = original with { Left = 300, Top = 400 };

        modified.Left.Should().Be(300);
        modified.Top.Should().Be(400);
        modified.Width.Should().Be(800);
        modified.Height.Should().Be(600);
        modified.IsMaximized.Should().BeFalse();

        // Original unchanged
        original.Left.Should().Be(100);
        original.Top.Should().Be(200);
    }

    [Fact]
    public void With_PreservesMaximized()
    {
        var original = new WindowGeometry(100, 200, 800, 600, true);
        var modified = original with { Width = 1024 };

        modified.IsMaximized.Should().BeTrue();
    }
}
