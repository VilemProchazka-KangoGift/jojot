using JoJot.Services;

namespace JoJot.Tests.Services;

/// <summary>
/// Tests for the polling fallback tick (<see cref="VirtualDesktopService.PollDesktopChange"/>).
/// Runs in the Database collection because it mutates VirtualDesktopService's static
/// current-desktop state, which StartupServiceTests reads; the state is restored after each test.
/// </summary>
[Collection("Database")]
public sealed class VirtualDesktopPollingTests : IDisposable
{
    private readonly string _savedGuid;
    private readonly string _savedName;
    private readonly List<(string Old, string New)> _switches = [];

    public VirtualDesktopPollingTests()
    {
        LogService.InitializeNoop();
        _savedGuid = VirtualDesktopService.CurrentDesktopGuid;
        _savedName = VirtualDesktopService.CurrentDesktopName;
        VirtualDesktopService.CurrentDesktopChanged += OnSwitched;
    }

    public void Dispose()
    {
        VirtualDesktopService.CurrentDesktopChanged -= OnSwitched;
        VirtualDesktopService.SetCurrentDesktopState(_savedGuid, _savedName, 0);
    }

    private void OnSwitched(string oldGuid, string newGuid) => _switches.Add((oldGuid, newGuid));

    [Fact]
    public void PollingInterval_Is2000Ms()
    {
        VirtualDesktopService.PollingIntervalMs.Should().Be(2000);
    }

    [Fact]
    public void Poll_SameDesktop_DoesNotFetchDetailsOrFireEvent()
    {
        var current = Guid.NewGuid();
        VirtualDesktopService.SetCurrentDesktopState(current.ToString(), "Work", 1);
        int detailCalls = 0;

        bool changed = VirtualDesktopService.PollDesktopChange(
            () => current,
            () => { detailCalls++; return (current, "Work", 1); });

        changed.Should().BeFalse();
        detailCalls.Should().Be(0, "the poll compares only the GUID");
        _switches.Should().BeEmpty();
        VirtualDesktopService.CurrentDesktopName.Should().Be("Work");
    }

    [Fact]
    public void Poll_GuidComparisonIsCaseInsensitive()
    {
        var current = Guid.NewGuid();
        VirtualDesktopService.SetCurrentDesktopState(current.ToString().ToUpperInvariant(), "Work", 1);

        bool changed = VirtualDesktopService.PollDesktopChange(
            () => current,
            () => throw new InvalidOperationException("must not be called"));

        changed.Should().BeFalse();
        _switches.Should().BeEmpty();
    }

    [Fact]
    public void Poll_DesktopChanged_FetchesDetailsOnceUpdatesStateAndFiresEvent()
    {
        var oldId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        VirtualDesktopService.SetCurrentDesktopState(oldId.ToString(), "Old", 0);
        int detailCalls = 0;

        bool changed = VirtualDesktopService.PollDesktopChange(
            () => newId,
            () => { detailCalls++; return (newId, "New", 2); });

        changed.Should().BeTrue();
        detailCalls.Should().Be(1);
        VirtualDesktopService.CurrentDesktopGuid.Should().Be(newId.ToString());
        VirtualDesktopService.CurrentDesktopName.Should().Be("New");
        _switches.Should().Equal((oldId.ToString(), newId.ToString()));
    }

    [Fact]
    public void Poll_DetailFetchFails_StillSwitchesGuidAndFiresEvent()
    {
        var oldId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        VirtualDesktopService.SetCurrentDesktopState(oldId.ToString(), "Old", 0);

        bool changed = VirtualDesktopService.PollDesktopChange(
            () => newId,
            () => throw new InvalidOperationException("COM hiccup"));

        changed.Should().BeTrue();
        VirtualDesktopService.CurrentDesktopGuid.Should().Be(newId.ToString());
        _switches.Should().ContainSingle();
    }

    [Fact]
    public void Poll_IdQueryThrows_ReturnsFalseAndKeepsState()
    {
        var current = Guid.NewGuid();
        VirtualDesktopService.SetCurrentDesktopState(current.ToString(), "Work", 1);

        bool changed = VirtualDesktopService.PollDesktopChange(
            () => throw new InvalidOperationException("COM unavailable"),
            () => (current, "Work", 1));

        changed.Should().BeFalse();
        VirtualDesktopService.CurrentDesktopGuid.Should().Be(current.ToString());
        _switches.Should().BeEmpty();
    }
}
