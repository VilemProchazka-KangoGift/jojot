using JoJot.Services;

namespace JoJot.Interop;

/// <summary>
/// Holds a set of COM interface GUIDs (IIDs) specific to a Windows build.
/// The undocumented virtual desktop interfaces change their GUIDs between major Windows releases.
/// </summary>
/// <param name="IVirtualDesktop">IID for the IVirtualDesktop interface.</param>
/// <param name="IVirtualDesktopManagerInternal">IID for the IVirtualDesktopManagerInternal interface.</param>
/// <param name="IVirtualDesktopNotification">IID for the IVirtualDesktopNotification callback interface.</param>
/// <param name="IVirtualDesktopNotificationService">IID for the IVirtualDesktopNotificationService registration interface.</param>
internal sealed record GuidSet(
    Guid IVirtualDesktop,
    Guid IVirtualDesktopManagerInternal,
    Guid IVirtualDesktopNotification,
    Guid IVirtualDesktopNotificationService);

/// <summary>
/// Build-number dispatch for Windows 11 virtual desktop COM GUIDs.
/// Maps OS build numbers to the correct set of interface GUIDs.
/// Returns null for unsupported builds, triggering fallback mode.
/// </summary>
internal static class ComGuids
{
    // ─── Stable CLSIDs (same across all Windows 11 builds) ──────────────

    /// <summary>CLSID for the ImmersiveShell COM object, used to obtain IServiceProvider.</summary>
    public static readonly Guid CLSID_ImmersiveShell =
        new("C2F03A33-21F5-47FA-B4BB-156362A2F239");

    /// <summary>CLSID for the internal virtual desktop manager COM object.</summary>
    public static readonly Guid CLSID_VirtualDesktopManagerInternal =
        new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");

    /// <summary>CLSID for the documented IVirtualDesktopManager COM object.</summary>
    public static readonly Guid CLSID_VirtualDesktopManager =
        new("AA509086-5CA9-4C25-8F95-589D3C07B48A");

    // ─── Documented interface GUID (stable) ─────────────────────────────

    /// <summary>IID for the documented IVirtualDesktopManager interface (stable across builds).</summary>
    public static readonly Guid IID_IVirtualDesktopManager =
        new("A5CD92FF-29BE-454C-8D04-D82879FB3F1B");

    // ─── Build-specific GUIDs ───────────────────────────────────────────

    private static readonly SortedDictionary<int, GuidSet> _buildMap = new()
    {
        // Windows 11 22H2 / 23H2 (build 22621 / 22631)
        [22621] = new GuidSet(
            IVirtualDesktop: new("3F07F4BE-B107-441A-AF0F-39D82529072C"),
            IVirtualDesktopManagerInternal: new("B2F925B9-5A0F-4D2E-9F4D-2B1507593C10"),
            IVirtualDesktopNotification: new("CD403E52-DEED-4C13-B437-B98380F2B1E8"),
            IVirtualDesktopNotificationService: new("0CD45DE4-2F0F-4211-ACE2-1B3C7C750E13")),

        // Windows 11 24H2 (build 26100+)
        [26100] = new GuidSet(
            IVirtualDesktop: new("3F07F4BE-B107-441A-AF0F-39D82529072C"),
            IVirtualDesktopManagerInternal: new("53F5CA0B-158F-4124-900C-057158060B27"),
            IVirtualDesktopNotification: new("C179334C-4295-40D3-BEA1-C654D965605A"),
            IVirtualDesktopNotificationService: new("88846798-1611-4D18-946A-5B8B2B5B0B80")),
    };

    /// <summary>
    /// Resolves the correct <see cref="GuidSet"/> for the given Windows build number.
    /// Returns null if the build is unsupported (below minimum or unknown).
    /// Uses the closest lower-or-equal build number from the map.
    /// </summary>
    public static GuidSet? Resolve(int buildNumber)
    {
        if (buildNumber < 22621)
        {
            LogService.Warn($"Unsupported OS build {buildNumber} — virtual desktop features disabled");
            return null;
        }

        // Find the closest lower-or-equal build number
        GuidSet? result = null;
        foreach (var kvp in _buildMap)
        {
            if (kvp.Key <= buildNumber)
            {
                result = kvp.Value;
            }
            else
            {
                break;
            }
        }

        if (result is not null)
        {
            LogService.Info($"COM GUIDs resolved for build {buildNumber}");
        }
        else
        {
            LogService.Warn($"No COM GUID mapping for build {buildNumber} — virtual desktop features disabled");
        }

        return result;
    }
}
