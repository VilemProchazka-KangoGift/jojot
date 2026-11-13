using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace JoJot.AvaloniaExperiment;

internal sealed partial class BenchmarkProbe(long entryTimestamp, ExperimentOptions options)
{
    private readonly double _entryProcessAge = ReadProcessAgeAtEntry(entryTimestamp);

    private static double ReadProcessAgeAtEntry(long entryTimestamp)
    {
        using var process = Process.GetCurrentProcess();
        return (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds -
            Stopwatch.GetElapsedTime(entryTimestamp).TotalMilliseconds;
    }

    private double Elapsed() => _entryProcessAge + Stopwatch.GetElapsedTime(entryTimestamp).TotalMilliseconds;

    public async Task<Frame> RecordFirstRenderAsync(Window window)
    {
        await WaitForRenderedBatchAsync(window);
        var elapsed = Elapsed();
        var handle = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("Window has no native handle.");
        if (GetWindowRect(handle, out var bounds) == 0 || GetClientRect(handle, out var client) == 0)
            throw new InvalidOperationException("Cannot read benchmark window geometry.");
        return new Frame(elapsed, window.Position.X / window.RenderScaling, window.Position.Y / window.RenderScaling,
            window.Bounds.Width, window.Bounds.Height, bounds, client);
    }

    public async Task RecordEditorAsync(Window window, int count, string? selectedNoteName, int editorTextLength, Task<Frame> firstRender)
    {
        var editorReady = Elapsed();
        await WaitForRenderedBatchAsync(window);
        var editorRendered = Elapsed();
        StartupTrace.Mark("editor-rendered");
        StartupTrace.Flush();
        var first = await firstRender;
        var data = new BenchmarkResult(first.Elapsed, editorReady, editorRendered, count,
            first.Left, first.Top, first.Width, first.Height, first.Bounds.Left, first.Bounds.Top,
            first.Bounds.Right - first.Bounds.Left, first.Bounds.Bottom - first.Bounds.Top,
            first.Client.Right - first.Client.Left, first.Client.Bottom - first.Client.Top, selectedNoteName, editorTextLength);
        options.ValidateBenchmarkPath();
        var output = options.BenchmarkOutput!;
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, BenchmarkJsonContext.Default.BenchmarkResult));
            File.Move(temporary, output, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private static async Task WaitForRenderedBatchAsync(Window window)
    {
        // Allow pending layout/render invalidations to reach the compositor before
        // awaiting its render-thread completion. This is a renderer milestone,
        // not a measurement of physical monitor presentation.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var compositor = ElementComposition.GetElementVisual(window)?.Compositor
            ?? throw new InvalidOperationException("Window has no composition visual.");
        await compositor.RequestCompositionBatchCommitAsync().Rendered.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [LibraryImport("user32.dll")]
    private static partial int GetWindowRect(nint handle, out NativeRect rect);

    [LibraryImport("user32.dll")]
    private static partial int GetClientRect(nint handle, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public sealed record Frame(double Elapsed, double Left, double Top, double Width, double Height,
        NativeRect Bounds, NativeRect Client);
}

internal sealed record BenchmarkResult(double FirstRenderMs, double EditorReadyMs, double EditorRenderingMs,
    int Notes, double FirstRenderLeft, double FirstRenderTop, double FirstRenderWidth, double FirstRenderHeight,
    int NativeLeft, int NativeTop, int NativeWidth, int NativeHeight, int ClientWidth, int ClientHeight,
    string? SelectedNoteName, int EditorTextLength);

[JsonSerializable(typeof(BenchmarkResult))]
internal sealed partial class BenchmarkJsonContext : JsonSerializerContext;
