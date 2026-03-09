using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

public class ObservableObjectTests
{
    private sealed class TestObject : ObservableObject
    {
        private string _name = "";
        private int _count;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public int Count
        {
            get => _count;
            set => SetProperty(ref _count, value);
        }

        // Property with dependent notifications
        private string _firstName = "";
        public string FirstName
        {
            get => _firstName;
            set => SetProperty(ref _firstName, value, [nameof(FullName)]);
        }

        public string FullName => $"{FirstName} Doe";
    }

    // ─── SetProperty ─────────────────────────────────────────────────

    [Fact]
    public void SetProperty_RaisesPropertyChanged_WhenValueDiffers()
    {
        var obj = new TestObject();
        var raised = new List<string>();
        obj.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        obj.Name = "hello";

        raised.Should().ContainSingle().Which.Should().Be(nameof(TestObject.Name));
    }

    [Fact]
    public void SetProperty_DoesNotRaise_WhenValueIsSame()
    {
        var obj = new TestObject { Name = "hello" };
        var raised = new List<string>();
        obj.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        obj.Name = "hello";

        raised.Should().BeEmpty();
    }

    [Fact]
    public void SetProperty_UpdatesBackingField()
    {
        var obj = new TestObject();

        obj.Name = "test";

        obj.Name.Should().Be("test");
    }

    [Fact]
    public void SetProperty_ReturnsTrueOnChange_FalseOnSame()
    {
        var obj = new TestObject();

        // First set — changes
        obj.Name = "a";
        // We can't test return directly since it's a property setter,
        // but we verify the field changed
        obj.Name.Should().Be("a");

        // Set same — no change expected
        var raised = new List<string>();
        obj.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);
        obj.Name = "a";
        raised.Should().BeEmpty();
    }

    [Fact]
    public void SetProperty_WorksWithValueTypes()
    {
        var obj = new TestObject();
        var raised = new List<string>();
        obj.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        obj.Count = 42;

        raised.Should().ContainSingle().Which.Should().Be(nameof(TestObject.Count));
        obj.Count.Should().Be(42);
    }

    [Fact]
    public void SetProperty_ValueType_DoesNotRaise_WhenSame()
    {
        var obj = new TestObject { Count = 5 };
        var raised = new List<string>();
        obj.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        obj.Count = 5;

        raised.Should().BeEmpty();
    }

    // ─── Dependent Properties ────────────────────────────────────────

    [Fact]
    public void SetProperty_RaisesDependentProperties()
    {
        var obj = new TestObject();
        var raised = new List<string>();
        obj.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        obj.FirstName = "Jane";

        raised.Should().HaveCount(2);
        raised.Should().Contain(nameof(TestObject.FirstName));
        raised.Should().Contain(nameof(TestObject.FullName));
    }

    [Fact]
    public void SetProperty_DependentNotRaised_WhenValueSame()
    {
        var obj = new TestObject { FirstName = "Jane" };
        var raised = new List<string>();
        obj.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        obj.FirstName = "Jane";

        raised.Should().BeEmpty();
    }

    // ─── No subscribers ─────────────────────────────────────────────

    [Fact]
    public void SetProperty_DoesNotThrow_WhenNoSubscribers()
    {
        var obj = new TestObject();

        var act = () => obj.Name = "safe";

        act.Should().NotThrow();
    }
}
