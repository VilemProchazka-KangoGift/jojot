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

    // ─── ShouldSwapDictionary ────────────────────────────────────

    private static readonly Uri LightUri = new("Themes/LightTheme.xaml", UriKind.Relative);
    private static readonly Uri DarkUri = new("Themes/DarkTheme.xaml", UriKind.Relative);

    [Fact]
    public void ShouldSwapDictionary_SameFile_NoSwap()
    {
        ThemeService.ShouldSwapDictionary(LightUri, LightUri).Should().BeFalse();
    }

    [Fact]
    public void ShouldSwapDictionary_DifferentFile_Swaps()
    {
        ThemeService.ShouldSwapDictionary(LightUri, DarkUri).Should().BeTrue();
        ThemeService.ShouldSwapDictionary(DarkUri, LightUri).Should().BeTrue();
    }

    [Fact]
    public void ShouldSwapDictionary_NoCurrentSource_Swaps()
    {
        ThemeService.ShouldSwapDictionary(null, LightUri).Should().BeTrue();
    }

    [Theory]
    [InlineData("/JoJot;component/Themes/LightTheme.xaml")]
    [InlineData("themes/lighttheme.xaml")]
    [InlineData("LightTheme.xaml")]
    public void ShouldSwapDictionary_EquivalentReferenceToSameFile_NoSwap(string current)
    {
        ThemeService.ShouldSwapDictionary(new Uri(current, UriKind.RelativeOrAbsolute), LightUri)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldSwapDictionary_UnrelatedDictionary_Swaps()
    {
        ThemeService.ShouldSwapDictionary(new Uri("Themes/Controls.xaml", UriKind.Relative), LightUri)
            .Should().BeTrue();
    }
}
