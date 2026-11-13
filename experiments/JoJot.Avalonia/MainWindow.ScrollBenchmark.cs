using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow
{
    internal async Task RecordContinuousScrollAsync(string output, Func<Window, Task> render)
    {
        var original = Editor.Text;
        var originalCaret = Editor.CaretIndex;
        var originalWrap = Editor.TextWrapping == TextWrapping.Wrap;
        var results = new List<ContinuousScrollResult>();
        using var process = Process.GetCurrentProcess();
        try
        {
            Width = 850;
            Height = 560;
            Editor.Text = string.Join('\n', Enumerable.Range(0, 2000).Select(i =>
                $"Line {i:D4}: needle alpha beta gamma delta epsilon zeta eta theta; needle 0123456789; " +
                "the quick brown fox jumps over the lazy dog repeatedly across a long line."));
            ShowFindPanel(); // Hold the same panel/viewport geometry for every cell.
            var scroll = GetEditorScroll() ?? throw new InvalidOperationException("Missing editor scroller.");
            var presenter = Editor.GetVisualDescendants().OfType<TextPresenter>().First();
            foreach (var wrap in new[] { false, true })
            foreach (var highlights in new[] { false, true })
            {
                SetEditorWrap(wrap);
                _findQuery!.Text = highlights ? "needle" : "";
                // TextChanged is queued; settle it before capturing the find generation.
                await render(this);
                _findTimer.Stop();
                await RunFindAsync();
                await render(this);
                if (_findMatches.Count != (highlights ? 4000 : 0))
                    throw new InvalidOperationException("Wrong find matches in scrolling probe.");
                var startY = Math.Floor((scroll.Extent.Height - scroll.Viewport.Height) * 0.4);
                if (startY < 0 || startY + 180 * 24 >= scroll.Extent.Height - scroll.Viewport.Height)
                    throw new InvalidOperationException("Scrolling probe requires a sufficiently long note.");
                scroll.Offset = new Vector(0, startY);
                await render(this);
                var overlay = _findHighlights!;
                overlay.RenderTicks = overlay.RenderAllocatedBytes = 0;
                overlay.RenderCount = overlay.DrawnRectangles = 0;
                overlay.ProfileRendering = true;
                var initialLayout = presenter.TextLayout;
                var layoutChanges = 0;
                var layoutEvents = 0;
                EventHandler layoutHandler = (_, _) => layoutEvents++;
                Editor.LayoutUpdated += layoutHandler;
                var frameTimes = new List<double>();
                var inputTimes = new List<double>();
                var actualOffsets = new List<double>();
                var steps = 0;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                var started = Stopwatch.GetTimestamp();
                var previousFrame = started;
                var previousInput = started;
                process.Refresh();
                var cpu = process.TotalProcessorTime;
                timer.Tick += (_, _) =>
                {
                    var now = Stopwatch.GetTimestamp();
                    inputTimes.Add(Stopwatch.GetElapsedTime(previousInput, now).TotalMilliseconds);
                    previousInput = now;
                    scroll.Offset = new Vector(0, startY + ++steps * 24);
                    actualOffsets.Add(scroll.Offset.Y);
                    if (steps == 180) timer.Stop();
                };
                try
                {
                    timer.Start();
                    while (steps < 180)
                    {
                        await render(this);
                        var now = Stopwatch.GetTimestamp();
                        frameTimes.Add(Stopwatch.GetElapsedTime(previousFrame, now).TotalMilliseconds);
                        previousFrame = now;
                        if (!ReferenceEquals(initialLayout, presenter.TextLayout))
                        {
                            initialLayout = presenter.TextLayout;
                            layoutChanges++;
                        }
                    }
                    await render(this);
                }
                finally
                {
                    timer.Stop();
                    overlay.ProfileRendering = false;
                    Editor.LayoutUpdated -= layoutHandler;
                }
                process.Refresh();
                if (actualOffsets.Count != 180 || actualOffsets.Distinct().Count() != 180 ||
                    (highlights && overlay.DrawnRectangles == 0) || (!highlights && overlay.DrawnRectangles != 0))
                    throw new InvalidOperationException("Scrolling/highlight probe did not exercise the requested state.");
                results.Add(new ContinuousScrollResult(wrap, highlights, Editor.Text!.Length, _findMatches.Count,
                    scroll.Viewport.Width, scroll.Viewport.Height, scroll.Extent.Height,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds, (process.TotalProcessorTime - cpu).TotalMilliseconds,
                    layoutChanges, layoutEvents, overlay.RenderCount, overlay.DrawnRectangles,
                    overlay.RenderTicks * 1000d / Stopwatch.Frequency, overlay.RenderAllocatedBytes,
                    frameTimes.ToArray(), inputTimes.ToArray(), actualOffsets.ToArray()));
            }
        }
        finally
        {
            HideFindPanel();
            SetEditorWrap(originalWrap);
            Editor.Text = original;
            Editor.CaretIndex = originalCaret;
        }
        File.WriteAllText(output + ".scroll.json", JsonSerializer.Serialize(results.ToArray(), ContinuousScrollJsonContext.Default.ContinuousScrollResultArray));
    }
}
internal sealed record ContinuousScrollResult(bool Wrap, bool Highlights, int TextLength, int Matches,
    double ViewportWidth, double ViewportHeight, double ExtentHeight, double ElapsedMs, double CpuMs,
    int TextLayoutChanges, int LayoutEvents, int HighlightRenderCount, int HighlightRectangles,
    double HighlightRenderMs, long HighlightAllocatedBytes, double[] FrameMs, double[] InputMs, double[] ScrollY);
[JsonSerializable(typeof(ContinuousScrollResult[]))]
internal sealed partial class ContinuousScrollJsonContext : JsonSerializerContext;
