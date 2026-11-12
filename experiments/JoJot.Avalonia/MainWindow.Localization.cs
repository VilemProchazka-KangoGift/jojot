using Avalonia.Automation;
using Avalonia.Controls;
using JoJot.Resources;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow
{
    private static string CoreLabel(string english, string czech) =>
        Strings.Culture?.TwoLetterISOLanguageName == "cs" ? czech : english;

    private static string OpenFilesLabel => CoreLabel("Open files…", "Otevřít soubory…");

    private void LocalizeCoreControls()
    {
        AddButton.Content = Strings.Date_NewNote;
        ToolTip.SetTip(AddButton, Strings.Tooltip_NewTab);
        OpenFilesMenuItem.Header = OpenFilesLabel;
        SearchBox.PlaceholderText = Strings.Search_Placeholder;
        AutomationProperties.SetName(SearchBox, Strings.Search_Placeholder);
        AutomationProperties.SetName(NoteList, CoreLabel("Notes", "Poznámky"));
        AutomationProperties.SetName(Editor, CoreLabel("Note editor", "Editor poznámky"));
        UndoToastButton.Content = Strings.Common_Undo;
        DropHeading.Text = Strings.Drop_FileHere;
        DropHint.Text = Strings.Drop_Hint;
        LabelToolbarButton(RenameButton, Strings.Ctx_Rename, Strings.Ctx_Rename + " (F2)");
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

    private static void LabelToolbarButton(Button button, string label, string tooltip)
    {
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, tooltip);
    }
}
