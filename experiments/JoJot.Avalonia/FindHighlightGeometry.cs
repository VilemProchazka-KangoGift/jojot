using Avalonia;
using Avalonia.Media.TextFormatting;

namespace JoJot.AvaloniaExperiment;

// TextLayout.HitTestTextRange scans preceding lines and allocates a document-sized
// rectangle buffer for each match. Index line tops once, then visit only visible lines.
internal sealed class FindHighlightGeometry
{
    private TextLayout? _layout;
    private double[] _lineTops = [];

    internal void Clear()
    {
        _layout = null;
        _lineTops = [];
    }

    internal IEnumerable<Rect> GetVisibleRectangles(TextLayout layout, List<int> matches, int length, int selected, Rect viewport,
        int selectionStart = 0, int selectionLength = 0)
    {
        if (matches.Count == 0 || length <= 0 || viewport.Height <= 0) yield break;
        var lines = layout.TextLines;
        if (!ReferenceEquals(_layout, layout))
        {
            _layout = layout;
            _lineTops = new double[lines.Count + 1];
            for (var index = 0; index < lines.Count; index++)
                _lineTops[index + 1] = _lineTops[index] + lines[index].Height;
        }
        var firstLine = Array.BinarySearch(_lineTops, viewport.Top);
        if (firstLine < 0) firstLine = ~firstLine - 1;
        firstLine = Math.Max(0, firstLine);
        for (var lineIndex = firstLine; lineIndex < lines.Count && _lineTops[lineIndex] < viewport.Bottom; lineIndex++)
        {
            var line = lines[lineIndex];
            var lineStart = line.FirstTextSourceIndex;
            var lineEnd = lineStart + line.Length;
            var matchIndex = matches.BinarySearch(Math.Max(0, lineStart - length + 1));
            if (matchIndex < 0) matchIndex = ~matchIndex;
            for (; matchIndex < matches.Count && matches[matchIndex] < lineEnd; matchIndex++)
            {
                if (matchIndex == selected) continue;
                if (selectionLength > 0 && matches[matchIndex] < selectionStart + selectionLength &&
                    matches[matchIndex] + length > selectionStart) continue;
                var start = Math.Max(lineStart, matches[matchIndex]);
                var count = Math.Min(lineEnd, matches[matchIndex] + length) - start;
                if (count <= 0) continue;
                Rect? pending = null;
                foreach (var bounds in line.GetTextBounds(start, count))
                {
                    var rect = bounds.Rectangle.WithY(_lineTops[lineIndex]);
                    if (pending is { } previous && Math.Abs(previous.Right - rect.Left) < 0.000001)
                        pending = previous.WithWidth(previous.Width + rect.Width);
                    else
                    {
                        if (pending is { } completed) yield return completed;
                        pending = rect;
                    }
                }
                if (pending is { } last) yield return last;
            }
        }
    }
}
