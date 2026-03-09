namespace JoJot;

public partial class MainWindow
{
    // ─── Confirmation Overlay ──

    private void ShowConfirmation(string title, string message, Action? onConfirm)
    {
        ConfirmationOverlay.Show(title, message, onConfirm);
    }

    private void HideConfirmation()
    {
        ConfirmationOverlay.Hide();
    }
}
