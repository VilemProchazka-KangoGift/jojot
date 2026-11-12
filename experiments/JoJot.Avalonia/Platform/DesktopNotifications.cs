using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JoJot.AvaloniaExperiment.Platform;

// Windows 11 24H2 notification ABI. The listener is an explicit unmanaged COM object;
// the shell never receives an RCW or an unmanaged pointer to a managed object.
internal sealed unsafe partial class DesktopNotifications : IDisposable
{
    private static readonly Guid CallbackId = new("B9E5E94D-233E-49AB-AF5C-2B4541C3AADE");
    private static readonly Guid UnknownId = new("00000000-0000-0000-C000-000000000046");
    private static readonly Lock Gate = new();
    private static Action? _changed;
    private static nint _listener;
    private nint _service;
    private uint _cookie;
    private bool _registered;
    private bool _initialized;

    internal static DesktopNotifications? TryRegister(Action changed)
    {
        if (!OperatingSystem.IsWindows() || Environment.OSVersion.Version.Build < 26100) return null;
        var result = new DesktopNotifications();
        result._initialized = CoInitializeEx(0, 2) >= 0;
        nint provider = 0;
        try
        {
            var clsid = new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239");
            var providerId = new Guid("6D5140C1-7436-11CE-8034-00AA006009FA");
            if (CoCreateInstance(&clsid, 0, 21, &providerId, &provider) < 0 || provider == 0) return null;
            var serviceId = new Guid("A501FDEC-4A09-464C-AE4E-1B9C21B84918");
            var interfaceId = new Guid("0CD45E71-D927-4F15-8B0A-8FEF525337BF");
            var query = (delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)(*(nint**)provider)[3];
            nint service = 0;
            if (query(provider, &serviceId, &interfaceId, &service) < 0 || service == 0) return null;
            result._service = service;
            lock (Gate)
            {
                if (_changed is not null) return null;
                _changed = changed;
                EnsureListener();
                uint cookie = 0;
                var register = (delegate* unmanaged[Stdcall]<nint, nint, uint*, int>)(*(nint**)service)[3];
                if (register(service, _listener, &cookie) < 0)
                {
                    _changed = null;
                    return null;
                }
                result._cookie = cookie;
                result._registered = true;
            }
            return result;
        }
        finally
        {
            Release(provider);
            if (!result._registered) result.Dispose();
        }
    }

    private static void EnsureListener()
    {
        if (_listener != 0) return;
        // Stable for the process lifetime, including callbacks racing unregister.
        var table = (nint*)NativeMemory.AllocZeroed((nuint)(14 * sizeof(nint)));
        table[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface;
        table[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef;
        table[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&ReleaseRef;
        for (var i = 3; i < 14; i++) table[i] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int>)&Notify;
        var obj = (nint*)NativeMemory.AllocZeroed((nuint)(2 * sizeof(nint)));
        obj[0] = (nint)table;
        obj[1] = 1;
        _listener = (nint)obj;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(nint self, Guid* iid, nint* output)
    {
        if (output == null || iid == null) return unchecked((int)0x80004003);
        *output = 0;
        if (*iid != UnknownId && *iid != CallbackId) return unchecked((int)0x80004002);
        *output = self;
        Interlocked.Increment(ref ((int*)self)[2]);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(nint self) => (uint)Interlocked.Increment(ref ((int*)self)[2]);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint ReleaseRef(nint self) => (uint)Interlocked.Decrement(ref ((int*)self)[2]);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Notify(nint self, nint first, nint second, nint third)
    {
        try { Volatile.Read(ref _changed)?.Invoke(); }
        catch { /* Never let an exception cross the native callback boundary. */ }
        return 0;
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_registered && _service != 0)
            {
                var unregister = (delegate* unmanaged[Stdcall]<nint, uint, int>)(*(nint**)_service)[4];
                unregister(_service, _cookie);
                _cookie = 0;
                _registered = false;
                _changed = null;
            }
        }
        Release(_service);
        _service = 0;
        if (_initialized) { CoUninitialize(); _initialized = false; }
    }

    private static void Release(nint ptr)
    {
        if (ptr != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)ptr)[2])(ptr);
    }
    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* result);
    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint flags);
    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();
}
