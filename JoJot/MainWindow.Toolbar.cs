using System.Windows;
using System.Windows.Input;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── Toolbar ───────────────────────

    private void ToolbarUndo_Click(object sender, RoutedEventArgs e) => PerformUndo();
    private void ToolbarRedo_Click(object sender, RoutedEventArgs e) => PerformRedo();

    private void ToolbarPin_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is not null)
            _ = TogglePinAsync(_activeTab);
    }

    private void ToolbarClone_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is not null)
            _ = CloneTabAsync(_activeTab);
    }

    private void ToolbarCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is null) return;

        try
        {
            if (ContentEditor.SelectionLength > 0)
                Clipboard.SetText(ContentEditor.SelectedText);
            else if (!string.IsNullOrEmpty(_activeTab.Content))
                Clipboard.SetText(_activeTab.Content);
        }
        catch (Exception ex)
        {
            LogService.Warn($"Clipboard access failed: {ex.Message}");
        }
    }

    private void ToolbarPaste_Click(object sender, RoutedEventArgs e)
    {
        ContentEditor.Focus();
        ApplicationCommands.Paste.Execute(null, ContentEditor);
    }

    private void ToolbarSave_Click(object sender, RoutedEventArgs e) => SaveAsTxt();

    private void ToolbarDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is not null)
            _ = DeleteTabAsync(_activeTab);
    }

    /// <summary>
    /// Updates toolbar button enabled states and pin icon based on active tab.
    /// Called from: tab selection change, undo/redo, pin toggle, text change.
    /// </summary>
    private void UpdateToolbarState()
    {
        bool hasTab = _activeTab is not null;

        ToolbarUndo.IsEnabled = hasTab && UndoManager.Instance.CanUndo(_activeTab!.Id);
        ToolbarRedo.IsEnabled = hasTab && UndoManager.Instance.CanRedo(_activeTab!.Id);
        ToolbarPin.IsEnabled = hasTab;
        ToolbarClone.IsEnabled = hasTab;
        ToolbarCopy.IsEnabled = hasTab;
        ToolbarPaste.IsEnabled = hasTab;
        ToolbarSave.IsEnabled = hasTab;
        ToolbarDelete.IsEnabled = hasTab;

        // Update pin icon: show Unpin when tab is already pinned
        if (hasTab && _activeTab!.Pinned)
        {
            PinIconText.Text = "\uE77A"; // Unpin
            ToolbarPin.ToolTip = "Unpin (Ctrl+P)";
        }
        else
        {
            PinIconText.Text = "\uE718"; // Pin
            ToolbarPin.ToolTip = "Pin (Ctrl+P)";
        }
    }
}
