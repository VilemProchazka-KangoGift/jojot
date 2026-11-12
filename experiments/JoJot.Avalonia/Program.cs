using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace JoJot.AvaloniaExperiment;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var entry = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var options = ExperimentOptions.Parse(args);
            ExperimentApp.Options = options;
            ExperimentApp.Benchmark = options.BenchmarkOutput is null ? null : new BenchmarkProbe(entry, options);
            var result = AppBuilder.Configure<ExperimentApp>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
            return result != 0 ? result : Environment.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}

internal sealed class ExperimentApp : Application
{
    internal static ExperimentOptions Options { get; set; } = null!;
    internal static BenchmarkProbe? Benchmark { get; set; }

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
