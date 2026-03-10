using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JoJot.Models;
using JoJot.Services;
using JoJot.ViewModels;

namespace JoJot;

public partial class MainWindow
{
    // ─── Input Binding Commands ────────────────────────────────────────────────

    internal ICommand NewTabCommand { get; private set; } = null!;
    internal ICommand CloseTabCommand { get; private set; } = null!;
    internal ICommand TogglePinCommand { get; private set; } = null!;
    internal ICommand CloneTabCommand { get; private set; } = null!;
    internal ICommand SaveAsCommand { get; private set; } = null!;
    internal ICommand ToggleHelpCommand { get; private set; } = null!;
    internal ICommand IncreaseFontCommand { get; private set; } = null!;
    internal ICommand DecreaseFontCommand { get; private set; } = null!;
    internal ICommand ResetFontCommand { get; private set; } = null!;

    /// <summary>
    /// Creates keyboard shortcut commands and registers them as InputBindings.
    /// Called from constructor after InitializeComponent.
    /// Simple action shortcuts use InputBindings; complex/context-dependent
    /// shortcuts remain in Window_PreviewKeyDown.
    /// </summary>
    private void InitializeInputBindings()
    {
        NewTabCommand = new RelayCommand(() => _ = CreateNewTabAsync());
        CloseTabCommand = new RelayCommand(
            () => _ = DeleteTabAsync(_activeTab!),
            () => _activeTab is not null);
        TogglePinCommand = new RelayCommand(
            () => _ = TogglePinAsync(_activeTab!),
            () => _activeTab is not null);
        CloneTabCommand = new RelayCommand(
            () => _ = CloneTabAsync(_activeTab!),
            () => _activeTab is not null);
        SaveAsCommand = new RelayCommand(
            () => SaveAsTxt(),
            () => _activeTab is not null);
        ToggleHelpCommand = new RelayCommand(() =>
        {
            if (ViewModel.IsHelpOpen) HideHelpOverlay();
            else ShowHelpOverlay();
        });
        IncreaseFontCommand = new RelayCommand(() => _ = ChangeFontSizeAsync(1));
        DecreaseFontCommand = new RelayCommand(() => _ = ChangeFontSizeAsync(-1));
        ResetFontCommand = new RelayCommand(() => _ = SetFontSizeAsync(13));

        InputBindings.Add(new KeyBinding(NewTabCommand, Key.T, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(CloseTabCommand, Key.W, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(TogglePinCommand, Key.P, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(CloneTabCommand, Key.K, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(SaveAsCommand, Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ToggleHelpCommand, Key.OemQuestion, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(IncreaseFontCommand, Key.OemPlus, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(IncreaseFontCommand, Key.Add, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(DecreaseFontCommand, Key.OemMinus, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(DecreaseFontCommand, Key.Subtract, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ResetFontCommand, Key.D0, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ResetFontCommand, Key.NumPad0, ModifierKeys.Control));
    }

    // ─── Keyboard Shortcuts ─────────────────────────────────────────────────

    /// <summary>
    /// Window-level keyboard shortcut handler for guards and complex shortcuts.
    /// Simple action shortcuts (Ctrl+T/W/P/K/S, help toggle, font size) are
    /// handled by InputBindings — see InitializeInputBindings.
    /// Guards here block ALL keys (including InputBindings) during modal states.
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
        if (ConfirmationOverlay.IsOpen)
        {
            if (e.Key == Key.Escape)
            {
                HideConfirmation();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                ConfirmationOverlay.Confirm();
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

        // Find panel: Enter cycles forward, Shift+Enter cycles backward
        if (e.Key == Key.Enter && _findPanelOpen)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                CycleFindMatch(forward: false);
            else
                CycleFindMatch(forward: true);
            e.Handled = true;
            return;
        }

        // Escape closes find panel if open
        if (e.Key == Key.Escape && _findPanelOpen)
        {
            HideFindPanel();
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
        if (PreferencesPanel.IsRecordingHotkey)
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
                    PreferencesPanel.StopRecording();
                    PreferencesPanel.UpdateHotkeyDisplay(HotkeyService.GetHotkeyDisplayString());
                    if (!success)
                    {
                        ShowInfoToast("Hotkey already in use by another app");
                    }
                });
            });

            e.Handled = true;
            return;
        }

        // Ctrl+Z: Undo — MUST be in PreviewKeyDown to prevent WPF native undo
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

        // Ctrl+F or Ctrl+H: Open find panel (replace always visible)
        if ((e.Key == Key.F || e.Key == Key.H) && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowFindPanel();
            e.Handled = true;
            return;
        }

        // F2: Rename active tab
        if (e.Key == Key.F2 && TabList.SelectedItem is ListBoxItem f2Item && f2Item.Tag is NoteTab f2Tab)
        {
            BeginRename(f2Item, f2Tab);
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
