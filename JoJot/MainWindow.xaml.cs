using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using JoJot.Services;

namespace JoJot
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Called by the IPC command handler when an ActivateCommand arrives.
        /// Brings the window to the foreground at ApplicationIdle priority.
        /// If the window is hidden (process-alive state), shows it first.
        /// </summary>
        public void ActivateFromIpc()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (!IsVisible)
                {
                    ShowAndActivate();
                }
                else
                {
                    WindowActivationHelper.ActivateWindow(this);
                }
                LogService.Info("Window activated via IPC");
            }, DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// Shows the window (if hidden) and activates it.
        /// Used when IPC activate arrives and window was hidden.
        /// </summary>
        public void ShowAndActivate()
        {
            Show();
            WindowActivationHelper.ActivateWindow(this);
        }

        /// <summary>
        /// Flushes content, removes empty tabs, saves window geometry, then closes for real.
        /// Phase 1 stub: logs the call and immediately closes.
        /// Full implementation deferred to phases when tabs and content exist (PROC-06).
        /// </summary>
        public void FlushAndClose()
        {
            LogService.Info("FlushAndClose called");
            // Phase 1 stub — no tabs or content yet; just close.
            // Later phases will: flush tab content, delete empty tabs, persist window geometry.
            Close();
        }

        /// <summary>
        /// Updates the window title based on the current desktop identity.
        /// Title format (per user decision VDSK-06):
        ///   - "JoJot — {desktop name}" when name is known and non-empty
        ///   - "JoJot — Desktop N" when name is empty but index is known (N = index + 1)
        ///   - "JoJot" in fallback mode or when no desktop info available
        /// Uses em-dash (U+2014 —) with spaces, not hyphen (-) or en-dash (U+2013).
        /// </summary>
        public void UpdateDesktopTitle(string? desktopName, int? desktopIndex)
        {
            if (!string.IsNullOrEmpty(desktopName))
            {
                Title = $"JoJot \u2014 {desktopName}";
            }
            else if (desktopIndex.HasValue)
            {
                Title = $"JoJot \u2014 Desktop {desktopIndex.Value + 1}";
            }
            else
            {
                Title = "JoJot";
            }
        }

        /// <summary>
        /// PROC-05: process stays alive when the user closes the window.
        /// Instead of closing, we hide the window. The app only truly exits via FlushAndClose.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            // Cancel the OS close — we hide instead of closing
            e.Cancel = true;
            Hide();
            LogService.Info("Window hidden (process stays alive per PROC-05)");
        }
    }
}
