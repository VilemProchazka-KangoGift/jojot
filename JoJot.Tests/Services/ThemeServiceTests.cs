using JoJot.Services;
using static JoJot.Services.ThemeService;

namespace JoJot.Tests.Services;

/// <summary>
/// Tests for ThemeService preference↔enum conversion helpers.
/// </summary>
public class ThemeServiceTests
{
    // ─── ParseThemePreference ────────────────────────────────────

    [Fact]
    public void ParseThemePreference_Light()
    {
        ThemeService.ParseThemePreference("light").Should().Be(AppTheme.Light);
    }

    [Fact]
    public void ParseThemePreference_Dark()
    {
        ThemeService.ParseThemePreference("dark").Should().Be(AppTheme.Dark);
    }

    [Fact]
    public void ParseThemePreference_System()
    {
        ThemeService.ParseThemePreference("system").Should().Be(AppTheme.System);
    }

    [Fact]
    public void ParseThemePreference_Null_DefaultsToSystem()
    {
        ThemeService.ParseThemePreference(null).Should().Be(AppTheme.System);
    }

    [Fact]
    public void ParseThemePreference_Empty_DefaultsToSystem()
    {
        ThemeService.ParseThemePreference("").Should().Be(AppTheme.System);
    }

    [Fact]
    public void ParseThemePreference_UnknownValue_DefaultsToSystem()
    {
        ThemeService.ParseThemePreference("sepia").Should().Be(AppTheme.System);
    }

    [Fact]
    public void ParseThemePreference_CaseSensitive_MixedCase_DefaultsToSystem()
    {
        // The switch is case-sensitive; "Light" doesn't match "light"
        ThemeService.ParseThemePreference("Light").Should().Be(AppTheme.System);
    }

    // ─── ThemeToPreferenceString ─────────────────────────────────

    [Fact]
    public void ThemeToPreferenceString_Light()
    {
        ThemeService.ThemeToPreferenceString(AppTheme.Light).Should().Be("light");
    }

    [Fact]
    public void ThemeToPreferenceString_Dark()
    {
        ThemeService.ThemeToPreferenceString(AppTheme.Dark).Should().Be("dark");
    }

    [Fact]
    public void ThemeToPreferenceString_System()
    {
        ThemeService.ThemeToPreferenceString(AppTheme.System).Should().Be("system");
    }

    // ─── Round-trip ──────────────────────────────────────────────

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.System)]
    public void RoundTrip_ThemeToStringAndBack(AppTheme theme)
    {
        var str = ThemeService.ThemeToPreferenceString(theme);
        var parsed = ThemeService.ParseThemePreference(str);
        parsed.Should().Be(theme);
    }
}
