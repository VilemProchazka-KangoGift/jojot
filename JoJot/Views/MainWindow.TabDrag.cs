using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JoJot.Models;
using JoJot.Services;
using JoJot.Themes;

namespace JoJot;

public partial class MainWindow
{
    // ─── Drag-to-Reorder ────────────────────────────────────────────────

    private void TabItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeRename is not null) return; // No drag during rename
        if (!string.IsNullOrEmpty(_searchText)) return; // No drag during search

        _dragStartPoint = e.GetPosition(TabList);
        _dragItem = sender as ListBoxItem;
        _dragTab = _dragItem?.Tag as NoteTab;
    }

    private void TabItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        // Drag start and tracking is handled by TabList.PreviewMouseMove (fires first during tunneling).
        // This handler is kept only as a fallback — the TabList handler sets e.Handled during drag.
    }

    /// <summary>
    /// Initiates the drag operation: sets state, fades the tab, captures mouse.
    /// Called from TabList.PreviewMouseMove when distance threshold is exceeded.
    /// </summary>
    private void StartDrag()
    {
        if (_isDragging || _dragItem is null || _dragTab is null) return;

        _isDragging = true;

        // Track original index for indicator suppression (Items are NoteTab data items now).
        _dragOriginalListIndex = TabList.Items.IndexOf(_dragTab);

        // Fade original item to 50% opacity in-place (no ghost adorner)
        // Set on content Border (not ListBoxItem) to avoid WPF internal Opacity resets
        var dragBorder = FindNamedDescendant<Border>(_dragItem, "OuterBorder");
        if (dragBorder is not null)
        {
            dragBorder.Opacity = 0.5;
        }

        // SubTree mode keeps events routing to children within TabList
        // Guard prevents LostMouseCapture from aborting drag during transfer
        _isTransferringCapture = true;
        try
        {
            Mouse.Capture(TabList, CaptureMode.SubTree);
        }
        finally
        {
            _isTransferringCapture = false;
        }
    }

    private void TabItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging) e.Handled = true;
        CompleteDrag();
    }

    /// <summary>
    /// Updates the drop indicator position during drag.
    /// Enforces zone boundaries: pinned tabs stay in pinned zone, unpinned in unpinned.
    /// Shows a horizontal line at the drop target position.
    /// </summary>
    private void UpdateDropIndicator(System.Windows.Point mousePos)
    {
        RemoveDropIndicator();

        _dragInsertIndex = -1;
        double bestDistance = double.MaxValue;
        int lastSameZoneIndex = -1;

        for (int i = 0; i < TabList.Items.Count; i++)
        {
            if (TabList.Items[i] is not NoteTab candidateTab) continue;

            // Zone enforcement: only allow drop between same-zone items
            if (candidateTab.Pinned != _dragTab!.Pinned) continue;

            // Resolve the realized container; off-screen (virtualized) items aren't drop targets.
            if (ContainerFor(candidateTab) is not ListBoxItem candidate) continue;

            lastSameZoneIndex = i;

            try
            {
                var transform = candidate.TransformToAncestor(TabList);
                var itemPos = transform.Transform(new System.Windows.Point(0, 0));

                // Check distance to top edge of item
                double distTop = Math.Abs(mousePos.Y - itemPos.Y);
                if (distTop < bestDistance)
                {
                    bestDistance = distTop;
                    _dragInsertIndex = i;
                }

                // Check distance to bottom edge of item
                double distBottom = Math.Abs(mousePos.Y - (itemPos.Y + candidate.ActualHeight));
                if (distBottom < bestDistance)
                {
                    bestDistance = distBottom;
                    _dragInsertIndex = i + 1;
                }
            }
            catch
            {
                // TransformToAncestor can fail if item isn't in visual tree
            }
        }

        if (_dragInsertIndex < 0) return;

        // Suppress indicator at positions that wouldn't change the order
        // Inserting at the original index or the index after it leaves the item in place
        if (_dragOriginalListIndex >= 0 &&
            (_dragInsertIndex == _dragOriginalListIndex || _dragInsertIndex == _dragOriginalListIndex + 1))
        {
            _dragInsertIndex = -1;
            return;
        }

        // Show a horizontal line at the drop target. Every item is a real tab now (no separators),
        // so draw a top border on the target index, or a bottom border after the last item.
        if (_dragInsertIndex < TabList.Items.Count)
        {
            var targetBorder = OuterBorderForIndex(_dragInsertIndex);
            if (targetBorder is not null)
            {
                targetBorder.BorderThickness = new Thickness(0, 2, 0, 0);
                targetBorder.BorderBrush = GetBrush(ThemeKeys.Accent);
                _dropIndicatorBorder = targetBorder;
            }
        }
        else if (lastSameZoneIndex >= 0)
        {
            var lastBorder = OuterBorderForIndex(lastSameZoneIndex);
            if (lastBorder is not null)
            {
                lastBorder.BorderThickness = new Thickness(0, 0, 0, 2);
                lastBorder.BorderBrush = GetBrush(ThemeKeys.Accent);
                _dropIndicatorBorder = lastBorder;
            }
        }
    }

    /// <summary>
    /// Returns the OuterBorder of the realized container at the given item index, or null if the
    /// index is out of range or the container is virtualized off-screen.
    /// </summary>
    private Border? OuterBorderForIndex(int index)
    {
        if (index < 0 || index >= TabList.Items.Count) return null;
        if (TabList.Items[index] is not NoteTab tab) return null;
        var container = ContainerFor(tab);
        return container is null ? null : FindNamedDescendant<Border>(container, "OuterBorder");
    }

    /// <summary>
    /// Completes the drag operation: moves the tab in the collection, updates sort orders.
    /// </summary>
    private void CompleteDrag()
    {
        if (!_isDragging) { ResetDragState(); return; }

        // Re-entrancy guard — Mouse.Capture(null) can re-fire LostMouseCapture
        _isCompletingDrag = true;
        try
        {
            Mouse.Capture(null);
            RemoveDropIndicator();

            // Restore old item opacity (no-move path)
            if (_dragItem is not null)
            {
                var oldBorder = FindNamedDescendant<Border>(_dragItem, "OuterBorder");
                if (oldBorder is not null) oldBorder.Opacity = 1.0;
            }

            if (_dragInsertIndex >= 0 && _dragTab is not null)
            {
                int oldIndex = _tabs.IndexOf(_dragTab);
                // Drag is disabled while searching, so the item order equals the _tabs order and
                // there are no separator rows — the item insert index IS the collection index.
                int newIndex = Math.Min(_dragInsertIndex, _tabs.Count);

                if (ViewModel.MoveTab(oldIndex, newIndex))
                {
                    _ = NoteStore.UpdateNoteSortOrdersAsync(
                        _tabs.Where(t => t.Id > 0).Select(t => (t.Id, t.SortOrder)));

                    // Mark tab for fade-in animation (handled in TabItemBorder_Loaded)
                    _fadeInTab = _dragTab;
                    RebuildTabList();
                    SelectTabByNote(_dragTab);
                }
            }

            ResetDragState();
        }
        finally
        {
            _isCompletingDrag = false;
        }
    }

    /// <summary>
    /// Removes the visual drop indicator, restoring the border to its original state.
    /// </summary>
    private void RemoveDropIndicator()
    {
        if (_dropIndicatorBorder is not null)
        {
            _dropIndicatorBorder.BorderThickness = new Thickness(0);
            _dropIndicatorBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
            _dropIndicatorBorder = null;
        }
    }

    private void ResetDragState()
    {
        _isDragging = false;
        if (_dragItem is not null)
        {
            var resetBorder = FindNamedDescendant<Border>(_dragItem, "OuterBorder");
            if (resetBorder is not null) resetBorder.Opacity = 1.0;
        }
        _dragItem = null;
        _dragTab = null;
        _dragInsertIndex = -1;
        _dragOriginalListIndex = -1;
    }
}
