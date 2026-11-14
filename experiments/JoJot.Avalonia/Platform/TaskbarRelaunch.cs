using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using JoJot.Services;

namespace JoJot.AvaloniaExperiment.Platform;

// Window properties must be cleared while the HWND still exists. The explicit ID
// also keeps separate experiment data directories in separate taskbar groups.
internal static unsafe partial class TaskbarRelaunch
{
    private static readonly Guid PropertyFormat = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private static readonly Guid PropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    internal static void Attach(Window window, string directory, bool production = false)
    {
        // Production keeps the path-based identity used by the WPF shortcuts.
        if (production || !OperatingSystem.IsWindows()) return;
        nint handle = 0;
        window.Opened += (_, _) =>
        {
            if (window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND", Handle: not 0 } native) return;
            handle = native.Handle;
            try { SetProperties(handle, BuildCommand(Environment.ProcessPath!, Environment.GetCommandLineArgs()[0], directory), BuildId(directory)); }
            catch (Exception ex) { LogService.Warn("Could not configure taskbar relaunch", ex); }
        };
        window.Closing += (_, e) =>
        {
            if (e.Cancel || handle == 0) return;
            try { ClearProperties(handle); }
            catch (Exception ex) { LogService.Warn("Could not clear taskbar relaunch properties", ex); }
            handle = 0;
        };
    }

    internal static string BuildId(string directory)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())))[..24];
#if DEBUG
        return $"JoJot.Avalonia.Dev.{hash}";
#else
        return $"JoJot.Avalonia.{hash}";
#endif
    }

    internal static string BuildCommand(string executable, string entryAssembly, string directory)
    {
        var host = string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"{Quote(executable)} {Quote(entryAssembly)}" : Quote(executable);
        return $"{host} --data-dir {Quote(directory)}";
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"')
                result.Append('\\', slashes * 2 + 1);
            else
                result.Append('\\', slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    internal static void SetProperties(nint hwnd, string command, string id)
    {
        var store = OpenStore(hwnd);
        try
        {
            SetString(store, 2, command);
            SetString(store, 4, "JoJot Avalonia Experiment");
            SetString(store, 5, id); // ID last: this triggers the taskbar refresh.
        }
        catch { ClearStore(store); throw; }
        finally { Release(store); }
    }

    internal static void ClearProperties(nint hwnd)
    {
        var store = OpenStore(hwnd);
        try { ClearStore(store); }
        finally { Release(store); }
    }

    private static void ClearStore(nint store)
    {
        var empty = default(PropVariant);
        foreach (var property in new uint[] { 5, 2, 4 }) SetValue(store, property, &empty);
    }

    private static void SetString(nint store, uint property, string value)
    {
        var variant = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(value) };
        try { SetValue(store, property, &variant); }
        finally { PropVariantClear(&variant); }
    }

    private static void SetValue(nint store, uint property, PropVariant* value)
    {
        var key = new PropertyKey { Format = PropertyFormat, Id = property };
        var set = (delegate* unmanaged[Stdcall]<nint, PropertyKey*, PropVariant*, int>)(*(nint**)store)[6];
        Marshal.ThrowExceptionForHR(set(store, &key, value));
    }

    private static nint OpenStore(nint hwnd)
    {
        var iid = PropertyStoreId;
        nint store = 0;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, &iid, &store));
        return store;
    }

    private static void Release(nint store) => ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)store)[2])(store);

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { internal Guid Format; internal uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] internal ushort Type; [FieldOffset(8)] internal nint Pointer; }

    [LibraryImport("shell32.dll")] private static partial int SHGetPropertyStoreForWindow(nint hwnd, Guid* iid, nint* store);
    [LibraryImport("ole32.dll")] private static partial int PropVariantClear(PropVariant* variant);
}
