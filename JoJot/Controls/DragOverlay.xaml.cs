using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using JoJot.Resources;

namespace JoJot.Controls;

/// <summary>
/// Overlay shown when a window is dragged between virtual desktops, presenting keep/merge/cancel options.
/// </summary>
public partial class DragOverlay : UserControl
{
    public event EventHandler? KeepHereClicked;
    public event EventHandler? MergeClicked;
    public event EventHandler? CancelClicked;

    public DragOverlay()
    {
        InitializeComponent();
    }

    public void Show(string sourceName, string title, string message, bool showKeepHere, bool showMerge)
    {
        SourceName.Text = sourceName;
        TitleText.Text = title;
        MessageText.Text = message;
        KeepHereButton.Visibility = showKeepHere ? Visibility.Visible : Visibility.Collapsed;
        MergeButton.Visibility = showMerge ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Content = Strings.Drag_GoBack;
        CancelFailureText.Visibility = Visibility.Collapsed;

        Opacity = 0;
        Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fadeIn);
    }

    public void UpdateContent(string message, bool showKeepHere, bool showMerge)
    {
        MessageText.Text = message;
        KeepHereButton.Visibility = showKeepHere ? Visibility.Visible : Visibility.Collapsed;
        MergeButton.Visibility = showMerge ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowRetryMode()
    {
        CancelButton.Content = Strings.Drag_Retry;
        CancelFailureText.Visibility = Visibility.Visible;
    }

    public async Task HideAsync()
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        var tcs = new TaskCompletionSource<bool>();
        fadeOut.Completed += (_, _) => tcs.SetResult(true);
        BeginAnimation(OpacityProperty, fadeOut);
        await tcs.Task;

        Visibility = Visibility.Collapsed;
    }

    public void HideImmediate()
    {
        Visibility = Visibility.Collapsed;
    }

    private void KeepHereButton_Click(object sender, RoutedEventArgs e)
    {
        KeepHereClicked?.Invoke(this, EventArgs.Empty);
    }

    private void MergeButton_Click(object sender, RoutedEventArgs e)
    {
        MergeClicked?.Invoke(this, EventArgs.Empty);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelClicked?.Invoke(this, EventArgs.Empty);
    }
}
