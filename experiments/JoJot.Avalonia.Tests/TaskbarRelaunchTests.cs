using System.Runtime.InteropServices;
using JoJot.AvaloniaExperiment.Platform;
using Xunit;

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class TaskbarRelaunchTests
{
    [Fact]
    public void Command_preserves_the_data_directory_and_hosted_entry_assembly()
    {
        var command = TaskbarRelaunch.BuildCommand(@"C:\Tools With Spaces\dotnet.exe", @"C:\Apps\JoJot.dll", @"P:\Data With Spaces\");
        Assert.Equal("\"C:\\Tools With Spaces\\dotnet.exe\" \"C:\\Apps\\JoJot.dll\" --data-dir \"P:\\Data With Spaces\\\\\"", command);
        Assert.Equal(TaskbarRelaunch.BuildId(@"P:\DATA"), TaskbarRelaunch.BuildId(@"p:\data"));
        Assert.NotEqual(TaskbarRelaunch.BuildId(@"P:\data"), TaskbarRelaunch.BuildId(@"P:\other"));
    }

    [Fact]
    public void Native_window_property_store_round_trips_and_clears_relaunch_values()
    {
        var apartment = CoInitializeEx(0, 2);
        var window = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        try
        {
            const string command = "\"C:\\Example path\\app.exe\" --data-dir \"P:\\notes path\"";
            const string id = "JoJot.Avalonia.TestPropertyStore";
            TaskbarRelaunch.SetProperties(window, command, id);
            Assert.Equal(command, Read(window, 2));
            Assert.Equal("JoJot Avalonia Experiment", Read(window, 4));
            Assert.Equal(id, Read(window, 5));
            TaskbarRelaunch.ClearProperties(window);
            Assert.Null(Read(window, 2));
            Assert.Null(Read(window, 4));
            Assert.Null(Read(window, 5));
        }
        finally { DestroyWindow(window); if (apartment >= 0) CoUninitialize(); }
    }

    private static string? Read(nint window, uint property)
    {
        var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref iid, out var store));
        try
        {
            var table = Marshal.ReadIntPtr(store);
            var get = Marshal.GetDelegateForFunctionPointer<GetValue>(Marshal.ReadIntPtr(table, 5 * IntPtr.Size));
            var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = property };
            Marshal.ThrowExceptionForHR(get(store, ref key, out var value));
            try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.Release(store); }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetValue(nint store, ref PropertyKey key, out PropVariant value);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Pointer; }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(nint hwnd, ref Guid iid, out nint store);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
}
