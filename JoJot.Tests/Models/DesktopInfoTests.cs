using JoJot.Models;

namespace JoJot.Tests.Models;

public class DesktopInfoTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var id = Guid.NewGuid();
        var info = new DesktopInfo(id, "Desktop 1", 0);

        info.Id.Should().Be(id);
        info.Name.Should().Be("Desktop 1");
        info.Index.Should().Be(0);
    }

    [Fact]
    public void Constructor_EmptyGuid_Allowed()
    {
        var info = new DesktopInfo(Guid.Empty, "Default", 0);
        info.Id.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Constructor_NullName_Allowed()
    {
        var info = new DesktopInfo(Guid.NewGuid(), null!, -1);
        info.Name.Should().BeNull();
    }

    [Fact]
    public void Constructor_NegativeIndex_Allowed()
    {
        var info = new DesktopInfo(Guid.NewGuid(), "Test", -1);
        info.Index.Should().Be(-1);
    }

    [Fact]
    public void ValueEquality_SameValues()
    {
        var id = new Guid("12345678-1234-1234-1234-123456789012");
        var a = new DesktopInfo(id, "Desktop", 0);
        var b = new DesktopInfo(id, "Desktop", 0);
        a.Should().Be(b);
    }

    [Fact]
    public void ValueInequality_DifferentId()
    {
        var a = new DesktopInfo(Guid.NewGuid(), "Desktop", 0);
        var b = new DesktopInfo(Guid.NewGuid(), "Desktop", 0);
        a.Should().NotBe(b);
    }

    [Fact]
    public void ValueInequality_DifferentName()
    {
        var id = Guid.NewGuid();
        var a = new DesktopInfo(id, "Desktop 1", 0);
        var b = new DesktopInfo(id, "Desktop 2", 0);
        a.Should().NotBe(b);
    }

    [Fact]
    public void ValueInequality_DifferentIndex()
    {
        var id = Guid.NewGuid();
        var a = new DesktopInfo(id, "Desktop", 0);
        var b = new DesktopInfo(id, "Desktop", 1);
        a.Should().NotBe(b);
    }

    [Fact]
    public void With_CreatesModifiedCopy()
    {
        var info = new DesktopInfo(Guid.Empty, "Original", 0);
        var modified = info with { Name = "Modified", Index = 5 };

        modified.Name.Should().Be("Modified");
        modified.Index.Should().Be(5);
        modified.Id.Should().Be(Guid.Empty);
        info.Name.Should().Be("Original");
    }

    [Fact]
    public void ToString_ContainsFields()
    {
        var info = new DesktopInfo(Guid.Empty, "TestDesktop", 3);
        var str = info.ToString();
        str.Should().Contain("TestDesktop");
        str.Should().Contain("3");
    }
}
