using System.Runtime.InteropServices;
using JoJot.Services;

namespace JoJot.Interop
{
    /// <summary>
    /// Low-level COM activation and lifecycle for virtual desktop interop.
    /// All raw COM operations are contained here — no COM types escape this class.
    /// VirtualDesktopService is the only consumer.
    /// </summary>
    internal static class VirtualDesktopInterop
    {
        private static IVirtualDesktopManager? _manager;
        private static IVirtualDesktopManagerInternal? _managerInternal;
        private static IVirtualDesktopNotificationService? _notificationService;
        private static GuidSet? _guidSet;
        private static bool _initialized;

        /// <summary>
        /// Initializes COM interfaces for virtual desktop interaction.
        /// Must be called on the WPF UI thread (STA requirement for shell COM objects).
        /// Throws PlatformNotSupportedException if OS build is unsupported.
        /// Throws COMException if COM activation fails.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized)
                return;

            int buildNumber = Environment.OSVersion.Version.Build;
            LogService.Info($"VirtualDesktopInterop: initializing for build {buildNumber}");

            _guidSet = ComGuids.Resolve(buildNumber);
            if (_guidSet is null)
                throw new PlatformNotSupportedException(
                    $"Unsupported OS build {buildNumber} — virtual desktop features disabled");

            // 1. Create the documented IVirtualDesktopManager
            var managerType = Type.GetTypeFromCLSID(ComGuids.CLSID_VirtualDesktopManager);
            if (managerType is null)
                throw new COMException("Failed to resolve CLSID_VirtualDesktopManager");

            _manager = (IVirtualDesktopManager)Activator.CreateInstance(managerType)!;

            // 2. Create ImmersiveShell and get IServiceProvider
            var shellType = Type.GetTypeFromCLSID(ComGuids.CLSID_ImmersiveShell);
            if (shellType is null)
                throw new COMException("Failed to resolve CLSID_ImmersiveShell");

            var shell = Activator.CreateInstance(shellType);
            if (shell is null)
                throw new COMException("Failed to create ImmersiveShell instance");

            var provider = (IServiceProvider10)shell;

            // 3. Query for IVirtualDesktopManagerInternal
            var clsidInternal = ComGuids.CLSID_VirtualDesktopManagerInternal;
            var iidInternal = _guidSet.IVirtualDesktopManagerInternal;

            int hr = provider.QueryService(ref clsidInternal, ref iidInternal, out IntPtr internalPtr);
            if (hr != 0 || internalPtr == IntPtr.Zero)
                throw new COMException(
                    $"QueryService for IVirtualDesktopManagerInternal failed (HRESULT: 0x{hr:X8})", hr);

            _managerInternal = (IVirtualDesktopManagerInternal)
                Marshal.GetObjectForIUnknown(internalPtr);
            Marshal.Release(internalPtr);

            // 4. Query for IVirtualDesktopNotificationService
            var iidNotifService = _guidSet.IVirtualDesktopNotificationService;
            hr = provider.QueryService(ref clsidInternal, ref iidNotifService, out IntPtr notifPtr);
            if (hr != 0 || notifPtr == IntPtr.Zero)
            {
                // Notification service is optional — title updates won't work but detection still does
                LogService.Warn(
                    $"QueryService for IVirtualDesktopNotificationService failed (HRESULT: 0x{hr:X8}) — live notifications disabled");
                _notificationService = null;
            }
            else
            {
                _notificationService = (IVirtualDesktopNotificationService)
                    Marshal.GetObjectForIUnknown(notifPtr);
                Marshal.Release(notifPtr);
            }

            _initialized = true;
            LogService.Info("VirtualDesktopInterop: initialization complete");
        }

        /// <summary>
        /// Gets the desktop GUID for the window identified by hwnd.
        /// </summary>
        public static Guid GetWindowDesktopId(IntPtr hwnd)
        {
            EnsureInitialized();
            int hr = _manager!.GetWindowDesktopId(hwnd, out Guid desktopId);
            if (hr != 0)
                throw new COMException($"GetWindowDesktopId failed (HRESULT: 0x{hr:X8})", hr);
            return desktopId;
        }

        /// <summary>
        /// Moves a window to the specified virtual desktop.
        /// Used by DRAG-06 (Cancel/Go back) to return a window to its origin desktop.
        /// </summary>
        public static void MoveWindowToDesktop(IntPtr hwnd, Guid desktopId)
        {
            EnsureInitialized();
            int hr = _manager!.MoveWindowToDesktop(hwnd, ref desktopId);
            if (hr != 0)
                throw new COMException($"MoveWindowToDesktop failed (HRESULT: 0x{hr:X8})", hr);
        }

        /// <summary>
        /// Gets the current virtual desktop's GUID, name, and index.
        /// </summary>
        public static (Guid Id, string Name, int Index) GetCurrentDesktop()
        {
            EnsureInitialized();

            int hr = _managerInternal!.GetCurrentDesktop(out IVirtualDesktop desktop);
            if (hr != 0)
                throw new COMException($"GetCurrentDesktop failed (HRESULT: 0x{hr:X8})", hr);

            Guid id = desktop.GetId();
            string name;
            try
            {
                name = desktop.GetName() ?? "";
            }
            catch
            {
                name = "";
            }
            // Fallback: read from registry when COM GetName() returns empty (e.g., Win11 25H2 build 26200+)
            if (string.IsNullOrEmpty(name))
            {
                name = GetDesktopNameFromRegistry(id);
            }

            // Find index by enumerating all desktops
            int index = 0;
            var allDesktops = GetAllDesktopsInternal();
            for (int i = 0; i < allDesktops.Count; i++)
            {
                if (allDesktops[i].Id == id)
                {
                    index = i;
                    break;
                }
            }

            return (id, name, index);
        }

        /// <summary>
        /// Enumerates all virtual desktops and returns their GUIDs, names, and indices.
        /// </summary>
        public static List<(Guid Id, string Name, int Index)> GetAllDesktopsInternal()
        {
            EnsureInitialized();

            int hr = _managerInternal!.GetDesktops(out IObjectArray desktops);
            if (hr != 0)
                throw new COMException($"GetDesktops failed (HRESULT: 0x{hr:X8})", hr);

            hr = desktops.GetCount(out uint count);
            if (hr != 0)
                throw new COMException($"IObjectArray.GetCount failed (HRESULT: 0x{hr:X8})", hr);

            var result = new List<(Guid, string, int)>((int)count);
            var iidVd = _guidSet!.IVirtualDesktop;

            for (uint i = 0; i < count; i++)
            {
                hr = desktops.GetAt(i, ref iidVd, out object desktopObj);
                if (hr != 0)
                {
                    LogService.Warn($"IObjectArray.GetAt({i}) failed (HRESULT: 0x{hr:X8}) — skipping");
                    continue;
                }

                var desktop = (IVirtualDesktop)desktopObj;
                Guid id = desktop.GetId();
                string name;
                try
                {
                    name = desktop.GetName() ?? "";
                }
                catch
                {
                    name = "";
                }
                // Fallback: read from registry when COM GetName() returns empty (e.g., Win11 25H2 build 26200+)
                if (string.IsNullOrEmpty(name))
                {
                    name = GetDesktopNameFromRegistry(id);
                }

                result.Add((id, name, (int)i));
            }

            return result;
        }

        /// <summary>
        /// Gets the notification service for registering callbacks.
        /// Returns null if notifications are not available.
        /// </summary>
        public static IVirtualDesktopNotificationService? GetNotificationService()
        {
            return _notificationService;
        }

        /// <summary>
        /// Releases all COM objects and resets state.
        /// Safe to call multiple times.
        /// </summary>
        public static void Dispose()
        {
            if (_notificationService is not null)
            {
                try { Marshal.ReleaseComObject(_notificationService); } catch { }
                _notificationService = null;
            }

            if (_managerInternal is not null)
            {
                try { Marshal.ReleaseComObject(_managerInternal); } catch { }
                _managerInternal = null;
            }

            if (_manager is not null)
            {
                try { Marshal.ReleaseComObject(_manager); } catch { }
                _manager = null;
            }

            _guidSet = null;
            _initialized = false;
        }

        /// <summary>
        /// Reads a virtual desktop's name from the registry as a fallback when COM GetName() fails.
        /// Windows stores desktop names at:
        /// HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops\{GUID}\Name
        /// Returns empty string if the key doesn't exist (unnamed desktop) or on any error.
        /// </summary>
        private static string GetDesktopNameFromRegistry(Guid desktopId)
        {
            try
            {
                string guidStr = desktopId.ToString("B"); // {xxxxxxxx-xxxx-...} format
                string keyPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops\{guidStr}";
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath);
                return key?.GetValue("Name") as string ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static void EnsureInitialized()
        {
            if (!_initialized)
                throw new InvalidOperationException("VirtualDesktopInterop is not initialized. Call Initialize() first.");
        }
    }
}
