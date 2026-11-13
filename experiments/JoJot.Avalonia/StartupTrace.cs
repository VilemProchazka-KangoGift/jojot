using System.Diagnostics;
using System.Globalization;
using JoJot.Services;

namespace JoJot.AvaloniaExperiment;

// Opt in with JOJOT_STARTUP_TRACE=1. Buffer timings so tracing adds no startup file I/O.
internal static class StartupTrace
{
    private static readonly object Gate = new();
    private static readonly List<(long Timestamp, string Phase)> Events = [];
    private static long _entry;
    private static volatile bool _enabled;

    internal static void Begin(long entry)
    {
        lock (Gate)
        {
            Events.Clear();
            _entry = entry;
            _enabled = Environment.GetEnvironmentVariable("JOJOT_STARTUP_TRACE") == "1";
        }
        Mark("entry");
    }

    internal static void Mark(string phase)
    {
        if (!_enabled) return;
        lock (Gate)
            if (_enabled) Events.Add((Stopwatch.GetTimestamp(), phase));
    }

    internal static void Flush()
    {
        if (!_enabled) return;
        (long Timestamp, string Phase)[] events;
        long entry;
        lock (Gate)
        {
            if (!_enabled) return;
            _enabled = false;
            events = Events.ToArray();
            entry = _entry;
            Events.Clear();
        }
        try
        {
            var path = Path.Combine(AppEnvironment.AppDataDirectory,
                $"startup-trace-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Environment.ProcessId}.tsv");
            File.WriteAllLines(path, events.Select(item =>
                Stopwatch.GetElapsedTime(entry, item.Timestamp).TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + "\t" + item.Phase));
        }
        catch (Exception ex) { LogService.Warn("Could not write startup trace", ex); }
    }
}
