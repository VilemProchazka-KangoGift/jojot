using System.Reflection;
using JoJot.Interop;

namespace JoJot.Tests.Interop;

/// <summary>
/// Pins the vtable layout of the undocumented COM interfaces. For [ComImport] interfaces the
/// declaration order IS the vtable, so a reorder, insertion or extra parameter silently shifts
/// every call. These tests make such an edit fail loudly.
/// </summary>
public class ComInterfaceLayoutTests
{
    private static string[] DeclaredMethodNames(Type type) =>
        [.. type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .OrderBy(m => m.MetadataToken)
            .Select(m => m.Name)];

    [Fact]
    public void IVirtualDesktopNotification_Has24H2VtableOrder()
    {
        DeclaredMethodNames(typeof(IVirtualDesktopNotification)).Should().Equal(
            "VirtualDesktopCreated",
            "VirtualDesktopDestroyBegin",
            "VirtualDesktopDestroyFailed",
            "VirtualDesktopDestroyed",
            "VirtualDesktopMoved",
            "VirtualDesktopNameChanged",
            "ViewVirtualDesktopChanged",
            "CurrentVirtualDesktopChanged",
            "VirtualDesktopWallpaperChanged",
            "VirtualDesktopSwitched",
            "RemoteVirtualDesktopConnected");
    }

    [Fact]
    public void IVirtualDesktopNotification_GetMethodsOrderIsDeclarationOrder()
    {
        // The task-level guarantee: plain GetMethods() also returns declaration order.
        typeof(IVirtualDesktopNotification).GetMethods().Select(m => m.Name)
            .Should().Equal(DeclaredMethodNames(typeof(IVirtualDesktopNotification)));
    }

    [Fact]
    public void IVirtualDesktopNotification_HasNoMonitorsParameter()
    {
        var offenders = typeof(IVirtualDesktopNotification).GetMethods()
            .SelectMany(m => m.GetParameters().Select(p => (Method: m.Name, Param: p.Name)))
            .Where(x => string.Equals(x.Param, "monitors", StringComparison.OrdinalIgnoreCase))
            .ToList();

        offenders.Should().BeEmpty("the 24H2+ callback vtable has no monitors parameter");
    }

    [Fact]
    public void IVirtualDesktopNotification_ParameterShapesMatch24H2()
    {
        var methods = typeof(IVirtualDesktopNotification).GetMethods().ToDictionary(m => m.Name);

        Types(methods["VirtualDesktopCreated"]).Should().Equal(typeof(IVirtualDesktop));
        Types(methods["VirtualDesktopDestroyBegin"]).Should().Equal(typeof(IVirtualDesktop), typeof(IVirtualDesktop));
        Types(methods["VirtualDesktopDestroyFailed"]).Should().Equal(typeof(IVirtualDesktop), typeof(IVirtualDesktop));
        Types(methods["VirtualDesktopDestroyed"]).Should().Equal(typeof(IVirtualDesktop), typeof(IVirtualDesktop));
        Types(methods["VirtualDesktopMoved"]).Should().Equal(typeof(IVirtualDesktop), typeof(long), typeof(long));
        Types(methods["VirtualDesktopNameChanged"]).Should().Equal(typeof(IVirtualDesktop), typeof(string));
        Types(methods["ViewVirtualDesktopChanged"]).Should().Equal(typeof(IntPtr));
        Types(methods["CurrentVirtualDesktopChanged"]).Should().Equal(typeof(IVirtualDesktop), typeof(IVirtualDesktop));
        Types(methods["VirtualDesktopWallpaperChanged"]).Should().Equal(typeof(IVirtualDesktop), typeof(string));
        Types(methods["VirtualDesktopSwitched"]).Should().Equal(typeof(IVirtualDesktop));
        Types(methods["RemoteVirtualDesktopConnected"]).Should().Equal(typeof(IVirtualDesktop));

        methods.Values.Should().OnlyContain(m => m.ReturnType == typeof(int), "every callback is [PreserveSig] int");

        static Type[] Types(MethodInfo m) => [.. m.GetParameters().Select(p => p.ParameterType)];
    }

    [Fact]
    public void IVirtualDesktop_HasFourMethodVtable()
    {
        DeclaredMethodNames(typeof(IVirtualDesktop)).Should().Equal(
            "IsViewVisible",
            "GetId",
            "GetName",
            "GetWallpaperPath");
    }

    [Fact]
    public void IVirtualDesktopNotificationService_HasRegisterThenUnregister()
    {
        DeclaredMethodNames(typeof(IVirtualDesktopNotificationService)).Should().Equal("Register", "Unregister");
    }
}
