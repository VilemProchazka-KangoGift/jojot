using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using JoJot.Themes;

namespace JoJot;

public partial class MainWindow
{
    // ─── Hamburger Menu ─────────────────────────────────────────────────

    /// <summary>
    /// Generic hover handler for themed menu item Borders (hover background highlight).
    /// </summary>
    private void MenuItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border b) b.Background = GetBrush(ThemeKeys.HoverBackground);
    }

    /// <summary>
    /// Checks if the mouse is currently over any visual within a Popup.
    /// Used for hamburger menu dismiss detection.
    /// </summary>
    private static bool IsMouseOverPopup(Popup popup)
    {
        return popup.Child is FrameworkElement child && child.IsMouseOver;
    }

    private static bool IsMouseOverElement(UIElement element)
    {
        return element.IsMouseOver;
    }

    private void MenuItem_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border b) b.Background = System.Windows.Media.Brushes.Transparent;
    }

    private void HamburgerButton_Click(object sender, RoutedEventArgs e)
    {
        // StaysOpen=False closes the popup before Click fires, so the toggle
        // always sees IsOpen=false. Detect this by checking if it just closed.
        if ((DateTime.UtcNow - _hamburgerClosedAt).TotalMilliseconds < 300)
            return;
        HamburgerMenu.IsOpen = !HamburgerMenu.IsOpen;
    }

    /// <summary>
    /// "Clean up tabs" menu click — opens the cleanup side panel.
    /// </summary>
    private void MenuCleanup_Click(object sender, MouseButtonEventArgs e)
    {
        HamburgerMenu.IsOpen = false;
        ShowCleanupPanel();
    }

    /// <summary>
    /// Recover sessions — opens the orphan recovery flyout panel.
    /// </summary>
    private void MenuRecover_Click(object sender, MouseButtonEventArgs e)
    {
        HamburgerMenu.IsOpen = false;
        ShowRecoveryPanel();
    }

    /// <summary>
    /// Exit — flush all windows and terminate.
    /// Uses Dispatcher.BeginInvoke so the menu closes before shutdown begins.
    /// </summary>
    private void MenuExit_Click(object sender, MouseButtonEventArgs e)
    {
        HamburgerMenu.IsOpen = false;
        if (Application.Current is App app)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ExitApplication(app);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private static void ExitApplication(App app)
    {
        var windows = app.GetAllWindows();
        foreach (var window in windows)
        {
            window.FlushAndClose();
        }
        Environment.Exit(0);
    }
}
