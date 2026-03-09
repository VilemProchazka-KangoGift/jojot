using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JoJot.Controls;

public partial class RecoveryPanel : UserControl
{
    public event EventHandler? CloseRequested;

    internal StackPanel SessionList_ => SessionList;

    public RecoveryPanel()
    {
        InitializeComponent();
    }

    public void Show()
    {
        Visibility = Visibility.Visible;
        var anim = new DoubleAnimation
        {
            From = 320, To = 0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        PanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    public void Hide()
    {
        var anim = new DoubleAnimation
        {
            From = 0, To = 320,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            PanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
            PanelTransform.X = 320;
        };
        PanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void Close_Click(object sender, MouseButtonEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
