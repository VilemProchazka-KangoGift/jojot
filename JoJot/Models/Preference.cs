namespace JoJot.Models;

/// <summary>
/// Entity for the preferences table. Key/value settings store.
/// </summary>
public class Preference
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
