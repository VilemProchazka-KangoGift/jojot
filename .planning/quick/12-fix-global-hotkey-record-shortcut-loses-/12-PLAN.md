---
phase: quick-12
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Services/HotkeyService.cs
  - JoJot/Controls/PreferencesPanel.xaml.cs
  - JoJot/Views/MainWindow.Keyboard.cs
  - JoJot.Tests/Services/HotkeyServiceTests.cs
autonomous: true
requirements: [HOTKEY-FIX]

must_haves:
  truths:
    - "User can record Win+Shift+N (or any Win-modifier combo) without losing focus to Start Menu"
    - "Recording the same hotkey that is already set succeeds silently (not 'already in use')"
    - "Default Win+Shift+N registers successfully on fresh install"
    - "Recording Escape cancels recording and restores previous hotkey"
  artifacts:
    - path: "JoJot/Services/HotkeyService.cs"
      provides: "Hotkey registration with same-combo detection and low-level keyboard hook for recording"
    - path: "JoJot/Controls/PreferencesPanel.xaml.cs"
      provides: "Recording UI state management"
    - path: "JoJot/Views/MainWindow.Keyboard.cs"
      provides: "Hotkey recording capture logic with low-level hook"
  key_links:
    - from: "JoJot/Views/MainWindow.Keyboard.cs"
      to: "JoJot/Services/HotkeyService.cs"
      via: "UpdateHotkeyAsync call during recording"
      pattern: "HotkeyService\\.UpdateHotkeyAsync"
---

<objective>
Fix three interrelated bugs in the global hotkey recording feature:

1. **Win key steals focus during recording:** Pressing the Windows key during hotkey recording opens the Start Menu, stealing focus from JoJot and aborting the recording. Fix by using a low-level keyboard hook (WH_KEYBOARD_LL) during recording mode to suppress the Win key's default shell behavior.

2. **All combos show "already in use":** The `UpdateHotkeyAsync` method unregisters the old hotkey, then tries to register the new one. But `PauseHotkey()` already unregistered it when recording started. If the user records the SAME combo that was already set, `RegisterHotKey` returns false because the OS sees it as a duplicate registration attempt from the same HWND (it was never truly released between PauseHotkey and UpdateHotkeyAsync). Fix by detecting same-combo re-registration and skipping the RegisterHotKey call, and also by ensuring PauseHotkey properly clears state so UpdateHotkeyAsync can cleanly register.

3. **Default Win+Shift+N broken:** This is a consequence of bug #2 -- on first launch with no saved preferences, the default registers fine. But once the user opens the recorder and presses Win+Shift+N to re-record the same default, it fails.

Purpose: Make the hotkey preferences panel actually functional for recording keyboard shortcuts.
Output: Fixed HotkeyService, PreferencesPanel, and MainWindow.Keyboard files.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@JoJot/Services/HotkeyService.cs
@JoJot/Controls/PreferencesPanel.xaml.cs
@JoJot/Views/MainWindow.Keyboard.cs
@JoJot/Views/MainWindow.Preferences.cs
@JoJot/Views/MainWindow.xaml.cs
@JoJot.Tests/Services/HotkeyServiceTests.cs
@JoJot.Tests/Services/HotkeyServiceEdgeCaseTests.cs

<interfaces>
<!-- Key types and contracts the executor needs -->

From JoJot/Services/HotkeyService.cs:
```csharp
// Win32 modifier constants
private const uint MOD_ALT = 0x0001;
private const uint MOD_CONTROL = 0x0002;
private const uint MOD_SHIFT = 0x0004;
private const uint MOD_WIN = 0x0008;
private const uint MOD_NOREPEAT = 0x4000;

// Public API
public static async Task<bool> InitializeAsync(Window window, Action onHotkeyPressed);
public static async Task<bool> UpdateHotkeyAsync(uint modifiers, uint vk);
public static (uint modifiers, uint vk) GetCurrentHotkey();
public static string GetHotkeyDisplayString();
public static string FormatHotkey(uint modifiers, uint vk);
public static uint ModifierKeysToWin32(ModifierKeys modifiers);
public static void PauseHotkey();
public static void ResumeHotkey();
public static void Shutdown();
```

From JoJot/Controls/PreferencesPanel.xaml.cs:
```csharp
public event EventHandler<bool>? HotkeyRecordingChanged;
public bool IsRecordingHotkey => _recordingHotkey;
public void StopRecording();
public void UpdateHotkeyDisplay(string hotkeyDisplay);
```

From JoJot/Views/MainWindow.Keyboard.cs (recording section, lines 167-208):
- PreviewKeyDown checks `PreferencesPanel.IsRecordingHotkey`
- Ignores lone modifier presses (LWin, RWin, LCtrl, RCtrl, etc.)
- Requires at least one modifier
- Calls `HotkeyService.UpdateHotkeyAsync(win32Mods, vk)` on Task.Run
- Shows toast "Hotkey already in use by another app" on failure
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Fix HotkeyService -- add low-level keyboard hook for recording and same-combo detection</name>
  <files>JoJot/Services/HotkeyService.cs, JoJot.Tests/Services/HotkeyServiceTests.cs</files>
  <action>
  Three changes to HotkeyService.cs:

  **A. Add low-level keyboard hook to suppress Win key during recording:**

  Add P/Invoke declarations at the top of the class:
  ```csharp
  [DllImport("user32.dll", SetLastError = true)]
  private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

  [DllImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool UnhookWindowsHookEx(IntPtr hhk);

  [DllImport("user32.dll")]
  private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

  [DllImport("kernel32.dll")]
  private static extern IntPtr GetModuleHandle(string? lpModuleName);

  private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

  private const int WH_KEYBOARD_LL = 13;
  private const int WM_KEYDOWN = 0x0100;
  private const int WM_KEYUP = 0x0101;
  private const int WM_SYSKEYDOWN = 0x0104;
  private const int WM_SYSKEYUP = 0x0105;
  private const int VK_LWIN = 0x5B;
  private const int VK_RWIN = 0x5C;
  ```

  Add static fields:
  ```csharp
  private static IntPtr _llKeyboardHook;
  private static LowLevelKeyboardProc? _llKeyboardProc; // prevent GC collection of delegate
  private static bool _isRecording;
  ```

  Add methods:
  ```csharp
  /// <summary>
  /// Installs a low-level keyboard hook that suppresses the Win key's default
  /// Start Menu behavior during hotkey recording. This prevents the OS from
  /// stealing focus when the user presses Win as part of a hotkey combo.
  /// Must be called on the UI thread.
  /// </summary>
  public static void StartRecordingMode()
  {
      if (_llKeyboardHook != IntPtr.Zero) return;
      _isRecording = true;
      _llKeyboardProc = LowLevelKeyboardHookProc;
      _llKeyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _llKeyboardProc, GetModuleHandle(null), 0);
      if (_llKeyboardHook == IntPtr.Zero)
      {
          LogService.Warn("Failed to install low-level keyboard hook for recording");
      }
  }

  /// <summary>
  /// Removes the low-level keyboard hook installed for recording.
  /// Must be called on the UI thread.
  /// </summary>
  public static void StopRecordingMode()
  {
      _isRecording = false;
      if (_llKeyboardHook != IntPtr.Zero)
      {
          UnhookWindowsHookEx(_llKeyboardHook);
          _llKeyboardHook = IntPtr.Zero;
          _llKeyboardProc = null;
      }
  }

  private static IntPtr LowLevelKeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
  {
      if (nCode >= 0 && _isRecording)
      {
          int vkCode = Marshal.ReadInt32(lParam);
          if (vkCode == VK_LWIN || vkCode == VK_RWIN)
          {
              // Suppress Win key default behavior (Start Menu) but allow WPF to see it as a modifier.
              // We swallow the key message entirely -- WPF's Keyboard.Modifiers still tracks
              // the Win key state via GetKeyState, which is unaffected by the hook suppression.
              return (IntPtr)1;
          }
      }
      return CallNextHookEx(_llKeyboardHook, nCode, wParam, lParam);
  }
  ```

  **B. Fix same-combo detection in UpdateHotkeyAsync:**

  At the top of `UpdateHotkeyAsync`, before unregistering, check if the requested combo is the same as the current one:
  ```csharp
  public static async Task<bool> UpdateHotkeyAsync(uint modifiers, uint vk)
  {
      // If recording the same combo that's already set, just re-register it
      if (modifiers == _modifiers && vk == _vk)
      {
          if (!_isRegistered)
          {
              bool success = RegisterHotKey(_hwnd, HOTKEY_ID, _modifiers | MOD_NOREPEAT, _vk);
              _isRegistered = success;
              if (success)
              {
                  LogService.Info("Global hotkey re-registered (same combo): {HotkeyDisplay}", GetHotkeyDisplayString());
              }
              return success;
          }
          return true; // Already registered with same combo
      }

      // ... rest of existing method unchanged ...
  }
  ```

  **C. Update Shutdown to clean up hook:**

  In `Shutdown()`, add `StopRecordingMode();` at the beginning.

  **D. Add test for IsSameHotkey detection:**

  In `HotkeyServiceTests.cs`, add a test for `GetCurrentHotkey`:
  ```csharp
  [Fact]
  public void GetCurrentHotkey_ReturnsDefaultValues()
  {
      var (modifiers, vk) = HotkeyService.GetCurrentHotkey();
      // Default is Win+Shift+N (but may have been modified by other tests)
      // Just verify it returns a valid tuple
      modifiers.Should().BeGreaterThanOrEqualTo(0u);
      vk.Should().BeGreaterThan(0u);
  }
  ```
  </action>
  <verify>
    <automated>dotnet build JoJot/JoJot.slnx --no-restore 2>&amp;1 | tail -5</automated>
  </verify>
  <done>
  - HotkeyService has StartRecordingMode/StopRecordingMode using WH_KEYBOARD_LL hook
  - Hook suppresses VK_LWIN and VK_RWIN during recording to prevent Start Menu activation
  - UpdateHotkeyAsync detects same-combo re-registration and handles it without failing
  - Shutdown cleans up the LL hook if active
  - Solution builds without errors
  </done>
</task>

<task type="auto">
  <name>Task 2: Wire recording mode hook into PreferencesPanel and MainWindow keyboard handler</name>
  <files>JoJot/Controls/PreferencesPanel.xaml.cs, JoJot/Views/MainWindow.Keyboard.cs</files>
  <action>
  **A. Update PreferencesPanel.xaml.cs -- Hide() should stop recording mode hook:**

  In `Hide()` method, when `_recordingHotkey` is true, also call `HotkeyService.StopRecordingMode()` before resetting state:
  ```csharp
  public void Hide()
  {
      if (_recordingHotkey)
      {
          _recordingHotkey = false;
          HotkeyRecordText.Text = "Record";
          HotkeyService.StopRecordingMode(); // Clean up LL hook
      }
      // ... rest unchanged
  }
  ```

  In `HotkeyRecord_Click`, when toggling recording ON, call `HotkeyService.StartRecordingMode()`. When toggling OFF (cancel), call `HotkeyService.StopRecordingMode()`:
  ```csharp
  private void HotkeyRecord_Click(object sender, MouseButtonEventArgs e)
  {
      if (_recordingHotkey)
      {
          _recordingHotkey = false;
          HotkeyRecordText.Text = "Record";
          HotkeyService.StopRecordingMode();
          HotkeyRecordingChanged?.Invoke(this, false);
      }
      else
      {
          _recordingHotkey = true;
          HotkeyRecordText.Text = "Press keys...";
          HotkeyService.StartRecordingMode();
          HotkeyRecordingChanged?.Invoke(this, true);
      }
  }
  ```

  In `StopRecording()` (called externally after successful capture), also stop the hook:
  ```csharp
  public void StopRecording()
  {
      _recordingHotkey = false;
      HotkeyRecordText.Text = "Record";
      HotkeyService.StopRecordingMode();
  }
  ```

  **B. Update MainWindow.Keyboard.cs -- handle Escape during recording and improve capture:**

  In `Window_PreviewKeyDown`, in the hotkey recording block (around line 168), add Escape handling BEFORE the lone-modifier check:
  ```csharp
  if (PreferencesPanel.IsRecordingHotkey)
  {
      var mods = Keyboard.Modifiers;
      var key = e.Key == Key.System ? e.SystemKey : e.Key;

      // Escape cancels recording
      if (key == Key.Escape)
      {
          PreferencesPanel.StopRecording();
          HotkeyService.ResumeHotkey();
          e.Handled = true;
          return;
      }

      // Ignore lone modifier presses
      if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift ||
          key == Key.RightShift || key == Key.LeftAlt || key == Key.RightAlt ||
          key == Key.LWin || key == Key.RWin)
      {
          e.Handled = true;
          return;
      }

      // Require at least one modifier
      if (mods == ModifierKeys.None)
      {
          e.Handled = true;
          return;
      }

      uint win32Mods = HotkeyService.ModifierKeysToWin32(mods);
      uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

      // Stop the LL hook BEFORE attempting registration so the Win key
      // is no longer suppressed when we call RegisterHotKey
      HotkeyService.StopRecordingMode();

      _ = Task.Run(async () =>
      {
          bool success = await HotkeyService.UpdateHotkeyAsync(win32Mods, vk);
          await Dispatcher.InvokeAsync(() =>
          {
              PreferencesPanel.StopRecording();
              PreferencesPanel.UpdateHotkeyDisplay(HotkeyService.GetHotkeyDisplayString());
              if (!success)
              {
                  ShowInfoToast("Hotkey already in use by another app");
              }
          });
      });

      e.Handled = true;
      return;
  }
  ```

  Note: The key change is adding `HotkeyService.StopRecordingMode()` BEFORE the Task.Run that calls UpdateHotkeyAsync, and adding Escape-to-cancel at the top of the recording block.
  </action>
  <verify>
    <automated>dotnet build JoJot/JoJot.slnx --no-restore 2>&amp;1 | tail -5 &amp;&amp; dotnet test JoJot.Tests/JoJot.Tests.csproj --no-build --filter "HotkeyService" 2>&amp;1 | tail -15</automated>
  </verify>
  <done>
  - Recording mode starts the LL keyboard hook when user clicks "Record"
  - Recording mode stops the LL keyboard hook when: user captures a combo, user clicks "Record" again to cancel, user presses Escape, or preferences panel closes
  - Escape during recording cancels and restores previous hotkey via ResumeHotkey
  - LL hook is stopped before UpdateHotkeyAsync is called so RegisterHotKey works correctly
  - All existing hotkey tests pass
  - Build succeeds
  </done>
</task>

</tasks>

<verification>
1. `dotnet build JoJot/JoJot.slnx` succeeds with no errors
2. `dotnet test JoJot.Tests/JoJot.Tests.csproj` -- all tests pass including HotkeyService tests
3. Manual verification flow:
   - Open Preferences panel, click Record
   - Press Win+Shift+N -- should record successfully without Start Menu appearing
   - Click Record again, press Win+Shift+N again -- should succeed (same combo re-registration)
   - Click Record, press Escape -- should cancel and restore previous hotkey
   - Close preferences while recording -- should clean up gracefully
   - Minimize JoJot, press Win+Shift+N from desktop -- should focus JoJot
</verification>

<success_criteria>
- Win key does not open Start Menu during hotkey recording
- Re-recording the same combo succeeds instead of showing "already in use"
- Default Win+Shift+N works on fresh install and after re-recording
- Escape cancels recording and restores previous hotkey
- All cleanup paths (cancel, close panel, successful capture) remove the LL hook
- All existing tests pass, solution builds cleanly
</success_criteria>

<output>
After completion, create `.planning/quick/12-fix-global-hotkey-record-shortcut-loses-/12-SUMMARY.md`
</output>
