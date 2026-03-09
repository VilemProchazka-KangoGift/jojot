using JoJot.Services;
using System.Windows.Input;

namespace JoJot.Tests.Services;

public class HotkeyServiceTests
{
    // ─── FormatHotkey ──────────────────────────────────────────────────

    [Fact]
    public void FormatHotkey_WinShiftN()
    {
        // Win=0x0008, Shift=0x0004, N=0x4E
        var result = HotkeyService.FormatHotkey(0x0008 | 0x0004, 0x4E);
        result.Should().Be("Win + Shift + N");
    }

    [Fact]
    public void FormatHotkey_CtrlAltX()
    {
        // Ctrl=0x0002, Alt=0x0001, X=0x58
        var result = HotkeyService.FormatHotkey(0x0002 | 0x0001, 0x58);
        result.Should().Be("Ctrl + Alt + X");
    }

    [Fact]
    public void FormatHotkey_SingleModifier()
    {
        // Ctrl=0x0002, A=0x41
        var result = HotkeyService.FormatHotkey(0x0002, 0x41);
        result.Should().Be("Ctrl + A");
    }

    [Fact]
    public void FormatHotkey_AllModifiers()
    {
        // Win+Ctrl+Alt+Shift + F1 (0x70)
        var result = HotkeyService.FormatHotkey(0x0008 | 0x0002 | 0x0001 | 0x0004, 0x70);
        result.Should().Be("Win + Ctrl + Alt + Shift + F1");
    }

    // ─── ModifierKeysToWin32 ───────────────────────────────────────────

    [Fact]
    public void ModifierKeysToWin32_Control()
    {
        HotkeyService.ModifierKeysToWin32(ModifierKeys.Control).Should().Be(0x0002);
    }

    [Fact]
    public void ModifierKeysToWin32_Alt()
    {
        HotkeyService.ModifierKeysToWin32(ModifierKeys.Alt).Should().Be(0x0001);
    }

    [Fact]
    public void ModifierKeysToWin32_Shift()
    {
        HotkeyService.ModifierKeysToWin32(ModifierKeys.Shift).Should().Be(0x0004);
    }

    [Fact]
    public void ModifierKeysToWin32_Windows()
    {
        HotkeyService.ModifierKeysToWin32(ModifierKeys.Windows).Should().Be(0x0008);
    }

    [Fact]
    public void ModifierKeysToWin32_Combined()
    {
        var result = HotkeyService.ModifierKeysToWin32(ModifierKeys.Control | ModifierKeys.Shift);
        result.Should().Be(0x0002 | 0x0004);
    }

    [Fact]
    public void ModifierKeysToWin32_None()
    {
        HotkeyService.ModifierKeysToWin32(ModifierKeys.None).Should().Be(0u);
    }
}
