using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using JoJot.Models;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow
{
    private NoteTab? _dragNote;
    private Point _dragOrigin;
    private bool _draggingTab;
    private ListBoxItem? _dropItem;
    private int _dropIndex = -1;

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_busy || !string.IsNullOrEmpty(SearchBox.Text) || e.ClickCount != 1 || !e.GetCurrentPoint(NoteList).Properties.IsLeftButtonPressed ||
            (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;
        _dragNote = NoteFromSource(e.Source);
        _dragOrigin = e.GetPosition(NoteList);
    }

    private void OnTabPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragNote is not { } note || !e.GetCurrentPoint(NoteList).Properties.IsLeftButtonPressed)
            return;
        var point = e.GetPosition(NoteList);
        if (!_draggingTab)
        {
            if (Math.Abs(point.Y - _dragOrigin.Y) < 5 && Math.Abs(point.X - _dragOrigin.X) < 5)
                return;
            _draggingTab = true;
            FinishRename(save: false);
            e.Pointer.Capture(NoteList);
        }

        if (NoteList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
        {
            if (point.Y < 18)
                scroll.LineUp();
            else if (point.Y > NoteList.Bounds.Height - 18)
                scroll.LineDown();
        }

        _dropItem?.Classes.Remove("dropAbove");
        _dropItem?.Classes.Remove("dropBelow");
        _dropItem?.Classes.Remove("dropBoundary");
        _dropItem = null;
        _dropIndex = -1;
        foreach (var container in NoteList.GetVisualDescendants().OfType<ListBoxItem>())
        {
            if (container.DataContext is not NoteTab target || target.Pinned != note.Pinned ||
                container.TranslatePoint(default, NoteList) is not { } origin ||
                point.Y < origin.Y || point.Y > origin.Y + container.Bounds.Height)
                continue;
            var below = point.Y >= origin.Y + container.Bounds.Height / 2;
            _dropIndex = _notes.IndexOf(target) + (below ? 1 : 0);
            _dropItem = container;
            if (!below && target.ShowUnpinnedSeparator)
                container.Classes.Add("dropBoundary");
            else if (below && _dropIndex < _notes.Count && _notes[_dropIndex].ShowUnpinnedSeparator &&
                     NoteList.ContainerFromItem(_notes[_dropIndex]) is ListBoxItem boundary)
            {
                _dropItem = boundary;
                boundary.Classes.Add("dropBoundary");
            }
            else
                container.Classes.Add(below ? "dropBelow" : "dropAbove");
            break;
        }

        e.Handled = true;
    }

    private async void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var note = _dragNote;
        var position = _dropIndex;
        var dragged = _draggingTab;
        ResetTabDrag();
        if (!dragged)
            return;
        e.Pointer.Capture(null);
        e.Handled = true;
        if (note is not null && position >= 0)
            await MoveTabAsync(note, position);
    }

    internal Task MoveTabAsync(NoteTab note, int position) => RunActionAsync(async () =>
    {
        bool moved;
        _refreshing = true;
        try { moved = _viewModel.MoveTab(_notes.IndexOf(note), position); }
        finally { _refreshing = false; }
        if (moved)
            await SaveTabOrderAsync();
    });

    private void ResetTabDrag()
    {
        _dragNote = null;
        _draggingTab = false;
        _dropItem?.Classes.Remove("dropAbove");
        _dropItem?.Classes.Remove("dropBelow");
        _dropItem?.Classes.Remove("dropBoundary");
        _dropItem = null;
        _dropIndex = -1;
    }
}
