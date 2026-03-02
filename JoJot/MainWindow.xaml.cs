using System.ComponentModel;
using System.Windows;
using JoJot.Services;

namespace JoJot
{
    /// <summary>
    /// Per-desktop window bound to a virtual desktop GUID.
    /// Phase 3: Windows are created on-demand and destroyed on close (not hidden).
    /// The process stays alive via ShutdownMode.OnExplicitShutdown.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly string _desktopGuid;

        /// <summary>
        /// Creates a MainWindow bound to a specific virtual desktop.
        /// The desktopGuid is used for geometry save/restore and window registry keying.
        /// </summary>
        public MainWindow(string desktopGuid)
        {
            _desktopGuid = desktopGuid;
            InitializeComponent();
        }

        /// <summary>
        /// The virtual desktop GUID this window is bound to.
        /// Used by App for registry keying and event routing.
        /// </summary>
        public string DesktopGuid => _desktopGuid;

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
        /// Creates a new empty tab and focuses it.
        /// Phase 3 stub: logs the request. Full implementation in Phase 4 when tab UI exists.
        /// </summary>
        public void RequestNewTab()
        {
            LogService.Info($"RequestNewTab called for desktop {_desktopGuid} (stub until Phase 4)");
            // Phase 4 will: create notes row, add tab to UI, focus editor
        }

        /// <summary>
        /// Flushes content, removes empty tabs, saves window geometry, then closes for real.
        /// Called by App on explicit shutdown (Exit menu, etc.).
        /// Phase 3: saves geometry. Full content flush deferred to Phase 6.
        /// </summary>
        public void FlushAndClose()
        {
            LogService.Info($"FlushAndClose called for desktop {_desktopGuid}");
            // Phase 6 will: flush tab content, delete empty tabs
            // Geometry is saved in OnClosing which fires when Close() is called below
            Close();
        }

        /// <summary>
        /// TASK-05: Window close saves geometry, flushes content, deletes empty tabs, then destroys.
        /// The process stays alive (ShutdownMode.OnExplicitShutdown).
        /// Unlike Phase 1 which hid the window, Phase 3 actually destroys it.
        /// The window is recreated fresh when needed via IPC (WPF windows cannot be reopened after Close).
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            // Capture geometry while the HWND is still valid
            var geo = WindowPlacementHelper.CaptureGeometry(this);

            // Fire-and-forget: save geometry to database (process stays alive so this completes)
            _ = DatabaseService.SaveWindowGeometryAsync(_desktopGuid, geo);

            // Stub: flush content and delete empty tabs (Phase 6 will implement content flush)
            LogService.Info($"Window closing for desktop {_desktopGuid} \u2014 geometry saved ({geo.Left},{geo.Top} {geo.Width}x{geo.Height} maximized={geo.IsMaximized})");

            // Do NOT set e.Cancel = true — let the window close and be destroyed
        }
    }
}
