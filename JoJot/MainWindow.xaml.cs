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
