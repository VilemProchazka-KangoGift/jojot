---
phase: quick
plan: 1
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Services/ThemeService.cs
  - JoJot/Views/MainWindow.xaml.cs
autonomous: true
requirements: ["QUICK-01"]
must_haves:
  truths:
    - "Dark theme shows dark title bar (dark background, light title text)"
    - "Light theme shows light title bar (default Windows styling)"
    - "System theme follows Windows setting for title bar color"
    - "Theme switching at runtime updates the title bar immediately"
  artifacts:
    - path: "JoJot/Services/ThemeService.cs"
      provides: "DWM interop for title bar dark mode + window registration + ApplyTitleBarTheme"
      contains: "DwmSetWindowAttribute"
    - path: "JoJot/Views/MainWindow.xaml.cs"
      provides: "Window registers with ThemeService on load"
      contains: "RegisterWindow"
  key_links:
    - from: "JoJot/Services/ThemeService.cs"
      to: "dwmapi.dll"
      via: "P/Invoke DwmSetWindowAttribute"
      pattern: "DwmSetWindowAttribute"
    - from: "JoJot/Views/MainWindow.xaml.cs"
      to: "JoJot/Services/ThemeService.cs"
      via: "RegisterWindow call in constructor after InitializeComponent"
      pattern: "ThemeService\\.RegisterWindow"
---

<objective>
Make the Windows title bar (caption bar with window title text and min/max/close buttons) respect the current JoJot theme. In dark mode, the title bar should be dark with light text. In light mode, it should use the default Windows light styling.

Purpose: The app body already themes correctly via ResourceDictionary swap, but the OS-drawn title bar stays white in dark mode, creating a jarring visual mismatch.

Output: ThemeService gains DWM interop to set `DWMWA_USE_IMMERSIVE_DARK_MODE` on all MainWindow instances, updating them on theme switch.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@JoJot/Services/ThemeService.cs
@JoJot/Views/MainWindow.xaml.cs
@JoJot/Themes/DarkTheme.xaml
@JoJot/Themes/LightTheme.xaml

<interfaces>
From JoJot/Services/ThemeService.cs:
```csharp
public static class ThemeService
{
    public enum AppTheme { Light, Dark, System }
    public static AppTheme CurrentSetting { get; }
    public static void ApplyTheme(AppTheme theme);
    public static async Task SetThemeAsync(AppTheme theme);
    public static void Shutdown();
}
```

From JoJot/Views/MainWindow.xaml.cs:
```csharp
public partial class MainWindow : Window
{
    public MainWindow(string desktopGuid)  // constructor calls InitializeComponent()
}
```

DWM interop already exists in the project (WindowInteropHelper used in App.xaml.cs line 281).
The project uses `[DllImport]` style P/Invoke (see HotkeyService.cs, WindowActivationHelper.cs).
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add DWM title bar dark mode support to ThemeService</name>
  <files>JoJot/Services/ThemeService.cs</files>
  <action>
Add DWM interop and window tracking to ThemeService:

1. Add P/Invoke declaration at the top of the class:
   ```csharp
   [DllImport("dwmapi.dll", PreserveSig = true)]
   private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
   private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
   ```
   Add `using System.Runtime.InteropServices;` and `using System.Windows.Interop;` to the file's usings.

2. Add a `List<WeakReference<Window>>` field `_trackedWindows` to track all open MainWindow instances.

3. Add a public static method `RegisterWindow(Window window)` that:
   - Adds a `WeakReference<Window>` to `_trackedWindows`
   - Calls `ApplyTitleBarToWindow(window)` immediately so the window gets the current theme on creation

4. Add a private static method `ApplyTitleBarToWindow(Window window)` that:
   - Gets the HWND via `new WindowInteropHelper(window).Handle`
   - If handle is IntPtr.Zero, return (window not yet shown)
   - Determines effective theme: if `_currentSetting == AppTheme.System` then call `DetectSystemTheme()`, else use `_currentSetting`
   - Sets `int useDarkMode = effective == AppTheme.Dark ? 1 : 0;`
   - Calls `DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int))`

5. Add a private static method `ApplyTitleBarToAllWindows()` that:
   - Iterates `_trackedWindows`, removes dead references, calls `ApplyTitleBarToWindow` on each alive window

6. At the END of the existing `ApplyTheme` method (after the ResourceDictionary swap), add a call to `ApplyTitleBarToAllWindows()`.

Do NOT change any existing method signatures. The `RegisterWindow` + `ApplyTitleBarToAllWindows` pattern matches the existing static service pattern used throughout the codebase.
  </action>
  <verify>
    <automated>dotnet build JoJot/JoJot.slnx --no-restore 2>&1 | tail -5</automated>
  </verify>
  <done>ThemeService has DWM P/Invoke, tracks windows, applies title bar dark mode on every theme change, and on initial registration.</done>
</task>

<task type="auto">
  <name>Task 2: Register MainWindow with ThemeService for title bar theming</name>
  <files>JoJot/Views/MainWindow.xaml.cs</files>
  <action>
In the MainWindow constructor, AFTER the `InitializeComponent()` call (line 160), add:

```csharp
// Register with ThemeService for title bar dark mode tracking
ThemeService.RegisterWindow(this);
```

This must come after InitializeComponent so the window handle can be obtained. However, the HWND may not exist yet at this point (it's created when the window is shown). To handle this:

Back in ThemeService.RegisterWindow, if `new WindowInteropHelper(window).Handle == IntPtr.Zero`, hook `window.SourceInitialized += (_, _) => ApplyTitleBarToWindow(window);` so the title bar is applied once the HWND becomes available. This ensures both immediate and deferred scenarios work.

Do NOT modify any other code in MainWindow.xaml.cs. Single line addition in the constructor.
  </action>
  <verify>
    <automated>dotnet build JoJot/JoJot.slnx --no-restore 2>&1 | tail -5</automated>
  </verify>
  <done>MainWindow registers itself with ThemeService. Title bar updates on creation and on every subsequent theme switch.</done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <what-built>Dark mode title bar support via DWM API. The Windows-drawn title bar (caption bar with "JoJot" text, minimize/maximize/close buttons) now follows the app theme.</what-built>
  <how-to-verify>
    1. Run the app: `dotnet run --project JoJot/JoJot.csproj`
    2. Open Preferences (hamburger menu -> Preferences)
    3. Switch to Dark theme -> title bar should turn dark (dark background, light "JoJot" text, dark window control buttons)
    4. Switch to Light theme -> title bar should return to default light Windows styling
    5. Switch to System theme -> title bar should match your Windows dark/light setting
    6. While in dark mode, verify the title bar blends with the app body (no white bar at top)
  </how-to-verify>
  <resume-signal>Type "approved" or describe any visual issues</resume-signal>
</task>

</tasks>

<verification>
- `dotnet build JoJot/JoJot.slnx` succeeds with no errors
- `dotnet test JoJot.Tests/JoJot.Tests.csproj` still passes (no breaking changes to ThemeService public API)
- Visual: dark theme shows dark title bar, light theme shows light title bar
</verification>

<success_criteria>
- Dark mode: title bar background is dark, title text is light, window controls (min/max/close) use dark styling
- Light mode: title bar uses default Windows light appearance
- System mode: follows Windows personalization setting
- Runtime switching: changing theme in Preferences immediately updates the title bar without restart
- All 302 existing tests still pass
</success_criteria>

<output>
After completion, create `.planning/quick/1-the-main-window-title-bar-window-title-a/1-SUMMARY.md`
</output>
