using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

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

        // Clip to TextBox bounds so highlights don't bleed over toolbar
        var clipRect = new Rect(0, 0, _textBox.ActualWidth, _textBox.ActualHeight);
        drawingContext.PushClip(new RectangleGeometry(clipRect));

        var matchBrush = (Brush)_textBox.FindResource("c-find-match-bg");
        var activeBrush = (Brush)_textBox.FindResource("c-find-match-active-bg");

        for (int i = 0; i < _matches.Count; i++)
        {
            int pos = _matches[i];
            var brush = (i == _activeIndex) ? activeBrush : matchBrush;

            try
            {
                // Use leading edge of first char and trailing edge of last char
                // to avoid multi-line false positives at line wrap boundaries
                var startRect = _textBox.GetRectFromCharacterIndex(pos);
                var endRect = _textBox.GetRectFromCharacterIndex(pos + _queryLength - 1, true);

                // Single rectangle from start leading edge to end trailing edge
                var rect = new Rect(startRect.TopLeft, endRect.BottomRight);
                drawingContext.DrawRectangle(brush, null, rect);
            }
            catch
            {
                // Silently skip matches with invalid positions
                // (can happen during rapid text changes)
            }
        }

        drawingContext.Pop();
    }
}
