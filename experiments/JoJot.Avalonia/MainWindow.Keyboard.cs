using Avalonia.Input;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow
{
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandlePanelShortcutRecording(e))
            return;
        if (e.Key == Key.Escape)
        {
            if (_renameTab is not null)
                FinishRename(save: false);
            else if (DismissSidePanel())
            {
                e.Handled = true;
                return;
            }
            else if (FindHost.IsVisible)
                HideFindPanel();
            else if (SearchBox.IsKeyboardFocusWithin)
            {
                SearchBox.Clear();
                Editor.Focus();
            }
            else
                return;
            e.Handled = true;
            return;
        }

        if (_ready && !_busy && !_closing && _renameTab is null &&
            e.KeyModifiers == KeyModifiers.Control && e.Key is Key.F or Key.H)
        {
            ShowFindPanel();
            e.Handled = true;
            return;
        }

        if (!_ready || _busy || _closing || _renameTab is not null || PanelHost.IsVisible)
            return;
        if (e.Key == Key.Enter && FindHost.IsVisible && !Editor.IsKeyboardFocusWithin &&
            (_findQuery?.IsKeyboardFocusWithin == true || _findReplace?.IsKeyboardFocusWithin == true))
        {
            e.Handled = true;
            NavigateFind(!e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            return;
        }
        if (e.Key == Key.Enter && SearchBox.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            Editor.Focus();
            return;
        }

        var note = _active;
        var ctrl = e.KeyModifiers == KeyModifiers.Control;
        var ctrlShift = e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift);
        if (e.Key == Key.F2 && note is not null)
        {
            e.Handled = true;
            BeginRename(note);
            return;
        }
        if (e.Key == Key.Insert && e.KeyModifiers == KeyModifiers.None && Editor.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            return;
        }
        if (!ctrl && !ctrlShift)
            return;

        var editorHistory = Editor.IsKeyboardFocusWithin ||
            (!SearchBox.IsKeyboardFocusWithin && _findQuery?.IsKeyboardFocusWithin != true &&
             _findReplace?.IsKeyboardFocusWithin != true);
        e.Handled = true;
        switch (e.Key)
        {
            case Key.T when ctrl: RequestNewTab(); break;
            case Key.W when ctrl && note is not null: await DeleteNoteWithUndoAsync(note); break;
            case Key.P when ctrl && note is not null: await TogglePinAsync(note); break;
            case Key.K when ctrl && note is not null: await CloneNoteAsync(note); break;
            case Key.S when note is not null: await SaveNoteAsync(note, ctrlShift); break;
            case Key.O when ctrl: await OpenFilesAsync(); break;
            case Key.Z when ctrl && editorHistory: MoveHistory(false); break;
            case Key.Y when ctrl && editorHistory:
            case Key.Z when ctrlShift && editorHistory: MoveHistory(true); break;
            case Key.C when ctrl && Editor.IsKeyboardFocusWithin && string.IsNullOrEmpty(Editor.SelectedText):
                await CopyNoteAsync();
                break;
            case Key.Tab: CycleTabs(!ctrlShift); break;
            case Key.OemQuestion when ctrlShift: ShowHelp(); break;
            case Key.OemPlus when ctrl:
            case Key.Add when ctrl: await ChangeEditorFontAsync(1); break;
            case Key.OemMinus when ctrl:
            case Key.Subtract when ctrl: await ChangeEditorFontAsync(-1); break;
            case Key.D0 when ctrl:
            case Key.NumPad0 when ctrl: await ChangeEditorFontAsync(13 - Editor.FontSize); break;
            default: e.Handled = false; break;
        }
    }

    private void CycleTabs(bool forward)
    {
        if (NoteList.ItemCount < 2)
            return;
        var index = Math.Max(0, NoteList.SelectedIndex);
        NoteList.SelectedIndex = (index + (forward ? 1 : -1) + NoteList.ItemCount) % NoteList.ItemCount;
        if (NoteList.SelectedItem is { } note)
            NoteList.ScrollIntoView(note);
    }

    private async Task ChangeEditorFontAsync(double delta)
    {
        try { await SetEditorFontAsync(Editor.FontSize + delta); }
        catch (Exception ex) { ReportError(ex); }
    }
}
