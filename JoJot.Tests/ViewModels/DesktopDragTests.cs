using JoJot.ViewModels;

namespace JoJot.Tests.ViewModels;

public class DesktopDragTests
{
    private static MainWindowViewModel CreateVm(string guid = "desktop-aaa") => new(guid);

    // ─── Default State ───────────────────────────────────────────────

    [Fact]
    public void DragState_DefaultsToInactive()
    {
        var vm = CreateVm();

        vm.IsDragOverlayActive.Should().BeFalse();
        vm.DragFromDesktopGuid.Should().BeNull();
        vm.DragToDesktopGuid.Should().BeNull();
        vm.DragToDesktopName.Should().BeNull();
        vm.IsMisplaced.Should().BeFalse();
    }

    // ─── EvaluateDrag ────────────────────────────────────────────────

    [Fact]
    public void EvaluateDrag_ReturnsShowNew_WhenInactive()
    {
        var vm = CreateVm();

        vm.EvaluateDrag("desktop-bbb").Should().Be(MainWindowViewModel.DragAction.ShowNew);
    }

    [Fact]
    public void EvaluateDrag_ReturnsDismiss_WhenMovedBackToOrigin()
    {
        var vm = CreateVm();
        vm.BeginDrag("desktop-aaa", "desktop-bbb", "Desktop 2");

        vm.EvaluateDrag("desktop-aaa").Should().Be(MainWindowViewModel.DragAction.Dismiss);
    }

    [Fact]
    public void EvaluateDrag_ReturnsDismiss_CaseInsensitive()
    {
        var vm = CreateVm();
        vm.BeginDrag("Desktop-AAA", "desktop-bbb", "Desktop 2");

        vm.EvaluateDrag("desktop-aaa").Should().Be(MainWindowViewModel.DragAction.Dismiss);
    }

    [Fact]
    public void EvaluateDrag_ReturnsNoOp_WhenSameTarget()
    {
        var vm = CreateVm();
        vm.BeginDrag("desktop-aaa", "desktop-bbb", "Desktop 2");

        vm.EvaluateDrag("desktop-bbb").Should().Be(MainWindowViewModel.DragAction.NoOp);
    }

    [Fact]
    public void EvaluateDrag_ReturnsNoOp_CaseInsensitive()
    {
        var vm = CreateVm();
        vm.BeginDrag("desktop-aaa", "Desktop-BBB", "Desktop 2");

        vm.EvaluateDrag("desktop-bbb").Should().Be(MainWindowViewModel.DragAction.NoOp);
    }

    [Fact]
    public void EvaluateDrag_ReturnsUpdateTarget_WhenDifferentTarget()
    {
        var vm = CreateVm();
        vm.BeginDrag("desktop-aaa", "desktop-bbb", "Desktop 2");

        vm.EvaluateDrag("desktop-ccc").Should().Be(MainWindowViewModel.DragAction.UpdateTarget);
    }

    // ─── BeginDrag ───────────────────────────────────────────────────

    [Fact]
    public void BeginDrag_SetsAllState()
    {
        var vm = CreateVm();

        vm.BeginDrag("desktop-aaa", "desktop-bbb", "Desktop 2");

        vm.IsDragOverlayActive.Should().BeTrue();
        vm.DragFromDesktopGuid.Should().Be("desktop-aaa");
        vm.DragToDesktopGuid.Should().Be("desktop-bbb");
        vm.DragToDesktopName.Should().Be("Desktop 2");
    }

    [Fact]
    public void BeginDrag_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        using var monitor = vm.Monitor();

        vm.BeginDrag("desktop-aaa", "desktop-bbb", "Desktop 2");

        monitor.Should().RaisePropertyChangeFor(x => x.IsDragOverlayActive);
        monitor.Should().RaisePropertyChangeFor(x => x.DragFromDesktopGuid);
        monitor.Should().RaisePropertyChangeFor(x => x.DragToDesktopGuid);
        monitor.Should().RaisePropertyChangeFor(x => x.DragToDesktopName);
    }

    // ─── UpdateDragTarget ────────────────────────────────────────────

    [Fact]
    public void UpdateDragTarget_UpdatesTargetOnly()
    {
        var vm = CreateVm();
        vm.BeginDrag("desktop-aaa", "desktop-bbb", "Desktop 2");

        vm.UpdateDragTarget("desktop-ccc", "Desktop 3");

        vm.DragToDesktopGuid.Should().Be("desktop-ccc");
        vm.DragToDesktopName.Should().Be("Desktop 3");
        // From guid unchanged
        vm.DragFromDesktopGuid.Should().Be("desktop-aaa");
        vm.IsDragOverlayActive.Should().BeTrue();
    }

    // ─── ResetDragState ──────────────────────────────────────────────

    [Fact]
    public void ResetDragState_ClearsEverything()
    {
        var vm = CreateVm();
        vm.BeginDrag("desktop-aaa", "desktop-bbb", "Desktop 2");

        vm.ResetDragState();

        vm.IsDragOverlayActive.Should().BeFalse();
        vm.DragFromDesktopGuid.Should().BeNull();
        vm.DragToDesktopGuid.Should().BeNull();
        vm.DragToDesktopName.Should().BeNull();
    }

    [Fact]
    public void ResetDragState_DoesNotClearIsMisplaced()
    {
        var vm = CreateVm();
        vm.IsMisplaced = true;
        vm.BeginDrag("desktop-aaa", "desktop-bbb", "Desktop 2");

        vm.ResetDragState();

        vm.IsMisplaced.Should().BeTrue();
    }

    // ─── IsMisplacedOnDesktop ────────────────────────────────────────

    [Fact]
    public void IsMisplacedOnDesktop_ReturnsFalse_WhenMatches()
    {
        var vm = CreateVm("desktop-aaa");

        vm.IsMisplacedOnDesktop("desktop-aaa").Should().BeFalse();
    }

    [Fact]
    public void IsMisplacedOnDesktop_ReturnsFalse_CaseInsensitive()
    {
        var vm = CreateVm("Desktop-AAA");

        vm.IsMisplacedOnDesktop("desktop-aaa").Should().BeFalse();
    }

    [Fact]
    public void IsMisplacedOnDesktop_ReturnsTrue_WhenDifferent()
    {
        var vm = CreateVm("desktop-aaa");

        vm.IsMisplacedOnDesktop("desktop-bbb").Should().BeTrue();
    }

    // ─── IsMisplaced Property ────────────────────────────────────────

    [Fact]
    public void IsMisplaced_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        using var monitor = vm.Monitor();

        vm.IsMisplaced = true;

        monitor.Should().RaisePropertyChangeFor(x => x.IsMisplaced);
    }

    // ─── State Machine Integration ───────────────────────────────────

    [Fact]
    public void FullDragCycle_ShowNew_ThenDismiss()
    {
        var vm = CreateVm("desktop-aaa");

        // First move: show new
        vm.EvaluateDrag("desktop-bbb").Should().Be(MainWindowViewModel.DragAction.ShowNew);
        vm.BeginDrag("desktop-aaa", "desktop-bbb", "Desktop 2");

        // Move back: dismiss
        vm.EvaluateDrag("desktop-aaa").Should().Be(MainWindowViewModel.DragAction.Dismiss);
        vm.ResetDragState();

        // State fully cleared
        vm.IsDragOverlayActive.Should().BeFalse();
    }

    [Fact]
    public void FullDragCycle_ShowNew_UpdateTarget_Dismiss()
    {
        var vm = CreateVm("desktop-aaa");

        // First move
        vm.EvaluateDrag("desktop-bbb").Should().Be(MainWindowViewModel.DragAction.ShowNew);
        vm.BeginDrag("desktop-aaa", "desktop-bbb", "Desktop 2");

        // Move to different target
        vm.EvaluateDrag("desktop-ccc").Should().Be(MainWindowViewModel.DragAction.UpdateTarget);
        vm.UpdateDragTarget("desktop-ccc", "Desktop 3");

        // Same target again: no-op
        vm.EvaluateDrag("desktop-ccc").Should().Be(MainWindowViewModel.DragAction.NoOp);

        // Move back to origin: dismiss
        vm.EvaluateDrag("desktop-aaa").Should().Be(MainWindowViewModel.DragAction.Dismiss);
    }
}
