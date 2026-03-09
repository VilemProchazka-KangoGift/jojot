using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

/// <summary>
/// Additional RelayCommand tests targeting uncovered branches:
/// RelayCommand&lt;T&gt;.RaiseCanExecuteChanged.
/// </summary>
public class RelayCommandCoverageTests
{
    [Fact]
    public void RelayCommandT_RaiseCanExecuteChanged_FiresEvent()
    {
        var cmd = new RelayCommand<int>(i => { });
        bool fired = false;
        cmd.CanExecuteChanged += (_, _) => fired = true;

        cmd.RaiseCanExecuteChanged();

        fired.Should().BeTrue();
    }

    [Fact]
    public void RelayCommandT_RaiseCanExecuteChanged_NoSubscribers_DoesNotThrow()
    {
        var cmd = new RelayCommand<string>(s => { });
        cmd.RaiseCanExecuteChanged();
    }

    [Fact]
    public void RelayCommandT_CanExecute_WithNullCanExecuteFunc_ReturnsTrue()
    {
        var cmd = new RelayCommand<string>(s => { });
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RelayCommandT_CanExecute_WithFunc_DelegatesToFunc()
    {
        var cmd = new RelayCommand<int>(i => { }, i => i > 0);
        cmd.CanExecute(5).Should().BeTrue();
        cmd.CanExecute(-1).Should().BeFalse();
    }

    [Fact]
    public void RelayCommandT_Execute_PassesParameter()
    {
        int received = 0;
        var cmd = new RelayCommand<int>(i => received = i);

        cmd.Execute(42);

        received.Should().Be(42);
    }

    [Fact]
    public void RelayCommandT_ThrowsOnNullAction()
    {
        Action act = () => new RelayCommand<int>(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
