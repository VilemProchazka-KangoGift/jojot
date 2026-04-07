using JoJot.Services;

namespace JoJot.Tests.Services;

public sealed class LanguageServiceTests
{
    // ─── ParsePreference ────────────────────────────────────────────────

    [Theory]
    [InlineData(null, LanguageService.AppLanguage.English)]
    [InlineData("", LanguageService.AppLanguage.English)]
    [InlineData("en", LanguageService.AppLanguage.English)]
    [InlineData("cs", LanguageService.AppLanguage.Czech)]
    [InlineData("unknown", LanguageService.AppLanguage.English)]
    [InlineData("CS", LanguageService.AppLanguage.English)] // case-sensitive
    public void ParsePreference_ReturnsExpected(string? input, LanguageService.AppLanguage expected)
    {
        LanguageService.ParsePreference(input).Should().Be(expected);
    }

    // ─── ToPreferenceString ─────────────────────────────────────────────

    [Theory]
    [InlineData(LanguageService.AppLanguage.English, "en")]
    [InlineData(LanguageService.AppLanguage.Czech, "cs")]
    public void ToPreferenceString_ReturnsExpected(LanguageService.AppLanguage lang, string expected)
    {
        LanguageService.ToPreferenceString(lang).Should().Be(expected);
    }

    // ─── Round-trip ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(LanguageService.AppLanguage.English)]
    [InlineData(LanguageService.AppLanguage.Czech)]
    public void RoundTrip_ParseAndSerialize(LanguageService.AppLanguage lang)
    {
        var serialized = LanguageService.ToPreferenceString(lang);
        var parsed = LanguageService.ParsePreference(serialized);
        parsed.Should().Be(lang);
    }

    // ─── Plural ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "other")]
    [InlineData(1, "one")]
    [InlineData(2, "few")]
    [InlineData(3, "few")]
    [InlineData(4, "few")]
    [InlineData(5, "other")]
    [InlineData(10, "other")]
    [InlineData(100, "other")]
    public void Plural_SelectsCorrectForm(int count, string expected)
    {
        var result = LanguageService.Plural("one", "few", "other", count);
        result.Should().Be(expected);
    }
}
