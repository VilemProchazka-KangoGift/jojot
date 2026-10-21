namespace JoJot;

public partial class MainWindow
{
    // ─── Confirmation Overlay ───────────────────────────────────────────

    private void ShowConfirmation(string title, string message, Action? onConfirm, string? confirmText = null, bool useDangerStyle = true)
    {
        ConfirmationOverlayLazy.Show(title, message, onConfirm, confirmText, useDangerStyle);
    }

    private void HideConfirmation()
    {
        _confirmationOverlay?.Hide();
    }
}
