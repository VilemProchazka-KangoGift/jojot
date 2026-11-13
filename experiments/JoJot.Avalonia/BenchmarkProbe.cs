using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
        if (Environment.GetEnvironmentVariable("JOJOT_RENDER_WORKLOAD") == "1")
            await RecordRenderWorkloadAsync(window, output);
        if (Environment.GetEnvironmentVariable("JOJOT_CONTINUOUS_SCROLL") == "1")
            await ((MainWindow)window).RecordContinuousScrollAsync(output, WaitForRenderedBatchAsync);
    }

    private static async Task RecordRenderWorkloadAsync(Window window, string output)
    {
        var editor = window.FindControl<TextBox>("Editor") ?? throw new InvalidOperationException("Missing editor.");
        var original = editor.Text;
        var originalCaret = editor.CaretIndex;
        var results = new List<RenderWorkloadResult>();
        using var process = Process.GetCurrentProcess();
        try
        {
            // Exercise full layout and viewport scrolling with a long, ordinary text note.
            var text = string.Join('\n', Enumerable.Range(0, 2000).Select(i => $"Line {i:D4}: the quick brown fox jumps over the lazy dog; 0123456789."));
            editor.Text = text;
            await WaitForRenderedBatchAsync(window);
            var scroll = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
            if (scroll.Extent.Height <= scroll.Viewport.Height)
                throw new InvalidOperationException("Render probe requires scrollable text.");
            foreach (var scenario in new[] { "edit", "scroll" })
            {
                var frames = new double[90];
                var minimumScrollY = double.PositiveInfinity;
                var maximumScrollY = double.NegativeInfinity;
                process.Refresh();
                var cpu = process.TotalProcessorTime;
                var start = Stopwatch.GetTimestamp();
                for (var index = 0; index < frames.Length; index++)
                {
                    var frame = Stopwatch.GetTimestamp();
                    if (scenario == "edit") editor.Text = text + new string('x', index + 1);
                    else scroll.Offset = new Avalonia.Vector(0, index % 2 == 0 ? scroll.Extent.Height : 0);
                    await WaitForRenderedBatchAsync(window);
                    frames[index] = Stopwatch.GetElapsedTime(frame).TotalMilliseconds;
                    minimumScrollY = Math.Min(minimumScrollY, scroll.Offset.Y);
                    maximumScrollY = Math.Max(maximumScrollY, scroll.Offset.Y);
                }
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                process.Refresh();
                if (scenario == "scroll" && maximumScrollY <= minimumScrollY)
                    throw new InvalidOperationException("Render probe did not move the viewport.");
                results.Add(new RenderWorkloadResult(scenario, editor.Text?.Length ?? 0,
                    elapsed, (process.TotalProcessorTime - cpu).TotalMilliseconds, minimumScrollY, maximumScrollY, frames));
            }
        }
        finally
        {
            editor.Text = original;
            editor.CaretIndex = originalCaret;
        }
        File.WriteAllText(output + ".render.json", JsonSerializer.Serialize(results.ToArray(), BenchmarkJsonContext.Default.RenderWorkloadResultArray));
        File.WriteAllLines(output + ".modules.txt", process.Modules.Cast<ProcessModule>().Select(module => module.ModuleName).Order());
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
[JsonSerializable(typeof(RenderWorkloadResult[]))]
internal sealed partial class BenchmarkJsonContext : JsonSerializerContext;

internal sealed record RenderWorkloadResult(string Scenario, int TextLength, double ElapsedMs, double CpuMs,
    double MinimumScrollY, double MaximumScrollY, double[] FrameMs);
