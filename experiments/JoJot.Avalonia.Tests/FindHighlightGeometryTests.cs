using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using JoJot.ViewModels;
using Xunit;

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class FindHighlightGeometryTests
{
    [AvaloniaFact]
    public void OverlayDoesNotPaintOverAnyPartOfSelectedText()
    {
        using var layout = new TextLayout("note note note", new Typeface("Arial"), 13, maxWidth: 500);
        var geometry = new FindHighlightGeometry();
        var rectangles = geometry.GetVisibleRectangles(layout, [0, 5, 10], 4, -1, new Rect(0, 0, 500, 100), 2, 5).ToArray();
        Assert.Equal(layout.HitTestTextRange(10, 4).ToArray(), rectangles);
    }
    [AvaloniaFact]
    public void Visible_rectangles_match_framework_ranges_after_wrap_resize_and_query_changes()
    {
        var geometry = new FindHighlightGeometry();
        foreach (var wrapping in new[] { TextWrapping.NoWrap, TextWrapping.Wrap })
        foreach (var width in new[] { 83d, 213d })
        foreach (var flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
        foreach (var newline in new[] { "\n", "\r\n" })
        foreach (var query in new[] { "needle", "needle" + newline, "אבג", "\t", "delta needle", "needle\tאבג" })
        {
            var text = string.Join(newline, Enumerable.Range(0, 30).Select(i => $"{i}: needle\tאבג delta needle"));
            using var layout = new TextLayout(text, new Typeface("Arial"), 13, textWrapping: wrapping, maxWidth: width, flowDirection: flow);
            var matches = MainWindowViewModel.FindAllMatches(text, query);
            Assert.NotEmpty(matches);
            var viewport = new Rect(0, layout.Height * 0.35 + 2, width, 71);
            var selected = matches.FindIndex(match => layout.HitTestTextRange(match, query.Length)
                .Any(rect => rect.Bottom > viewport.Top && rect.Top < viewport.Bottom));
            Assert.True(selected >= 0, "The selected match must be visible to exercise its exclusion.");
            var expected = matches.SelectMany((match, index) => index == selected ? [] : layout.HitTestTextRange(match, query.Length))
                .Where(rect => rect.Bottom > viewport.Top && rect.Top < viewport.Bottom).OrderBy(rect => rect.Y).ThenBy(rect => rect.X).ToArray();
            var actual = geometry.GetVisibleRectangles(layout, matches, query.Length, selected, viewport)
                .OrderBy(rect => rect.Y).ThenBy(rect => rect.X).ToArray();
            Assert.Equal(expected.Length, actual.Length);
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.Equal(expected[index].X, actual[index].X, 5);
                Assert.Equal(expected[index].Y, actual[index].Y, 5);
                Assert.Equal(expected[index].Width, actual[index].Width, 5);
                Assert.Equal(expected[index].Height, actual[index].Height, 5);
            }
        }
    }

    [AvaloniaFact]
    public void Scrolling_highlights_do_not_allocate_per_document_line_for_each_visible_match()
    {
        var text = string.Join('\n', Enumerable.Repeat("needle words needle", 2000));
        using var layout = new TextLayout(text, new Typeface("Arial"), 13, maxWidth: 500);
        var matches = MainWindowViewModel.FindAllMatches(text, "needle");
        var geometry = new FindHighlightGeometry();
        var viewport = new Rect(0, layout.Height * 0.7, 500, 200);
        Assert.NotEmpty(geometry.GetVisibleRectangles(layout, matches, 6, -1, viewport));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var rectangles = geometry.GetVisibleRectangles(layout, matches, 6, -1, viewport).ToArray();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.NotEmpty(rectangles);
        Assert.True(allocated < 100_000, $"Visible highlights allocated {allocated:N0} bytes for one viewport.");
    }
}
