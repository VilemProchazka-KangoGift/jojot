using JoJot.Interop;
using JoJot.Services;

namespace JoJot.Tests.Interop;

public class ComGuidsTests
{
    public ComGuidsTests()
    {
        LogService.InitializeNoop();
    }

    [Fact]
    public void Resolve_BelowMinimumBuild_ReturnsNull()
    {
        ComGuids.Resolve(22620).Should().BeNull();
    }

    [Fact]
    public void Resolve_Windows10Build_ReturnsNull()
    {
        ComGuids.Resolve(19045).Should().BeNull();
    }

    [Fact]
    public void Resolve_ZeroBuild_ReturnsNull()
    {
        ComGuids.Resolve(0).Should().BeNull();
    }

    [Fact]
    public void Resolve_NegativeBuild_ReturnsNull()
    {
        ComGuids.Resolve(-1).Should().BeNull();
    }

    [Fact]
    public void Resolve_ExactMinimumBuild22621_ReturnsGuidSet()
    {
        var result = ComGuids.Resolve(22621);
        result.Should().NotBeNull();
        result!.IVirtualDesktop.Should().Be(new Guid("3F07F4BE-B107-441A-AF0F-39D82529072C"));
    }

    [Fact]
    public void Resolve_Build22631_UsesClosestLower22621()
    {
        // 22631 (23H2) should use the 22621 entry
        var result = ComGuids.Resolve(22631);
        result.Should().NotBeNull();
        result!.IVirtualDesktopManagerInternal.Should().Be(new Guid("B2F925B9-5A0F-4D2E-9F4D-2B1507593C10"));
    }

    [Fact]
    public void Resolve_ExactBuild26100_Returns24H2GuidSet()
    {
        var result = ComGuids.Resolve(26100);
        result.Should().NotBeNull();
        result!.IVirtualDesktopManagerInternal.Should().Be(new Guid("53F5CA0B-158F-4124-900C-057158060B27"));
    }

    [Fact]
    public void Resolve_BuildAbove26100_Uses26100Entry()
    {
        var result = ComGuids.Resolve(26200);
        result.Should().NotBeNull();
        result!.IVirtualDesktopNotification.Should().Be(new Guid("C179334C-4295-40D3-BEA1-C654D965605A"));
    }

    [Fact]
    public void Resolve_BuildBetween22621And26100_Uses22621Entry()
    {
        // A build between the two entries should use the 22621 set
        var result = ComGuids.Resolve(25000);
        result.Should().NotBeNull();
        result!.IVirtualDesktopNotificationService.Should().Be(new Guid("0CD45DE4-2F0F-4211-ACE2-1B3C7C750E13"));
    }

    [Fact]
    public void Resolve_GuidSetHasFourDistinctGuids()
    {
        var result = ComGuids.Resolve(22621)!;
        var guids = new[] { result.IVirtualDesktop, result.IVirtualDesktopManagerInternal, result.IVirtualDesktopNotification, result.IVirtualDesktopNotificationService };
        guids.Distinct().Count().Should().Be(4);
    }

    // ─── Static CLSIDs ──────────────────────────────────────────────

    [Fact]
    public void CLSID_ImmersiveShell_IsExpectedValue()
    {
        ComGuids.CLSID_ImmersiveShell.Should().Be(new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239"));
    }

    [Fact]
    public void CLSID_VirtualDesktopManagerInternal_IsExpectedValue()
    {
        ComGuids.CLSID_VirtualDesktopManagerInternal.Should().Be(new Guid("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B"));
    }

    [Fact]
    public void CLSID_VirtualDesktopManager_IsExpectedValue()
    {
        ComGuids.CLSID_VirtualDesktopManager.Should().Be(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"));
    }

    [Fact]
    public void IID_IVirtualDesktopManager_IsExpectedValue()
    {
        ComGuids.IID_IVirtualDesktopManager.Should().Be(new Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"));
    }
}
