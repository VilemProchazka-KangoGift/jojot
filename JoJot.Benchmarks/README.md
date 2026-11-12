# JoJot storage benchmarks

Run `dotnet run --project JoJot.Benchmarks/JoJot.Benchmarks.csproj -c Release` on Windows. The harness creates a temporary SQLite database with desktops containing 10, 100, and 1,000 notes of 1 KiB each. It warms each operation for 100 calls, then reports the median and range of 15 samples of 500 sequential calls. It measures note loading, counts, names, previews, updates, and inserts through JoJot's real stores, including its DB worker and EF Core. Lower times are better. The insert case writes new rows, so its database grows during measurement.

The results are microbenchmarks; they do not measure startup, typing latency, or UI responsiveness. Disk cache, background activity, and JIT warmup can affect results.
