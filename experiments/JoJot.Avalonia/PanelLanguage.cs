using System.Globalization;
using JoJot.Resources;

namespace JoJot.AvaloniaExperiment;

internal static class PanelLanguage
{
    internal static string Current => Strings.Culture?.TwoLetterISOLanguageName == "cs" ? "cs" : "en";

    internal static void Apply(string? preference)
    {
        var culture = CultureInfo.GetCultureInfo(preference == "cs" ? "cs-CZ" : "en-US");
        Strings.Culture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
    }

    internal static string Plural(string one, string few, string other, int count) =>
        count == 1 ? one : count is >= 2 and <= 4 ? few : other;
}
