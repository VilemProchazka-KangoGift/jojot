using JoJot.AvaloniaExperiment.Platform;
using Xunit;

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class MacGlobalHotkeyTests
{
    [Theory]
    [InlineData(0x41u, 0x00u)] // A has native keycode zero and must remain supported.
    [InlineData(0x4Eu, 0x2Du)]
    [InlineData(0x7Au, 0x67u)]
    [InlineData(0x2Eu, 0x75u)]
    public void MapsSupportedKeys(uint virtualKey, uint nativeKey)
    {
        Assert.True(MacGlobalHotkey.SupportsKey(virtualKey));
        Assert.Equal(nativeKey, MacGlobalHotkey.MapKey(virtualKey));
    }

    [Fact]
    public void UnsupportedKeyAndModifierMappingAreExplicit()
    {
        Assert.False(MacGlobalHotkey.SupportsKey(0xFF));
        Assert.Equal(0x1B00u, MacGlobalHotkey.MapModifiers(15));
        Assert.Equal(0u, MacGlobalHotkey.MapModifiers(0));
    }
}
