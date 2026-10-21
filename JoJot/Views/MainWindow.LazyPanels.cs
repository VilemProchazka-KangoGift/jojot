using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using JoJot.Controls;
using JoJot.Resources;
using JoJot.Services;

namespace JoJot;

/// <summary>
/// Lazily-created side panels and overlays. All seven start hidden, so building them in
/// InitializeComponent only costs cold-start time. Each lives in an empty, Collapsed
/// <see cref="Border"/> host in MainWindow.xaml that carries the layout properties (grid
/// position, ZIndex, Width, alignment); the control is created on first use, placed in its
/// host, and the host's Visibility is bound to the control's so the host takes no space and
/// no hit-testing while the control is hidden.
///
/// Read-only checks in guards (e.g. <see cref="IsConfirmationOpen"/>) must use the null-aware
/// helpers below, never the <c>...Lazy</c> accessors, so that a keystroke never forces a
/// panel into existence.
/// </summary>
public partial class MainWindow
{
    private FindReplacePanel? _findReplacePanel;
    private ConfirmationOverlay? _confirmationOverlay;
    private DragOverlay? _dragOverlay;
    private PreferencesPanel? _preferencesPanel;
    private HelpOverlay? _helpOverlay;
    private RecoveryPanel? _recoveryPanel;
    private CleanupPanel? _cleanupPanel;

    // ─── Null-aware guards (never create a control) ─────────────────────

    /// <summary>True when the confirmation dialog exists and is showing.</summary>
    private bool IsConfirmationOpen => _confirmationOverlay?.IsOpen == true;

    /// <summary>True when the preferences panel exists and is recording a hotkey.</summary>
    private bool IsRecordingHotkey => _preferencesPanel?.IsRecordingHotkey == true;

    // ─── Lazy accessors (create on first use) ───────────────────────────

    private FindReplacePanel FindPanelLazy => _findReplacePanel ??= CreateFindReplacePanel();
    private ConfirmationOverlay ConfirmationOverlayLazy => _confirmationOverlay ??= CreateConfirmationOverlay();
    private DragOverlay DragOverlayLazy => _dragOverlay ??= CreateDragOverlay();
    private PreferencesPanel PreferencesPanelLazy => _preferencesPanel ??= CreatePreferencesPanel();
    private HelpOverlay HelpOverlayLazy => _helpOverlay ??= CreateHelpOverlay();
    private RecoveryPanel RecoveryPanelLazy => _recoveryPanel ??= CreateRecoveryPanel();
    private CleanupPanel CleanupPanelLazy => _cleanupPanel ??= CreateCleanupPanel();

    /// <summary>
    /// Places a freshly created control in its XAML host and makes the host follow the
    /// control's own Visibility (the controls show/hide themselves by setting Visibility).
    /// </summary>
    private static T AttachToHost<T>(Border host, T control) where T : FrameworkElement
    {
        host.Child = control;
        host.SetBinding(VisibilityProperty, new Binding(nameof(Visibility))
        {
            Source = control,
            Mode = BindingMode.OneWay
        });
        return control;
    }

    // ─── Factories (event wiring formerly done in the constructor) ──────

    private FindReplacePanel CreateFindReplacePanel()
    {
        var panel = new FindReplacePanel();
        panel.CloseRequested += (_, _) => HideFindPanel();
        panel.FindTextChanged += OnFindTextChanged;
        panel.FindNextRequested += (_, _) => CycleFindMatch(forward: true);
        panel.FindPreviousRequested += (_, _) => CycleFindMatch(forward: false);
        panel.ReplaceRequested += (_, _) => PerformReplace();
        panel.ReplaceAllRequested += (_, _) => PerformReplaceAll();
        return AttachToHost(FindReplaceHost, panel);
    }

    private ConfirmationOverlay CreateConfirmationOverlay() =>
        AttachToHost(ConfirmationHost, new ConfirmationOverlay());

    private DragOverlay CreateDragOverlay()
    {
        var overlay = new DragOverlay();
        overlay.KeepHereClicked += (_, _) => DragKeepHere_Handler();
        overlay.MergeClicked += (_, _) => DragMerge_Handler();
        overlay.CancelClicked += (_, _) => DragCancel_Handler();
        return AttachToHost(DragOverlayHost, overlay);
    }

    private PreferencesPanel CreatePreferencesPanel()
    {
        var panel = new PreferencesPanel();
        panel.CloseRequested += (_, _) => HidePreferencesPanel();
        panel.ThemeChangeRequested += (_, theme) => _ = ThemeService.SetThemeAsync(theme);
        panel.LanguageChangeRequested += (_, lang) =>
        {
            if (lang == LanguageService.Current) return;
            ShowConfirmation(
                Strings.Language_RestartTitle,
                Strings.Language_RestartMessage,
                () => _ = LanguageService.SetLanguageAsync(lang),
                Strings.Language_RestartButton,
                useDangerStyle: false);
        };
        panel.FontSizeChangeRequested += (_, delta) => _ = ChangeFontSizeAsync(delta);
        panel.FontSizeResetRequested += (_, _) => _ = SetFontSizeAsync(13);
        panel.HotkeyRecordingChanged += (_, isRecording) =>
        {
            if (isRecording) HotkeyService.PauseHotkey();
            else HotkeyService.ResumeHotkey();
        };
        return AttachToHost(PreferencesHost, panel);
    }

    private HelpOverlay CreateHelpOverlay()
    {
        var overlay = new HelpOverlay();
        overlay.CloseRequested += (_, _) => ViewModel.IsHelpOpen = false;
        return AttachToHost(HelpOverlayHost, overlay);
    }

    private RecoveryPanel CreateRecoveryPanel()
    {
        var panel = new RecoveryPanel();
        panel.CloseRequested += (_, _) => HideRecoveryPanel();
        return AttachToHost(RecoveryHost, panel);
    }

    private CleanupPanel CreateCleanupPanel()
    {
        var panel = new CleanupPanel();
        panel.CloseRequested += (_, _) => HideCleanupPanel();
        panel.FilterChanged += (_, _) => { if (_cleanupPanelOpen) RefreshCleanupPreview(); };
        panel.DeleteRequested += (_, candidates) =>
        {
            int pinnedCount = candidates.Count(t => t.Pinned);
            string pinnedNote = pinnedCount > 0 ? string.Format(Strings.Cleanup_ConfirmPinnedNote, pinnedCount) : "";
            var msgFmt = LanguageService.Plural(Strings.Cleanup_ConfirmMessage_One, Strings.Cleanup_ConfirmMessage_Few, Strings.Cleanup_ConfirmMessage, candidates.Count);
            string message = string.Format(msgFmt, candidates.Count, pinnedNote);
            ShowConfirmation(Strings.Cleanup_ConfirmTitle, message, () => _ = ExecuteCleanupDeleteAsync(candidates));
        };
        panel.AutoDeleteDaysChanged += (_, value) =>
        {
            if (string.IsNullOrWhiteSpace(value))
                _ = PreferenceStore.SetPreferenceAsync("auto_delete_days", "");
            else if (int.TryParse(value, out int days) && days > 0)
                _ = PreferenceStore.SetPreferenceAsync("auto_delete_days", days.ToString());
        };
        return AttachToHost(CleanupHost, panel);
    }
}
