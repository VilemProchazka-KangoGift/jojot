using JoJot.Interop;
using JoJot.Models;

namespace JoJot.Services
{
    /// <summary>
    /// Public API for virtual desktop detection and session management.
    /// All COM interop is isolated behind this boundary — no COM types appear in the public API.
    /// Falls back silently to single-notepad mode when the COM API is unavailable.
    /// </summary>
    public static class VirtualDesktopService
    {
        private static bool _isAvailable;
        private static string _currentDesktopGuid = "default";
        private static string _currentDesktopName = "";
        private static int _currentDesktopIndex;

        /// <summary>
        /// Whether the virtual desktop COM API is available and functioning.
        /// When false, the app runs in single-notepad fallback mode.
        /// </summary>
        public static bool IsAvailable => _isAvailable;

        /// <summary>
        /// The GUID of the current virtual desktop as a string.
        /// Returns "default" in fallback mode.
        /// </summary>
        public static string CurrentDesktopGuid => _currentDesktopGuid;

        /// <summary>
        /// The name of the current virtual desktop.
        /// Returns empty string if name is not set or in fallback mode.
        /// </summary>
        public static string CurrentDesktopName => _currentDesktopName;

        /// <summary>
        /// Initializes the virtual desktop service.
        /// Must be called on the WPF UI thread (STA requirement for shell COM objects).
        /// On failure, sets IsAvailable=false and uses 'default' GUID — never throws.
        /// </summary>
        public static Task InitializeAsync()
        {
            try
            {
                VirtualDesktopInterop.Initialize();

                var (id, name, index) = VirtualDesktopInterop.GetCurrentDesktop();
                _currentDesktopGuid = id.ToString();
                _currentDesktopName = name;
                _currentDesktopIndex = index;
                _isAvailable = true;

                LogService.Info(
                    $"VirtualDesktopService: available=true, desktop={_currentDesktopGuid}, " +
                    $"name=\"{_currentDesktopName}\", index={_currentDesktopIndex}");
            }
            catch (Exception ex)
            {
                // Silent fallback — no UI indication per user decision
                LogService.Warn($"Virtual desktop API unavailable — fallback mode: {ex.Message}");
                _isAvailable = false;
                _currentDesktopGuid = "default";
                _currentDesktopName = "";
                _currentDesktopIndex = 0;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns information about all live virtual desktops.
        /// In fallback mode, returns a single-element list with a default desktop.
        /// </summary>
        public static IReadOnlyList<DesktopInfo> GetAllDesktops()
        {
            if (!_isAvailable)
                return new[] { new DesktopInfo(Guid.Empty, "", 0) };

            try
            {
                var desktops = VirtualDesktopInterop.GetAllDesktopsInternal();
                return desktops
                    .Select(d => new DesktopInfo(d.Id, d.Name, d.Index))
                    .ToList()
                    .AsReadOnly();
            }
            catch (Exception ex)
            {
                LogService.Warn($"Failed to enumerate desktops: {ex.Message}");
                return new[] { new DesktopInfo(Guid.Empty, "", 0) };
            }
        }

        /// <summary>
        /// Returns information about the current virtual desktop.
        /// In fallback mode, returns a default DesktopInfo.
        /// </summary>
        public static DesktopInfo GetCurrentDesktopInfo()
        {
            if (!_isAvailable)
                return new DesktopInfo(Guid.Empty, "", 0);

            try
            {
                var (id, name, index) = VirtualDesktopInterop.GetCurrentDesktop();
                return new DesktopInfo(id, name, index);
            }
            catch (Exception ex)
            {
                LogService.Warn($"Failed to get current desktop: {ex.Message}");
                return new DesktopInfo(Guid.Empty, "", 0);
            }
        }

        /// <summary>
        /// Shuts down the virtual desktop service and releases all COM objects.
        /// Safe to call even if not initialized or already shut down.
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                VirtualDesktopInterop.Dispose();
            }
            catch (Exception ex)
            {
                LogService.Warn($"Error during VirtualDesktopService shutdown: {ex.Message}");
            }

            _isAvailable = false;
            LogService.Info("VirtualDesktopService: shut down");
        }
    }
}
