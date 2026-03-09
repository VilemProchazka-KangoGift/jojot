using JoJot.Services;

namespace JoJot.Tests.Services;

/// <summary>
/// Additional HotkeyService tests for FormatHotkey edge cases.
/// </summary>
public class HotkeyServiceCoverageTests
{
    [Fact]
    public void FormatHotkey_NoModifiers_KeyOnly()
    {
        // No modifiers, just F5 (0x74)
        var result = HotkeyService.FormatHotkey(0, 0x74);
        result.Should().Be("F5");
    }

    [Fact]
    public void FormatHotkey_WinOnly()
    {
        // Win + A (0x41)
        var result = HotkeyService.FormatHotkey(0x0008, 0x41);
        result.Should().Be("Win + A");
    }

    [Fact]
    public void FormatHotkey_AltOnly()
    {
        // Alt + Tab (0x09)
        var result = HotkeyService.FormatHotkey(0x0001, 0x09);
        result.Should().Be("Alt + Tab");
    }
}
