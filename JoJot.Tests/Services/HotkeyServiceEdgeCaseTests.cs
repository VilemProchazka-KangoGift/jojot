using JoJot.Services;
using System.Windows.Input;

namespace JoJot.Tests.Services;

/// <summary>
/// Edge case tests for HotkeyService: additional modifier combos, hex fallback, ModifierKeysToWin32 combos.
/// </summary>
public class HotkeyServiceEdgeCaseTests
{
    // Win32 modifier constants (same as in HotkeyService)
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    // ─── FormatHotkey modifier combos ──────────────────────────────

    [Fact]
    public void FormatHotkey_CtrlShift()
    {
        var result = HotkeyService.FormatHotkey(MOD_CONTROL | MOD_SHIFT, 0x41);
        result.Should().Be("Ctrl + Shift + A");
    }

    [Fact]
    public void FormatHotkey_AltShift()
    {
        var result = HotkeyService.FormatHotkey(MOD_ALT | MOD_SHIFT, 0x41);
        result.Should().Be("Alt + Shift + A");
    }

    [Fact]
    public void FormatHotkey_WinCtrl()
    {
        var result = HotkeyService.FormatHotkey(MOD_WIN | MOD_CONTROL, 0x41);
        result.Should().Be("Win + Ctrl + A");
    }

    [Fact]
    public void FormatHotkey_WinAlt()
    {
        var result = HotkeyService.FormatHotkey(MOD_WIN | MOD_ALT, 0x41);
        result.Should().Be("Win + Alt + A");
    }

    [Fact]
    public void FormatHotkey_WinCtrlShift()
    {
        var result = HotkeyService.FormatHotkey(MOD_WIN | MOD_CONTROL | MOD_SHIFT, 0x70);
        result.Should().Be("Win + Ctrl + Shift + F1");
    }

    [Fact]
    public void FormatHotkey_CtrlAltShift()
    {
        var result = HotkeyService.FormatHotkey(MOD_CONTROL | MOD_ALT | MOD_SHIFT, 0x70);
        result.Should().Be("Ctrl + Alt + Shift + F1");
    }

    [Fact]
    public void FormatHotkey_ShiftOnly()
    {
        var result = HotkeyService.FormatHotkey(MOD_SHIFT, 0x41);
        result.Should().Be("Shift + A");
    }

    // ─── FormatHotkey unusual VK codes ─────────────────────────────

    [Fact]
    public void FormatHotkey_VK_Zero_ResolvesOrHex()
    {
        // VK 0x00 is "None" — KeyInterop may handle it
        var result = HotkeyService.FormatHotkey(0, 0x00);
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FormatHotkey_VK_Space()
    {
        var result = HotkeyService.FormatHotkey(MOD_CONTROL, 0x20);
        result.Should().Be("Ctrl + Space");
    }

    [Fact]
    public void FormatHotkey_VK_Escape()
    {
        var result = HotkeyService.FormatHotkey(0, 0x1B);
        result.Should().Be("Escape");
    }

    [Fact]
    public void FormatHotkey_VK_Enter()
    {
        var result = HotkeyService.FormatHotkey(MOD_CONTROL, 0x0D);
        result.Should().Be("Ctrl + Return");
    }

    // ─── ModifierKeysToWin32 combos ────────────────────────────────

    [Fact]
    public void ModifierKeysToWin32_CtrlAlt()
    {
        HotkeyService.ModifierKeysToWin32(ModifierKeys.Control | ModifierKeys.Alt)
            .Should().Be(MOD_CONTROL | MOD_ALT);
    }

    [Fact]
    public void ModifierKeysToWin32_AllFour()
    {
        HotkeyService.ModifierKeysToWin32(
            ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)
            .Should().Be(MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_WIN);
    }

    [Fact]
    public void ModifierKeysToWin32_WinShift()
    {
        HotkeyService.ModifierKeysToWin32(ModifierKeys.Windows | ModifierKeys.Shift)
            .Should().Be(MOD_WIN | MOD_SHIFT);
    }

    [Fact]
    public void ModifierKeysToWin32_WinAlt()
    {
        HotkeyService.ModifierKeysToWin32(ModifierKeys.Windows | ModifierKeys.Alt)
            .Should().Be(MOD_WIN | MOD_ALT);
    }
}
