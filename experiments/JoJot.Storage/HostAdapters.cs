namespace JoJot.Services;

// The host must select its own isolated directory before using the startup snapshot.
public static class AppEnvironment
{
    private static string? _appDataDirectory;
    public static string AppDataDirectory => _appDataDirectory ?? throw new InvalidOperationException("Set the experiment data directory first.");
    public static void SetAppDataDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        DatabaseCore.IsUiThread = null;
        _appDataDirectory = Path.GetFullPath(path);
        Directory.CreateDirectory(_appDataDirectory);
    }
}
