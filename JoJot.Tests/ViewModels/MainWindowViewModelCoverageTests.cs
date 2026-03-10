using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

/// <summary>
/// Additional MainWindowViewModel tests targeting uncovered branches:
/// DesktopGuid setter, SearchText getter.
/// </summary>
public class MainWindowViewModelCoverageTests
{
    [Fact]
    public void DesktopGuid_Setter_UpdatesValue()
    {
        var vm = new MainWindowViewModel("original-guid");
        vm.DesktopGuid.Should().Be("original-guid");

        vm.DesktopGuid = "new-guid";
        vm.DesktopGuid.Should().Be("new-guid");
    }

    [Fact]
    public void DesktopGuid_Setter_RaisesPropertyChanged()
    {
        var vm = new MainWindowViewModel("original");
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.DesktopGuid = "changed";

        raised.Should().Contain(nameof(MainWindowViewModel.DesktopGuid));
    }

    [Fact]
    public void SearchText_Getter_ReturnsCurrentValue()
    {
        var vm = new MainWindowViewModel("desk-1");
        vm.SearchText.Should().Be("");

        vm.SearchText = "hello";
        vm.SearchText.Should().Be("hello");
    }

    [Fact]
    public void DesktopGuid_SameValue_DoesNotRaise()
    {
        var vm = new MainWindowViewModel("same-guid");
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.DesktopGuid = "same-guid";

        raised.Should().NotContain(nameof(MainWindowViewModel.DesktopGuid));
    }
}
