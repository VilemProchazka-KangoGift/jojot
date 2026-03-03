# Phase 9: File Drop, Preferences, Hotkeys & Keyboard - Research

**Researched:** 2026-03-03
**Domain:** WPF file drag-and-drop, Win32 global hotkeys, preferences UI, keyboard shortcuts
**Confidence:** HIGH

## Summary

Phase 9 adds four feature clusters to JoJot: file drag-and-drop with content inspection, a slide-in preferences panel, a global hotkey via Win32 RegisterHotKey, and additional keyboard shortcuts (font size, Ctrl+F context routing, help overlay). All features build on existing infrastructure: the preferences table (DATA-06), ThemeService, AutosaveService.DebounceMs, Window_PreviewKeyDown, and the toast notification pattern.

The primary technical risks are: (1) RegisterHotKey requires Win32 P/Invoke with HwndSource message hooking on the STA thread, (2) file content inspection must detect binary vs text without false positives, and (3) the Ctrl+F shortcut must route context-dependently (editor find bar vs tab search). All are well-understood WPF patterns with HIGH confidence.

**Primary recommendation:** Implement in three waves: Wave 1 (file drop + preferences panel), Wave 2 (global hotkey + font size shortcuts + Ctrl+F routing), Wave 3 (keyboard shortcuts reference overlay + final wiring).

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- File drop visual feedback: full overlay covering content area during drag-over with "Drop file here" message and icon
- File drop error handling: errors via existing toast notification bar; combined toast for multi-file drops
- File drop tab behavior: last valid file becomes active tab for multi-drop; single file becomes active immediately
- Preferences dialog style: right-side slide-in panel within main window (not modal dialog)
- Preferences layout: Appearance (theme, font size), Editor (debounce), Shortcuts (hotkey picker) sections
- Hotkey picker: record mode with "Record" button
- Global hotkey behavior: toggle focus/minimize; activates window for current virtual desktop
- Global hotkey conflict handling: toast notification on startup if hotkey already registered
- Ctrl+F context-dependent: editor focused = in-editor find bar; tab list focused = tab search box
- Font size shortcuts: Ctrl+=/-, Ctrl+0, Ctrl+Scroll; persistent; brief tooltip showing size
- Keyboard shortcuts reference: Ctrl+? help overlay as reference card

### Claude's Discretion
- Slide-in panel animation timing and easing
- Drop overlay visual design (icon choice, opacity, animation)
- In-editor find bar design and behavior details
- Help overlay layout and styling
- Font size tooltip positioning and animation
- Exact section spacing and typography in preferences panel

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| DROP-01 | Dragging a file onto JoJot window opens it as a new tab | WPF AllowDrop + DragEnter/DragLeave/Drop event handlers |
| DROP-02 | Acceptance by content inspection (valid UTF-8/UTF-16, no null bytes) | StreamReader with encoding detection + binary check pattern |
| DROP-03 | Size limit 500KB checked before content inspection | FileInfo.Length check before reading |
| DROP-04 | Tab name set to filename including extension; content loaded; original file unmodified | Path.GetFileName + File.ReadAllText |
| DROP-05 | Drop visual feedback: highlight border while dragging over window | Full overlay on DragEnter, hide on DragLeave/Drop |
| DROP-06 | Error messages (inline alert, auto-dismiss 4s): too large, binary, read error | Reuse existing toast pattern |
| DROP-07 | Multiple files dropped simultaneously: each valid file gets own tab | DataFormats.FileDrop returns string[] of paths |
| PREF-01 | Preferences dialog opened via menu; all changes apply live, no restart | Slide-in panel; ThemeService.SetThemeAsync already wired |
| PREF-02 | Theme toggle: Light / System / Dark | Three-button toggle calling ThemeService.SetThemeAsync |
| PREF-03 | Font size control: +/- buttons, 8-32pt range, 1pt step, reset link to 13pt | ContentEditor.FontSize binding + DatabaseService.SetPreferenceAsync |
| PREF-04 | Autosave debounce interval: numeric input, 200-2000ms range, default 500 | AutosaveService.DebounceMs property already exists |
| PREF-05 | Global hotkey picker: key combination, default Win+Shift+N | Record mode UI + RegisterHotKey |
| KEYS-01 | Global hotkey (Win+Shift+N default) via RegisterHotKey: focus/minimize | Win32 RegisterHotKey/UnregisterHotKey P/Invoke |
| KEYS-02 | Font size: Ctrl+= increase, Ctrl+- decrease, Ctrl+0 reset to 13pt | Window_PreviewKeyDown additions |
| KEYS-03 | Ctrl+Scroll over editor changes font size; over tab list scrolls normally | PreviewMouseWheel handler with hit-test |
| KEYS-04 | All keyboard shortcuts per spec | Audit existing + add missing |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WPF (net10.0-windows) | .NET 10 | UI framework | Already in use; all features implemented via WPF APIs |
| Win32 P/Invoke | N/A | RegisterHotKey/UnregisterHotKey | Only way to register system-wide hotkeys; no WPF built-in |
| Microsoft.Data.Sqlite | 10.0.3 | Preferences persistence | Already in project for all data storage |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Text.Encoding | .NET built-in | UTF-8/UTF-16 detection | File drop content inspection |
| System.IO | .NET built-in | File reading | File drop file access |
| System.Windows.Interop (HwndSource) | WPF built-in | Win32 message hook | Receiving WM_HOTKEY messages |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Raw P/Invoke for hotkeys | NHotkey NuGet | NHotkey adds a dependency for a simple two-method P/Invoke; raw is simpler |
| Custom slide-in panel | ContentDialog / Popup | Popup lacks layout control; custom panel gives exact design control |

## Architecture Patterns

### Recommended Project Structure
```
JoJot/
├── Services/
│   ├── HotkeyService.cs          # NEW: RegisterHotKey P/Invoke, WM_HOTKEY handling
│   └── FileDropService.cs        # NEW: Content inspection, encoding detection, size check
├── MainWindow.xaml                # Additions: drop overlay, preferences panel, find bar, help overlay
├── MainWindow.xaml.cs             # Additions: drop handlers, pref panel logic, new shortcuts
└── App.xaml.cs                    # Addition: HotkeyService.Initialize on startup, Shutdown on exit
```

### Pattern 1: Win32 Global Hotkey via HwndSource
**What:** Register a system-wide hotkey using RegisterHotKey P/Invoke, hook WM_HOTKEY via HwndSource.AddHook
**When to use:** Any time you need a hotkey that works when the app is not focused
**Example:**
```csharp
// In HotkeyService.cs
[DllImport("user32.dll", SetLastError = true)]
private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

[DllImport("user32.dll", SetLastError = true)]
private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

private const int WM_HOTKEY = 0x0312;
private const int HOTKEY_ID = 0x9001;

// MOD_WIN = 0x0008, MOD_SHIFT = 0x0004, MOD_NOREPEAT = 0x4000
// VK_N = 0x4E

public static bool Register(IntPtr hwnd, uint modifiers, uint vk)
{
    return RegisterHotKey(hwnd, HOTKEY_ID, modifiers | 0x4000, vk);
}

// Hook via HwndSource in window's SourceInitialized event:
var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
source.AddHook(WndProc);

private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
{
    if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
    {
        // Toggle window focus/minimize
        handled = true;
    }
    return IntPtr.Zero;
}
```

### Pattern 2: WPF File Drag-and-Drop
**What:** AllowDrop="True" on the window/content area, handle DragEnter/DragLeave/Drop events
**When to use:** File drop acceptance
**Example:**
```csharp
// In MainWindow.xaml: AllowDrop="True" on the content area Grid
// DragEnter="OnDragEnter" DragLeave="OnDragLeave" Drop="OnDrop"

private void OnDragEnter(object sender, DragEventArgs e)
{
    if (e.Data.GetDataPresent(DataFormats.FileDrop))
    {
        e.Effects = DragDropEffects.Copy;
        ShowDropOverlay();
    }
    else
    {
        e.Effects = DragDropEffects.None;
    }
    e.Handled = true;
}

private void OnDrop(object sender, DragEventArgs e)
{
    HideDropOverlay();
    if (e.Data.GetDataPresent(DataFormats.FileDrop))
    {
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        _ = ProcessDroppedFilesAsync(files);
    }
}
```

### Pattern 3: Slide-In Panel Animation
**What:** A Grid column that slides in from the right edge using ThicknessAnimation or TranslateTransform
**When to use:** Preferences panel reveal/hide
**Example:**
```csharp
// XAML: Panel as a column with Width="300" and RenderTransform TranslateTransform
// Initially translated off-screen (X=300), animate to X=0 on show

var transform = new TranslateTransform(300, 0);
PreferencesPanel.RenderTransform = transform;
var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250))
{
    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
};
transform.BeginAnimation(TranslateTransform.XProperty, animation);
```

### Pattern 4: Binary File Detection
**What:** Read file bytes and check for null bytes or non-printable characters
**When to use:** DROP-02 content inspection
**Example:**
```csharp
public static bool IsBinaryContent(byte[] buffer, int bytesRead)
{
    for (int i = 0; i < bytesRead; i++)
    {
        byte b = buffer[i];
        // Null byte is definitive binary indicator
        if (b == 0) return true;
        // Non-printable characters (excluding common whitespace: tab, newline, carriage return)
        if (b < 0x08 || (b > 0x0D && b < 0x20 && b != 0x1B)) return true;
    }
    return false;
}
```

### Anti-Patterns to Avoid
- **Checking file extension instead of content:** DROP-02 explicitly requires content inspection, not extension filtering
- **Using DragDrop.DoDragDrop for receiving drops:** That's for initiating drags; file drops from Explorer use the DragEnter/DragLeave/Drop event pattern
- **Registering hotkey before window has HWND:** RegisterHotKey needs a valid window handle; register in SourceInitialized or after Show()
- **Blocking UI thread during file reading:** Use async file I/O for large files to prevent UI freeze

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Global hotkey registration | Custom message pump | Win32 RegisterHotKey + HwndSource.AddHook | Standard Win32 pattern, handles focus correctly |
| Theme switching | Manual color updates | ThemeService.SetThemeAsync (already exists) | Already handles ResourceDictionary swap + persistence |
| Preferences persistence | Custom file-based config | DatabaseService.Get/SetPreferenceAsync (already exists) | Preferences table already in schema |
| UTF-8 detection | Manual BOM checking | StreamReader with detectEncodingFromByteOrderMarks | .NET handles BOM detection and fallback correctly |

**Key insight:** Most of Phase 9's infrastructure already exists in the codebase. The preferences table, theme service, autosave debounce property, and keyboard shortcut pattern are all ready. The new code is primarily UI panels and wiring.

## Common Pitfalls

### Pitfall 1: RegisterHotKey Thread Affinity
**What goes wrong:** RegisterHotKey fails silently or WM_HOTKEY never arrives
**Why it happens:** RegisterHotKey must be called on the thread that owns the window handle, and the message hook must be on the same HwndSource
**How to avoid:** Always call RegisterHotKey on the UI thread; hook HwndSource from SourceInitialized event
**Warning signs:** RegisterHotKey returns false; check Marshal.GetLastWin32Error() for error code 1409 (ERROR_HOTKEY_ALREADY_REGISTERED)

### Pitfall 2: SetForegroundWindow Focus Stealing Prevention
**What goes wrong:** Calling SetForegroundWindow from a background context doesn't actually bring the window to front
**Why it happens:** Windows prevents focus stealing -- only the foreground process can call SetForegroundWindow
**How to avoid:** The WM_HOTKEY handler runs in the context of a user keypress, which grants foreground permission. Use WindowActivationHelper.ActivateWindow (already handles this correctly with P/Invoke)
**Warning signs:** Window flashes in taskbar but doesn't come to front

### Pitfall 3: DragLeave Fires When Entering Child Elements
**What goes wrong:** Drop overlay flickers when mouse moves over child elements within the drop zone
**Why it happens:** WPF fires DragLeave on the parent when entering a child element, then DragEnter on the child
**How to avoid:** Use a flag + delayed hide, or handle AllowDrop on the overlay itself (overlay covers everything, so DragLeave on overlay = truly left)
**Warning signs:** Overlay flashes on/off rapidly during drag

### Pitfall 4: Ctrl+= Key Detection in WPF
**What goes wrong:** Key.OemPlus or Key.Add doesn't fire for Ctrl+=
**Why it happens:** WPF maps the =/+ key differently based on keyboard layout; the key is Key.OemPlus on US keyboards
**How to avoid:** Check both Key.OemPlus (for = key) and Key.Add (for numpad +); some layouts may need additional checks
**Warning signs:** Shortcut works on some keyboards but not others

### Pitfall 5: Ctrl+Scroll Conflicts with Tab List Scroll
**What goes wrong:** Ctrl+Scroll changes font size even when scrolling the tab list
**Why it happens:** PreviewMouseWheel tunnels from window to all children
**How to avoid:** Hit-test the mouse position in the PreviewMouseWheel handler; only change font size if the mouse is over the editor area (not the tab list panel)
**Warning signs:** Users can't scroll the tab list while holding Ctrl

### Pitfall 6: HwndSource Leak
**What goes wrong:** Memory leak or crash on window close
**Why it happens:** HwndSource.AddHook adds a reference that isn't cleaned up
**How to avoid:** Call UnregisterHotKey and HwndSource.RemoveHook in the window's Closing event; also call in App.OnExit
**Warning signs:** Memory growth on repeated window open/close

## Code Examples

### File Drop Processing with Content Inspection
```csharp
private async Task ProcessDroppedFilesAsync(string[] filePaths)
{
    int validCount = 0;
    int errorCount = 0;
    string? lastError = null;
    NoteTab? lastValidTab = null;

    foreach (var path in filePaths)
    {
        try
        {
            var fileInfo = new FileInfo(path);

            // DROP-03: Size limit 500KB
            if (fileInfo.Length > 500 * 1024)
            {
                errorCount++;
                lastError = $"'{fileInfo.Name}' is too large (max 500KB)";
                continue;
            }

            // DROP-02: Content inspection — read first 8KB for binary check
            byte[] buffer = new byte[Math.Min(8192, (int)fileInfo.Length)];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length);
                if (IsBinaryContent(buffer, bytesRead))
                {
                    errorCount++;
                    lastError = $"'{fileInfo.Name}' contains binary content";
                    continue;
                }
            }

            // Read full content as text
            string content = await File.ReadAllTextAsync(path);

            // DROP-04: Create tab with filename as label
            var tab = await CreateTabFromFileAsync(fileInfo.Name, content);
            validCount++;
            lastValidTab = tab;
        }
        catch (Exception ex)
        {
            errorCount++;
            lastError = $"Failed to read '{Path.GetFileName(path)}'";
            LogService.Error($"File drop error: {path}", ex);
        }
    }

    // DROP-07: Focus last valid tab
    if (lastValidTab != null) SelectTab(lastValidTab);

    // DROP-06: Show toast for errors
    if (errorCount > 0)
    {
        string message = validCount > 0
            ? $"{validCount} file(s) opened, {errorCount} skipped"
            : lastError ?? $"{errorCount} file(s) skipped";
        ShowErrorToast(message);
    }
}
```

### Global Hotkey Service Structure
```csharp
public static class HotkeyService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x9001;

    // MOD_WIN=0x0008, MOD_SHIFT=0x0004, MOD_NOREPEAT=0x4000
    private static uint _modifiers = 0x0008 | 0x0004 | 0x4000; // Win+Shift+NoRepeat
    private static uint _vk = 0x4E; // VK_N

    private static IntPtr _hwnd;
    private static HwndSource? _source;
    private static Action? _onHotkeyPressed;

    public static bool Initialize(Window window, Action onHotkeyPressed)
    {
        _onHotkeyPressed = onHotkeyPressed;
        _hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        // Load saved hotkey from preferences
        // ... LoadSavedHotkey();

        bool success = RegisterHotKey(_hwnd, HOTKEY_ID, _modifiers, _vk);
        if (!success)
        {
            LogService.Warn($"Global hotkey registration failed (error {Marshal.GetLastWin32Error()})");
        }
        return success;
    }

    public static void Shutdown()
    {
        if (_hwnd != IntPtr.Zero)
            UnregisterHotKey(_hwnd, HOTKEY_ID);
        _source?.RemoveHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            _onHotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }
}
```

### Font Size Shortcut Handling
```csharp
// In Window_PreviewKeyDown:

// KEYS-02: Ctrl+= increase font size
if (e.Key == Key.OemPlus && Keyboard.Modifiers == ModifierKeys.Control)
{
    ChangeFontSize(1);
    e.Handled = true;
    return;
}

// KEYS-02: Ctrl+- decrease font size
if (e.Key == Key.OemMinus && Keyboard.Modifiers == ModifierKeys.Control)
{
    ChangeFontSize(-1);
    e.Handled = true;
    return;
}

// KEYS-02: Ctrl+0 reset font size to 13pt
if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.Control)
{
    SetFontSize(13);
    e.Handled = true;
    return;
}

private void ChangeFontSize(int delta)
{
    int newSize = Math.Clamp((int)ContentEditor.FontSize + delta, 8, 32);
    SetFontSize(newSize);
}

private async void SetFontSize(int size)
{
    ContentEditor.FontSize = size;
    await DatabaseService.SetPreferenceAsync("font_size", size.ToString());
    ShowFontSizeTooltip(size);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Global hooks via SetWindowsHookEx | RegisterHotKey for hotkeys | Always been standard | RegisterHotKey is simpler and doesn't require a hook DLL |
| Extension-based file type detection | Content inspection (magic bytes / encoding check) | Modern practice | More reliable for text detection |
| Modal settings dialogs | In-context slide panels | Windows 11 design trend | Better UX, keeps user in context |

**Deprecated/outdated:**
- SetWindowsHookEx for global hotkeys: overkill for single-key registration; RegisterHotKey is the correct API

## Open Questions

1. **Ctrl+? key detection**
   - What we know: ? requires Shift on US keyboards (Shift+/), so Ctrl+? is actually Ctrl+Shift+OemQuestion
   - What's unclear: Whether all keyboard layouts map ? to the same key
   - Recommendation: Use Key.OemQuestion with Ctrl+Shift modifiers; test with US layout, document limitation

2. **HotkeyService ownership with multiple windows**
   - What we know: RegisterHotKey is per-HWND; we have multiple windows (one per desktop)
   - What's unclear: Which window should own the hotkey registration
   - Recommendation: Register on the FIRST window created (in App.xaml.cs after CreateWindowForDesktop). The WM_HOTKEY callback should route through App to find/create the correct desktop window. If the owning window closes, re-register on another open window.

3. **In-editor find bar scope**
   - What we know: CONTEXT.md specifies Ctrl+F with editor focus opens in-editor find bar
   - What's unclear: Full find bar behavior (find next/previous, wrap, case sensitivity)
   - Recommendation: Minimal find bar: text input + next/previous buttons + close (Escape). Case-insensitive search. Wrap at document boundary. Highlight matches with yellow background.

## Sources

### Primary (HIGH confidence)
- WPF DragDrop documentation: DragEnter/DragLeave/Drop event pattern, DataFormats.FileDrop
- Win32 RegisterHotKey: MSDN documentation for RegisterHotKey/UnregisterHotKey API
- .NET HwndSource: System.Windows.Interop.HwndSource for Win32 message hooking in WPF
- Existing codebase: ThemeService, DatabaseService, AutosaveService patterns

### Secondary (MEDIUM confidence)
- MOD_NOREPEAT flag: Prevents auto-repeat WM_HOTKEY when key is held (Windows 7+)
- DragLeave child element issue: Well-known WPF quirk documented in community

### Tertiary (LOW confidence)
- None -- all findings verified against official APIs or existing codebase patterns

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - all WPF/.NET built-in, no new dependencies
- Architecture: HIGH - follows existing project patterns (static services, code-behind)
- Pitfalls: HIGH - well-documented Win32/WPF gotchas with known solutions

**Research date:** 2026-03-03
**Valid until:** 2026-04-03 (stable platform, no moving targets)
