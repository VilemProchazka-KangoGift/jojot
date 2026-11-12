using System.Globalization;

namespace JoJot.AvaloniaExperiment;

internal sealed record ExperimentOptions(string DataDirectory, string DesktopGuid, string? BenchmarkOutput,
    double Width, double Height, bool StayOpen, string? CompatibilityExpected, string? CompatibilityReplacement,
    string? VisualOutputDirectory = null)
{
    private static readonly string[] StorageFiles =
        ["jojot.db", "jojot.db-wal", "jojot.db-shm", ".clean-shutdown", ".experiment-lock", ".experiment-owner", ".experiment-owner.Dev", "startup-cache.json"];

    public static ExperimentOptions Parse(string[] args)
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#if DEBUG
        var directory = Path.Combine(localData, "JoJot.AvaloniaExperiment.Dev");
#else
        var directory = Path.Combine(localData, "JoJot.AvaloniaExperiment");
#endif
        var desktop = "default";
        string? benchmark = null;
        var width = 850d;
        var height = 560d;
        var stayOpen = false;
        var explicitDirectory = false;
        string? compatibilityExpected = null;
        string? compatibilityReplacement = null;
        string? visualOutput = null;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == "--stay-open")
            {
                stayOpen = true;
                continue;
            }

            if (++index == args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for {argument}.");
            var value = args[index];

            switch (argument)
            {
                case "--data-dir": directory = value; explicitDirectory = true; break;
                case "--compat-expected": compatibilityExpected = value; break;
                case "--compat-replacement": compatibilityReplacement = value; break;
                case "--visual-output": visualOutput = NormalizePath(value); break;
                case "--desktop-guid": desktop = value; break;
                case "--benchmark-output": benchmark = NormalizePath(value); break;
                case "--benchmark-width": width = ParseDimension(value, 510); break;
                case "--benchmark-height": height = ParseDimension(value, 360); break;
                default: throw new ArgumentException($"Unknown option {argument}.");
            }
        }

        directory = NormalizePath(directory);
        foreach (var folder in new[] { "JoJot", "JoJot.Dev" })
        {
            var production = Path.Combine(localData, folder);
            if (directory.Equals(production, StringComparison.OrdinalIgnoreCase) ||
                directory.StartsWith(production + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                benchmark?.StartsWith(production + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == true)
                throw new ArgumentException("Choose a disposable directory outside installed JoJot data.");
        }

        if (benchmark is not null &&
            StorageFiles.Any(file => benchmark.Equals(Path.Combine(directory, file), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Benchmark output must be separate from the experiment database and its sidecar files.");

        if (string.IsNullOrWhiteSpace(desktop))
            throw new ArgumentException("Desktop identifier cannot be blank.");

        if (compatibilityExpected is not null || compatibilityReplacement is not null)
        {
            if (compatibilityExpected is null || compatibilityReplacement is null || !explicitDirectory || benchmark is not null || stayOpen)
                throw new ArgumentException("Compatibility editing requires both text values, an explicit disposable data directory and automatic shutdown.");
        }
        if (visualOutput is not null && (!explicitDirectory || benchmark is not null || stayOpen ||
            compatibilityExpected is not null || !visualOutput.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Visual output requires a subdirectory of an explicit disposable data directory and automatic shutdown.");
        return new ExperimentOptions(directory, desktop, benchmark, width, height, stayOpen,
            compatibilityExpected, compatibilityReplacement, visualOutput);
    }

    public void ValidateDataPath()
    {
        // Refuse junctions and symbolic links so the experimental filename cannot
        // resolve to a production database through an alternate path.
        ValidateAncestors(new DirectoryInfo(DataDirectory));
        foreach (var name in StorageFiles)
            ValidateNoReparsePoint(new FileInfo(Path.Combine(DataDirectory, name)));
        ValidateBenchmarkPath();
        if (VisualOutputDirectory is not null)
            ValidateAncestors(new DirectoryInfo(VisualOutputDirectory));
    }

    public void ValidateBenchmarkPath()
    {
        if (BenchmarkOutput is null)
            return;
        var output = new FileInfo(BenchmarkOutput);
        ValidateNoReparsePoint(output);
        ValidateAncestors(output.Directory);
    }

    private static void ValidateAncestors(DirectoryInfo? directory)
    {
        for (; directory is not null; directory = directory.Parent)
            ValidateNoReparsePoint(directory);
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            fullPath.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            fullPath.Split(Path.DirectorySeparatorChar).Any(part => part.EndsWith('.') || part.EndsWith(' ')))
            throw new ArgumentException("Choose a path without device prefixes or trailing spaces and periods.");
        return fullPath;
    }

    private static void ValidateNoReparsePoint(FileSystemInfo entry)
    {
        if (entry.LinkTarget is not null || (entry.Exists && entry.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            throw new IOException($"Experimental data cannot use a reparse point ({entry.FullName}).");
    }

    private static double ParseDimension(string value, double minimum)
    {
        if (!double.TryParse(value, CultureInfo.InvariantCulture, out var size) ||
            !double.IsFinite(size) || size < minimum || size > 4096)
            throw new ArgumentException($"Window dimension must be between {minimum} and 4096 DIP.");
        return size;
    }
}
