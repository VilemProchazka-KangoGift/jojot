namespace JoJot.AvaloniaExperiment.Platform;

// WPF holds this same session mutex for the lifetime of its production data access.
internal sealed class ProductionDataLock : IDisposable
{
    private readonly Mutex _mutex;

    private ProductionDataLock(Mutex mutex) => _mutex = mutex;

    internal static ProductionDataLock Acquire()
    {
        var mutex = new Mutex(true, @"Local\JoJot_SingleInstance", out var created);
        if (created)
            return new ProductionDataLock(mutex);

        mutex.Dispose();
        throw new IOException("Close the previous JoJot app before opening its data with this version.");
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
