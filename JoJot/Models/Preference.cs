namespace JoJot.Models;

/// <summary>
/// Entity for the preferences table. Key/value settings store.
/// </summary>
public class Preference
{
    /// <summary>
    /// Unique setting key used as the primary key.
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Setting value stored as a string.
    /// </summary>
    public string Value { get; set; } = "";
}
