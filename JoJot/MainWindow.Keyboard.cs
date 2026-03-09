using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JoJot.Models;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── Keyboard Shortcuts ─────────────────────────────────────────────────

    /// <summary>
    /// Window-level keyboard shortcut handler.
    /// Ctrl+W: delete active tab, Ctrl+T: new tab, Ctrl+F: focus search,
    /// Ctrl+Tab/Ctrl+Shift+Tab: cycle tabs, Ctrl+P: pin/unpin, Ctrl+K: clone, F2: rename.
    /// </summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Block all keyboard shortcuts while drag overlay is active
        if (_isDragOverlayActive)
        {
            e.Handled = true;
            return;
        }

        // Escape cancels rename — check before panels/overlays
        if (e.Key == Key.Escape && _activeRename is not null)
        {
            CancelRename();
            Keyboard.Focus(ContentEditor);
            e.Handled = true;
            return;
        }

        // Confirmation dialog keyboard handling — intercept before all other shortcuts
        if (ConfirmationOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                HideConfirmation();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                var action = _confirmAction;
                HideConfirmation();
                action?.Invoke();
                e.Handled = true;
            }
            else
            {
                e.Handled = true; // Block all other keyboard shortcuts while dialog is open
            }
            return;
        }

        // Escape closes help overlay if visible
        if (e.Key == Key.Escape && ViewModel.IsHelpOpen)
        {
            HideHelpOverlay();
            e.Handled = true;
            return;
        }

        // Escape closes editor find bar if visible
        if (e.Key == Key.Escape && EditorFindBar.Visibility == Visibility.Visible)
        {
            HideEditorFindBar();
            e.Handled = true;
            return;
        }

        // Escape closes cleanup panel if visible
        if (e.Key == Key.Escape && _cleanupPanelOpen)
        {
            HideCleanupPanel();
            e.Handled = true;
            return;
        }

        // Escape closes recovery sidebar if visible
        if (e.Key == Key.Escape && _recoveryPanelOpen)
        {
            HideRecoveryPanel();
            e.Handled = true;
            return;
        }

        // Escape closes preferences panel if visible
        if (e.Key == Key.Escape && _preferencesOpen)
        {
            HidePreferencesPanel();
            e.Handled = true;
            return;
        }

        // Hotkey recording — capture key combination in preferences panel
        if (_recordingHotkey)
        {
            var mods = Keyboard.Modifiers;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Ignore lone modifier presses
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift ||
                key == Key.RightShift || key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
            {
                e.Handled = true;
                return;
            }

            // Require at least one modifier
            if (mods == ModifierKeys.None)
            {
                e.Handled = true;
                return;
            }

            uint win32Mods = HotkeyService.ModifierKeysToWin32(mods);
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

            _ = Task.Run(async () =>
            {
                bool success = await HotkeyService.UpdateHotkeyAsync(win32Mods, vk);
                await Dispatcher.InvokeAsync(() =>
                {
                    _recordingHotkey = false;
                    HotkeyRecordText.Text = "Record";
                    HotkeyDisplay.Text = HotkeyService.GetHotkeyDisplayString();
                    if (!success)
                    {
                        ShowInfoToast("Hotkey already in use by another app");
                    }
                });
            });

            e.Handled = true;
            return;
        }

        // Ctrl+Z: Undo — MUST be first to prevent WPF native undo
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_activeTab is not null)
            {
                PerformUndo();
            }
            e.Handled = true; // Always handle to prevent WPF native undo
            return;
        }

        // Ctrl+Y or Ctrl+Shift+Z: Redo
        if ((e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control) ||
            (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
        {
            if (_activeTab is not null)
            {
                PerformRedo();
            }
            e.Handled = true;
            return;
        }

        // Ctrl+C: Enhanced copy — no selection copies entire note
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (ContentEditor.SelectionLength == 0 && _activeTab is not null && !string.IsNullOrEmpty(_activeTab.Content))
            {
                try
                {
                    Clipboard.SetText(_activeTab.Content);
                }
                catch (Exception ex)
                {
                    LogService.Warn("Clipboard access failed: {ErrorMessage}", ex.Message);
                }
                e.Handled = true;
                return;
            }
            // If there IS a selection, do NOT set e.Handled — let WPF handle normal copy
            return;
        }

        // Ctrl+S: Save as TXT
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_activeTab is not null)
            {
                SaveAsTxt();
            }
            e.Handled = true;
            return;
        }

        // Ctrl+= or Ctrl+NumAdd: Increase font size
        if ((e.Key == Key.OemPlus || e.Key == Key.Add) && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _ = ChangeFontSizeAsync(1);
            e.Handled = true;
            return;
        }

        // Ctrl+- or Ctrl+NumSubtract: Decrease font size
        if ((e.Key == Key.OemMinus || e.Key == Key.Subtract) && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _ = ChangeFontSizeAsync(-1);
            e.Handled = true;
            return;
        }

        // Ctrl+0 or Ctrl+Numpad0: Reset font size to 13pt
        if ((e.Key == Key.D0 || e.Key == Key.NumPad0) && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _ = SetFontSizeAsync(13);
            e.Handled = true;
            return;
        }

        // Ctrl+Shift+/ (Ctrl+?): Show help overlay
        if (e.Key == Key.OemQuestion && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (ViewModel.IsHelpOpen)
                HideHelpOverlay();
            else
                ShowHelpOverlay();
            e.Handled = true;
            return;
        }

        // Ctrl+W: Delete active tab
        if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_activeTab is not null)
            {
                _ = DeleteTabAsync(_activeTab);
                e.Handled = true;
            }
            return;
        }

        // Ctrl+P: Pin/unpin toggle
        if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_activeTab is not null)
            {
                _ = TogglePinAsync(_activeTab);
                e.Handled = true;
            }
            return;
        }

        // Ctrl+K: Clone tab
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_activeTab is not null)
            {
                _ = CloneTabAsync(_activeTab);
                e.Handled = true;
            }
            return;
        }

        // F2: Rename active tab
        if (e.Key == Key.F2 && TabList.SelectedItem is ListBoxItem f2Item && f2Item.Tag is NoteTab f2Tab)
        {
            BeginRename(f2Item, f2Tab);
            e.Handled = true;
            return;
        }

        // Ctrl+T: New tab
        if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _ = CreateNewTabAsync();
            e.Handled = true;
            return;
        }

        // Ctrl+F: Context-dependent
        // If editor is focused → show in-editor find bar; otherwise → focus tab search
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (ContentEditor.IsFocused)
            {
                ShowEditorFindBar();
            }
            else
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
            e.Handled = true;
            return;
        }

        // Ctrl+Tab / Ctrl+Shift+Tab: Cycle tabs
        if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            int count = TabList.Items.Count;
            if (count <= 1) { e.Handled = true; return; }

            int current = TabList.SelectedIndex;
            if (current < 0) current = 0;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Previous tab: skip separator items
                int next = current;
                do { next = (next - 1 + count) % count; }
                while (next != current && TabList.Items[next] is ListBoxItem li && li.Tag is not NoteTab);
                TabList.SelectedIndex = next;
            }
            else
            {
                // Next tab: skip separator items
                int next = current;
                do { next = (next + 1) % count; }
                while (next != current && TabList.Items[next] is ListBoxItem li && li.Tag is not NoteTab);
                TabList.SelectedIndex = next;
            }

            e.Handled = true;
            return;
        }
    }

    /// <summary>
    /// Tab list keyboard handler. F2 triggers rename.
    /// </summary>
    private void TabList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && TabList.SelectedItem is ListBoxItem selItem && selItem.Tag is NoteTab selTab)
        {
            BeginRename(selItem, selTab);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Double-click on tab triggers rename.
    /// </summary>
    private void TabList_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TabList.SelectedItem is ListBoxItem item && item.Tag is NoteTab tab)
        {
            BeginRename(item, tab);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Ctrl+Scroll over editor changes font size; over tab list scrolls normally.
    /// </summary>
    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        // Hit-test: only change font size if mouse is over the editor area
        var mousePos = e.GetPosition(ContentEditor);
        if (mousePos.X >= 0 && mousePos.X <= ContentEditor.ActualWidth &&
            mousePos.Y >= 0 && mousePos.Y <= ContentEditor.ActualHeight)
        {
            int delta = e.Delta > 0 ? 1 : -1;
            _ = ChangeFontSizeAsync(delta);
            e.Handled = true; // Prevent scroll
        }
        // If not over editor, don't handle — let tab list scroll normally
    }
}
