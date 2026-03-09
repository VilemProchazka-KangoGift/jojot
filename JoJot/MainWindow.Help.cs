namespace JoJot;

public partial class MainWindow
{
    // ─── Help Overlay (Ctrl+?) ─────────────────────────────────────

    private void ShowHelpOverlay()
    {
        ViewModel.IsHelpOpen = true;
        HelpOverlay.Show();
    }

    private void HideHelpOverlay()
    {
        ViewModel.IsHelpOpen = false;
        HelpOverlay.Hide();
    }
}
