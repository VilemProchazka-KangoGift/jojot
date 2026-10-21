namespace JoJot;

public partial class MainWindow
{
    // ─── Help Overlay (Ctrl+?) ──────────────────────────────────────────

    private void ShowHelpOverlay()
    {
        ViewModel.IsHelpOpen = true;
        HelpOverlayLazy.Show();
    }

    private void HideHelpOverlay()
    {
        ViewModel.IsHelpOpen = false;
        _helpOverlay?.Hide();
    }
}
