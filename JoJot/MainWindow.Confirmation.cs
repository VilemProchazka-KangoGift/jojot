using System.Windows;
using System.Windows.Input;

namespace JoJot;

public partial class MainWindow
{
    // ─── Confirmation Overlay ──

    /// <summary>
    /// Shows the confirmation overlay with a title and message.
    /// The onConfirm action is called when the user clicks Delete.
    /// </summary>
    private void ShowConfirmation(string title, string message, Action? onConfirm)
    {
        ConfirmTitle.Text = title;
        ConfirmMessage.Text = message;
        _confirmAction = onConfirm;
        ConfirmDeleteButton.Visibility = onConfirm is not null ? Visibility.Visible : Visibility.Collapsed;
        ConfirmCancelButton.Content = onConfirm is not null ? "Cancel" : "OK";
        ConfirmationOverlay.Visibility = Visibility.Visible;
        ConfirmCancelButton.Focus();
    }

    private void HideConfirmation()
    {
        ConfirmationOverlay.Visibility = Visibility.Collapsed;
        _confirmAction = null;
    }

    /// <summary>
    /// Backdrop click dismisses the confirmation overlay without deleting.
    /// </summary>
    private void ConfirmOverlayBackdrop_Click(object sender, MouseButtonEventArgs e)
    {
        HideConfirmation();
    }

    /// <summary>
    /// Cancel button hides the confirmation overlay without deleting.
    /// </summary>
    private void ConfirmCancel_Click(object sender, RoutedEventArgs e)
    {
        HideConfirmation();
    }

    /// <summary>
    /// Delete button executes the confirmed bulk delete action.
    /// </summary>
    private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        var action = _confirmAction;
        HideConfirmation();
        action?.Invoke();
    }
}
