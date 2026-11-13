using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace JoJot.AvaloniaExperiment.Platform;

// Diagnostic-only: captures this application's owned HWND, including the native caption.
internal static partial class NativeFrameCapture
{
    internal static unsafe void Save(Window window, string path)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } platform)
            return;
        var expected = WindowChromeTheme.GetColors(window);
        var captionResult = DwmGetWindowAttribute(platform.Handle, WindowChromeTheme.CaptionColor, out var caption, 4);
        var textResult = DwmGetWindowAttribute(platform.Handle, WindowChromeTheme.TextColor, out var text, 4);
        File.WriteAllText(path + ".attributes.txt", $"Caption HRESULT={captionResult:X8} actual={caption:X8} expected={expected.Caption:X8}\nText HRESULT={textResult:X8} actual={text:X8} expected={expected.Text:X8}\n");
        // Caption/text attributes are documented for DwmSetWindowAttribute; some Windows
        // builds reject reading them with E_INVALIDARG. Inspect the native pixels as well.
        if ((captionResult == 0 && caption != expected.Caption) || (textResult == 0 && text != expected.Text))
            throw new InvalidOperationException($"Native caption colors did not follow the current theme: {File.ReadAllText(path + ".attributes.txt")}");
        if (!GetWindowRect(platform.Handle, out var rect)) throw new InvalidOperationException("Cannot read native window bounds.");
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
        var dc = CreateCompatibleDC(0);
        if (dc == 0) throw new InvalidOperationException("Cannot create capture DC.");
        nint bitmap = 0;
        nint previous = 0;
        try
        {
            bitmap = CreateDIBSection(dc, ref info, 0, out var pixels, 0, 0);
            if (bitmap == 0) throw new InvalidOperationException("Cannot create capture bitmap.");
            previous = SelectObject(dc, bitmap);
            if (!PrintWindow(platform.Handle, dc, 2)) throw new InvalidOperationException("Native frame capture failed.");
            var clientOrigin = new Point();
            if (!ClientToScreen(platform.Handle, ref clientOrigin)) throw new InvalidOperationException("Cannot locate caption pixels.");
            var captionHeight = Math.Clamp(clientOrigin.Y - rect.Top, 0, height);
            var captionPixels = 0;
            var textPixels = 0;
            for (var y = 0; y < captionHeight; y++)
            for (var x = 0; x < width; x++)
            {
                var pixel = (byte*)pixels + (y * width + x) * 4;
                var color = (uint)(pixel[2] | pixel[1] << 8 | pixel[0] << 16);
                if (color == expected.Caption) captionPixels++;
                if (color == expected.Text) textPixels++;
            }
            File.WriteAllText(path + ".pixels.txt", $"Caption height={captionHeight}; matching background pixels={captionPixels}; matching text pixels={textPixels}\n");
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && (captionPixels < width || textPixels < 2))
                throw new InvalidOperationException("Native caption pixels do not contain the expected theme colors.");
            using var output = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var target = output.Lock())
                for (var row = 0; row < height; row++)
                    Buffer.MemoryCopy((byte*)pixels + row * width * 4, (byte*)target.Address + row * target.RowBytes, target.RowBytes, width * 4);
            using var file = File.Open(path, FileMode.CreateNew, FileAccess.Write);
            output.Save(file, PngBitmapEncoderOptions.Default);
        }
        finally
        {
            if (previous != 0) SelectObject(dc, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, out uint value, int size);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out Rect rect);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hwnd, ref Point point);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(nint hwnd, nint dc, uint flags);
    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint value);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint value);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
}
