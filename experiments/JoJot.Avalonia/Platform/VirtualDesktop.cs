using System.Runtime.InteropServices;

namespace JoJot.AvaloniaExperiment.Platform;

// Raw COM calls avoid runtime-generated COM wrappers, which are unavailable under NativeAOT.
internal static unsafe partial class VirtualDesktop
{
    private static readonly Guid Shell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid ServiceProvider = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid ManagerService = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    private static readonly Guid DesktopInterface = new("3F07F4BE-B107-441A-AF0F-39D82529072C");

    private static IReadOnlyList<Desktop> EnumerateDesktops()
    {
        if (!OperatingSystem.IsWindows() || Environment.OSVersion.Version.Build < 22621)
            return [new Desktop("default", "", 0)];
        var iid = Environment.OSVersion.Version.Build >= 26100
            ? new Guid("53F5CA0B-158F-4124-900C-057158060B27")
            : new Guid("B2F925B9-5A0F-4D2E-9F4D-2B1507593C10");
        var initialized = CoInitializeEx(0, 2) >= 0;
        nint provider = 0, manager = 0, array = 0;
        try
        {
            var clsid = Shell;
            var providerIid = ServiceProvider;
            if (CoCreateInstance(&clsid, 0, 21, &providerIid, &provider) < 0 || provider == 0)
                return [new Desktop("default", "", 0)];
            var service = ManagerService;
            var query = (delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)(*(nint**)provider)[3];
            if (query(provider, &service, &iid, &manager) < 0 || manager == 0)
                return [new Desktop("default", "", 0)];
            var getDesktops = (delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)manager)[7];
            if (getDesktops(manager, &array) < 0 || array == 0)
                return [new Desktop("default", "", 0)];
            var arrayVtable = *(nint**)array;
            var getCount = (delegate* unmanaged[Stdcall]<nint, uint*, int>)arrayVtable[3];
            var getAt = (delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)arrayVtable[4];
            uint count = 0;
            if (getCount(array, &count) < 0 || count > 1024)
                return [new Desktop("default", "", 0)];
            var result = new List<Desktop>();
            for (uint index = 0; index < count; index++)
            {
                nint desktop = 0;
                var desktopIid = DesktopInterface;
                try
                {
                    if (getAt(array, index, &desktopIid, &desktop) < 0 || desktop == 0)
                        continue;
                    var getId = (delegate* unmanaged[Stdcall]<nint, Guid*, int>)(*(nint**)desktop)[4];
                    Guid id = default;
                    if (getId(desktop, &id) >= 0 && id != Guid.Empty)
                        result.Add(new Desktop(id.ToString(), GetName(desktop, id), (int)index));
                }
                finally { Release(desktop); }
            }
            return result.Count == 0 ? [new Desktop("default", "", 0)] : result;
        }
        finally
        {
            Release(array);
            Release(manager);
            Release(provider);
            if (initialized) CoUninitialize();
        }
    }

    internal static string GetCurrentDesktopOrDefault()
    {
        if (!OperatingSystem.IsWindows() || Environment.OSVersion.Version.Build < 22621)
            return "default";
        var iid = Environment.OSVersion.Version.Build >= 26100
            ? new Guid("53F5CA0B-158F-4124-900C-057158060B27")
            : new Guid("B2F925B9-5A0F-4D2E-9F4D-2B1507593C10");
        nint provider = 0, manager = 0, desktop = 0;
        var initialized = CoInitializeEx(0, 2) >= 0;
        try
        {
            var clsid = Shell;
            var providerIid = ServiceProvider;
            if (CoCreateInstance(&clsid, 0, 21, &providerIid, &provider) < 0 || provider == 0)
                return "default";
            var service = ManagerService;
            var providerVtable = *(nint**)provider;
            var queryService = (delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)providerVtable[3];
            if (queryService(provider, &service, &iid, &manager) < 0 || manager == 0)
                return "default";
            var managerVtable = *(nint**)manager;
            var current = (delegate* unmanaged[Stdcall]<nint, nint*, int>)managerVtable[6];
            if (current(manager, &desktop) < 0 || desktop == 0)
                return "default";
            var desktopVtable = *(nint**)desktop;
            var getId = (delegate* unmanaged[Stdcall]<nint, Guid*, int>)desktopVtable[4];
            Guid id = default;
            return getId(desktop, &id) >= 0 && id != Guid.Empty ? id.ToString() : "default";
        }
        finally
        {
            Release(desktop);
            Release(manager);
            Release(provider);
            if (initialized)
                CoUninitialize();
        }
    }

    internal sealed record Desktop(string Id, string Name, int Index);

    internal static IReadOnlyList<Desktop> GetDesktops() => EnumerateDesktops();
    internal static IReadOnlyList<string> GetDesktopIds() => GetDesktops().Select(d => d.Id).ToArray();

    private static string GetName(nint desktop, Guid id)
    {
        nint name = 0;
        try
        {
            var getName = (delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)desktop)[5];
            if (getName(desktop, &name) >= 0 && name != 0)
            {
                uint length = 0;
                var chars = WindowsGetStringRawBuffer(name, &length);
                if (chars != null && length > 0) return new string(chars, 0, checked((int)length));
            }
        }
        finally { if (name != 0) WindowsDeleteString(name); }
        return ReadName(id.ToString());
    }

    [LibraryImport("combase.dll")]
    private static partial char* WindowsGetStringRawBuffer(nint value, uint* length);
    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint value);

    private static string ReadName(string id)
    {
        if (!OperatingSystem.IsWindows() || !Guid.TryParse(id, out var guid)) return "";
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops\{guid:B}");
            return key?.GetValue("Name") as string ?? "";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return "";
        }
    }

    internal static string? GetWindowDesktopId(nint hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0) return null;
        var clsid = new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A");
        var iid = new Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B");
        nint manager = 0;
        var initialized = CoInitializeEx(0, 2) >= 0;
        try
        {
            if (CoCreateInstance(&clsid, 0, 21, &iid, &manager) < 0 || manager == 0) return null;
            Guid desktop = default;
            var getId = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, int>)(*(nint**)manager)[4];
            return getId(manager, hwnd, &desktop) >= 0 && desktop != Guid.Empty ? desktop.ToString() : null;
        }
        finally { Release(manager); if (initialized) CoUninitialize(); }
    }

    internal static bool TryMoveWindowToDesktop(nint hwnd, string id)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0 || !Guid.TryParse(id, out var desktop)) return false;
        var clsid = new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A");
        var iid = new Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B");
        nint manager = 0;
        var initialized = CoInitializeEx(0, 2) >= 0;
        try
        {
            if (CoCreateInstance(&clsid, 0, 21, &iid, &manager) < 0 || manager == 0) return false;
            var move = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, int>)(*(nint**)manager)[5];
            return move(manager, hwnd, &desktop) >= 0;
        }
        finally { Release(manager); if (initialized) CoUninitialize(); }
    }
    internal static bool TrySwitchToDesktop(string id)
    {
        if (!OperatingSystem.IsWindows() || Environment.OSVersion.Version.Build < 22621 || !Guid.TryParse(id, out var desktopId)) return false;
        var clsid = Shell;
        var providerId = ServiceProvider;
        var serviceId = ManagerService;
        var managerId = Environment.OSVersion.Version.Build >= 26100
            ? new Guid("53F5CA0B-158F-4124-900C-057158060B27")
            : new Guid("B2F925B9-5A0F-4D2E-9F4D-2B1507593C10");
        nint provider = 0, manager = 0, desktop = 0;
        var initialized = CoInitializeEx(0, 2) >= 0;
        try
        {
            if (CoCreateInstance(&clsid, 0, 21, &providerId, &provider) < 0 || provider == 0) return false;
            var query = (delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)(*(nint**)provider)[3];
            if (query(provider, &serviceId, &managerId, &manager) < 0 || manager == 0) return false;
            var find = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)manager)[14];
            if (find(manager, &desktopId, &desktop) < 0 || desktop == 0) return false;
            var change = (delegate* unmanaged[Stdcall]<nint, nint, int>)(*(nint**)manager)[9];
            return change(manager, desktop) >= 0;
        }
        finally { Release(desktop); Release(manager); Release(provider); if (initialized) CoUninitialize(); }
    }
    private static void Release(nint pointer)
    {
        if (pointer != 0)
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)pointer)[2])(pointer);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* result);
    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint flags);
    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();
}
