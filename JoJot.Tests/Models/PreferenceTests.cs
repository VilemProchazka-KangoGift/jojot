using JoJot.Models;

namespace JoJot.Tests.Models;

public class PreferenceTests
{
    [Fact]
    public void DefaultValues_AreEmptyStrings()
    {
        var pref = new Preference();

        pref.Key.Should().Be("");
        pref.Value.Should().Be("");
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var pref = new Preference
        {
            Key = "theme",
            Value = "dark"
        };

        pref.Key.Should().Be("theme");
        pref.Value.Should().Be("dark");
    }
}
