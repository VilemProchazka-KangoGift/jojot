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
    private const int HighlightThreshold = 500;

    private readonly TextBox _textBox;
    private List<int> _matches = [];
    private int _activeIndex = -1;
    private int _queryLength;
    private Brush? _cachedMatchBrush;
    private Brush? _cachedActiveBrush;

    public TextBoxHighlightAdorner(TextBox textBox) : base(textBox)
    {
        _textBox = textBox;
        IsHitTestVisible = false; // Don't intercept mouse events
        RefreshBrushes();
    }

    /// <summary>
    /// Reloads theme-aware brushes from resources. Call on theme change.
    /// </summary>
    public void RefreshBrushes()
    {
        _cachedMatchBrush = _textBox.TryFindResource("c-find-match-bg") as Brush;
        _cachedActiveBrush = _textBox.TryFindResource("c-find-match-active-bg") as Brush;
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

        // Determine visible character range so we only render matches on screen
        int firstVisibleLine = _textBox.GetFirstVisibleLineIndex();
        int lastVisibleLine = _textBox.GetLastVisibleLineIndex();
        if (firstVisibleLine < 0 || lastVisibleLine < 0) return;

        int visibleStart = _textBox.GetCharacterIndexFromLineIndex(firstVisibleLine);
        int lastLineStart = _textBox.GetCharacterIndexFromLineIndex(lastVisibleLine);
        int lastLineLength = _textBox.GetLineLength(lastVisibleLine);
        int visibleEnd = (lastLineStart >= 0 && lastLineLength >= 0)
            ? lastLineStart + lastLineLength
            : _textBox.Text.Length;

        // Clip to TextBox bounds so highlights don't bleed over toolbar
        var clipRect = new Rect(0, 0, _textBox.ActualWidth, _textBox.ActualHeight);
        drawingContext.PushClip(new RectangleGeometry(clipRect));

        if (_cachedMatchBrush is null || _cachedActiveBrush is null) return;

        if (_matches.Count > HighlightThreshold)
        {
            // Too many matches — only highlight the active one to stay responsive
            if (_activeIndex >= 0 && _activeIndex < _matches.Count)
            {
                int pos = _matches[_activeIndex];
                if (pos + _queryLength >= visibleStart && pos <= visibleEnd)
                    DrawMatch(drawingContext, pos, _cachedActiveBrush);
            }
        }
        else
        {
            // Binary search for first match in visible range
            int startIdx = BinarySearchFirstVisible(visibleStart);

            for (int i = startIdx; i < _matches.Count; i++)
            {
                int pos = _matches[i];
                if (pos > visibleEnd) break;

                var brush = (i == _activeIndex) ? _cachedActiveBrush : _cachedMatchBrush;
                DrawMatch(drawingContext, pos, brush);
            }
        }

        drawingContext.Pop();
    }

    private void DrawMatch(DrawingContext drawingContext, int pos, Brush brush)
    {
        try
        {
            var startRect = _textBox.GetRectFromCharacterIndex(pos);
            var endRect = _textBox.GetRectFromCharacterIndex(pos + _queryLength - 1, true);
            var rect = new Rect(startRect.TopLeft, endRect.BottomRight);
            drawingContext.DrawRectangle(brush, null, rect);
        }
        catch
        {
            // Silently skip matches with invalid positions
            // (can happen during rapid text changes)
        }
    }

    /// <summary>
    /// Binary search for the first match index where pos + queryLength >= visibleStart.
    /// </summary>
    private int BinarySearchFirstVisible(int visibleStart)
    {
        int lo = 0, hi = _matches.Count - 1;
        int result = _matches.Count;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_matches[mid] + _queryLength >= visibleStart)
            {
                result = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }
        return result;
    }
}
