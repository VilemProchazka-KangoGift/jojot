using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;

namespace JoJot.Controls;

/// <summary>
/// Adorner that paints highlight rectangles over match positions in a TextBox.
/// Active match uses a stronger color; other matches use a softer color.
/// Colors are resolved via FindResource on each render to stay theme-aware.
/// </summary>
public class TextBoxHighlightAdorner : Adorner
{
    private readonly TextBox _textBox;
    private List<int> _matches = [];
    private int _activeIndex = -1;
    private int _queryLength;

    public TextBoxHighlightAdorner(TextBox textBox) : base(textBox)
    {
        _textBox = textBox;
        IsHitTestVisible = false; // Don't intercept mouse events
    }

    public void Update(List<int> matches, int activeIndex, int queryLength)
    {
        _matches = matches;
        _activeIndex = activeIndex;
        _queryLength = queryLength;
        InvalidateVisual();
    }

    public void Clear()
    {
        _matches = [];
        _activeIndex = -1;
        _queryLength = 0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_matches.Count == 0 || _queryLength <= 0) return;

        var matchBrush = (Brush)_textBox.FindResource("c-find-match-bg");
        var activeBrush = (Brush)_textBox.FindResource("c-find-match-active-bg");

        for (int i = 0; i < _matches.Count; i++)
        {
            int pos = _matches[i];
            var brush = (i == _activeIndex) ? activeBrush : matchBrush;

            try
            {
                // GetRectFromCharacterIndex can throw if index is out of range
                // (e.g., content changed between search and render)
                var startRect = _textBox.GetRectFromCharacterIndex(pos);
                var endRect = _textBox.GetRectFromCharacterIndex(pos + _queryLength);

                if (startRect.Top == endRect.Top)
                {
                    // Single-line match — one rectangle
                    var rect = new Rect(startRect.TopLeft, endRect.BottomRight);
                    drawingContext.DrawRectangle(brush, null, rect);
                }
                else
                {
                    // Multi-line match: draw start to end-of-line for first line,
                    // then start-of-line to end position for last line.
                    var firstLineRect = new Rect(
                        startRect.TopLeft,
                        new Point(_textBox.ActualWidth - _textBox.Padding.Right, startRect.Bottom));
                    drawingContext.DrawRectangle(brush, null, firstLineRect);

                    var lastLineRect = new Rect(
                        new Point(_textBox.Padding.Left, endRect.Top),
                        endRect.BottomRight);
                    drawingContext.DrawRectangle(brush, null, lastLineRect);
                }
            }
            catch
            {
                // Silently skip matches with invalid positions
                // (can happen during rapid text changes)
            }
        }
    }
}
