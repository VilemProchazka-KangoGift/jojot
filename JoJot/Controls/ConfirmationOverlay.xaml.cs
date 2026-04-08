using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JoJot.Controls;

/// <summary>
/// Modal overlay for confirming destructive actions. Supports confirm/cancel and info-only modes.
/// </summary>
public partial class ConfirmationOverlay : UserControl
{
    private Action? _confirmAction;

    public bool IsOpen => Visibility == Visibility.Visible;

    public ConfirmationOverlay()
    {
        InitializeComponent();
    }

    public void Show(string title, string message, Action? onConfirm, string? confirmText = null, bool useDangerStyle = true)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        _confirmAction = onConfirm;
        DeleteButton.Visibility = onConfirm is not null ? Visibility.Visible : Visibility.Collapsed;
        if (onConfirm is not null && confirmText is not null)
            DeleteButton.Content = confirmText;
        else
            DeleteButton.Content = JoJot.Resources.Strings.Confirm_Delete;
        DeleteButton.SetResourceReference(BackgroundProperty, useDangerStyle ? Themes.ThemeKeys.Danger : Themes.ThemeKeys.Accent);
        CancelButton.Content = onConfirm is not null ? JoJot.Resources.Strings.Confirm_Cancel : "OK";
        Visibility = Visibility.Visible;
        CancelButton.Focus();
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        _confirmAction = null;
    }

    public void Confirm()
    {
        var action = _confirmAction;
        Hide();
        action?.Invoke();
    }

    private void Backdrop_Click(object sender, MouseButtonEventArgs e)
    {
        Hide();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        Confirm();
    }
}
