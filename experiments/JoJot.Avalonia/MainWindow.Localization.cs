using Avalonia.Automation;
using Avalonia.Controls;
using JoJot.Resources;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow
{
    private static string CoreLabel(string english, string czech) =>
        Strings.Culture?.TwoLetterISOLanguageName == "cs" ? czech : english;

    private static string OpenFilesLabel => CoreLabel("Open File(s)…", "Otevřít soubor(y)…");

    private void LocalizeCoreControls()
    {
        AutomationProperties.SetName(AddButton, Strings.Date_NewNote);
        ToolTip.SetTip(AddButton, Strings.Tooltip_NewTab);
        OpenFilesMenuItem.Header = OpenFilesLabel;
        SearchBox.PlaceholderText = Strings.Search_Placeholder;
        AutomationProperties.SetName(SearchBox, Strings.Search_Placeholder);
        AutomationProperties.SetName(NoteList, CoreLabel("Notes", "Poznámky"));
        AutomationProperties.SetName(Editor, CoreLabel("Note editor", "Editor poznámky"));
        UndoToastButton.Content = Strings.Common_Undo;
        DropHeading.Text = Strings.Drop_FileHere;
        DropHint.Text = Strings.Drop_Hint;
        LabelToolbarButton(CloneButton, Strings.Ctx_Clone, Strings.Tooltip_Clone);
        LabelToolbarButton(CopyButton, Strings.Help_Copy, Strings.Tooltip_Copy);
        LabelToolbarButton(PasteButton, Strings.Help_Paste, Strings.Tooltip_Paste);
        LabelToolbarButton(SaveButton, Strings.Ctx_Save, Strings.Tooltip_Save);
        LabelToolbarButton(SaveAsButton, Strings.Ctx_SaveAs, Strings.Tooltip_SaveAs);
        LabelToolbarButton(FindButton, Strings.Find_Header, Strings.Tooltip_Find);
        LabelToolbarButton(DeleteButton, Strings.Ctx_Delete, Strings.Tooltip_Delete);
        LabelToolbarButton(UndoButton, Strings.Help_Undo, Strings.Tooltip_Undo);
        LabelToolbarButton(RedoButton, Strings.Help_Redo, Strings.Tooltip_Redo);
    }

    private async Task RefreshLanguageAsync()
    {
        LocalizeCoreControls();
        PreferencesMenuItem.Header = Strings.Menu_Preferences;
        CleanupMenuItem.Header = Strings.Menu_Cleanup;
        RecoveryMenuItem.Header = Strings.Menu_Recover;
        ExitMenuItem.Header = Strings.Menu_Exit;
        UpdateOrphanBadge();
        foreach (var note in _notes)
            note.RefreshLocalizedLabels();
        UpdateEditorTitle();
        UpdateToolbarState();
        RefreshNoteList();

        RefreshFindLanguage();
        var panel = _currentPanel;
        if (panel is null || !_ready || _busy || _closing || _recordingGlobalShortcut)
            return;

        DismissSidePanel();
        switch (panel)
        {
            case "preferences": await ShowPreferencesAsync(); break;
            case "cleanup": await ShowCleanupAsync(); break;
            case "recovery": ShowRecovery(); break;
            case "help": ShowHelp(); break;
        }
    }

    private static void LabelToolbarButton(Button button, string label, string tooltip)
    {
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, tooltip);
    }
}
