using System.Diagnostics;
using System.Globalization;
using JoJot.Services;
using Avalonia;

namespace JoJot.AvaloniaExperiment;

// Opt in with JOJOT_STARTUP_TRACE=1. Buffer timings so tracing adds no startup file I/O.
internal static class StartupTrace
{
    private static readonly object Gate = new();
    private static readonly List<(long Timestamp, string Phase)> Events = [];
    private static long _entry;
    private static volatile bool _enabled;

    internal static AppBuilder TraceSubsystems(AppBuilder builder)
    {
        if (!_enabled) return builder;
        var runtime = builder.RuntimePlatformServicesInitializer;
        var windowing = builder.WindowingSubsystemInitializer;
        var rendering = builder.RenderingSubsystemInitializer;
        var shaping = builder.TextShapingSubsystemInitializer;
        if (runtime is not null)
            builder.UseRuntimePlatformSubsystem(() => Measure("runtime", runtime), builder.RuntimePlatformServicesName!);
        if (windowing is not null)
            builder.UseWindowingSubsystem(() => Measure("windowing", windowing), builder.WindowingSubsystemName!);
        if (rendering is not null)
            builder.UseRenderingSubsystem(() => Measure("rendering", rendering), builder.RenderingSubsystemName!);
        if (shaping is not null)
            builder.UseTextShapingSubsystem(() => Measure("text-shaping", shaping), builder.TextShapingSubsystemName!);
        return builder;
    }

    private static void Measure(string name, Action initialize)
    {
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        Mark(name + "-start");
        initialize();
        Mark(name + "-end");
        process.Refresh();
        Mark(name + "-process-cpu-ms=" + (process.TotalProcessorTime - cpu).TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
    }

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
