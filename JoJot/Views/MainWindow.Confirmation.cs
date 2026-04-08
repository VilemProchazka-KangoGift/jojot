namespace JoJot;

public partial class MainWindow
{
    // ─── Confirmation Overlay ───────────────────────────────────────────

    private void ShowConfirmation(string title, string message, Action? onConfirm, string? confirmText = null, bool useDangerStyle = true)
    {
        ConfirmationOverlay.Show(title, message, onConfirm, confirmText, useDangerStyle);
    }

    private void HideConfirmation()
    {
        ConfirmationOverlay.Hide();
    }
}
