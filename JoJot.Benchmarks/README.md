# JoJot storage benchmarks

Run `dotnet run --project JoJot.Benchmarks/JoJot.Benchmarks.csproj -c Release` on Windows for storage throughput. The harness creates a temporary SQLite database with desktops containing 10, 100, and 1,000 notes of 1 KiB each. It warms each operation for 100 calls, then reports the median and range of 15 samples of 500 sequential calls. It measures note loading, counts, names, previews, updates, and inserts through JoJot's stores and database worker. The insert case grows the database during measurement.

## Startup comparison

Run from PowerShell 7 with the .NET SDK required by the sources and an interactive Windows session.

```powershell
./JoJot.Benchmarks/Compare-Startup.ps1 -Baseline <baseline-source-directory>
```

Pass `-Candidate` to use a different candidate source tree. The script creates disposable Release, self-contained, ReadyToRun copies under `.local/startup-benchmark`. The baseline produces one database fixture with 100 one-KiB notes, a session on the current desktop, and matching preferences and geometry. Each sample starts a new process with its own copy of the database and clean-shutdown marker. The script interleaves scenarios and balances which variant launches first across rounds. It records three warmup pairs, then uses 20 measured pairs for medians, nearest-rank p95, and ranges. The OS file cache is left intact. Raw CSVs, summaries, and source SHA-256 manifests are written under the printed run directory.

The WPF runs cover startup with and without the preference and geometry snapshot. They record the first `ContentRendered` event, the point when the enabled editor contains the first seeded note and the list contains all 100 tabs, and completion of deferred startup work. `ReadyToUseMs` is the later of render and editor readiness for each sample. The disposable copies suppress foreground activation, editor keyboard focus, and global hotkey registration, and use their own data directory, mutex and pipe. The script also measures the isolated database path from open through the note load. The baseline's EF warmup runs when present.

Use `-Scenario Database` to run the database benchmark alone. Use `-PrepareOnly` to publish first, then pass the printed directory as `-PreparedRun` when the machine is idle. Supply a new `-MeasurementRoot` to reuse the binaries with a fixture generated in the current Windows session and security context.

### Window profiling

```powershell
./JoJot.Benchmarks/Compare-Startup.ps1 -Baseline <baseline-source-directory> -Scenario WpfBoth -WindowProfile -PrepareOnly
./JoJot.Benchmarks/Compare-Startup.ps1 -PreparedRun <printed-run-directory> -Scenario WpfBoth -WindowProfile -MeasurementRoot <ordinary-results-directory>
./JoJot.Benchmarks/Compare-Startup.ps1 -PreparedRun <printed-run-directory> -Scenario WpfBoth -WindowProfile -ControlGeometry -MeasurementRoot <controlled-results-directory>
```

`-WindowProfile` adds process-relative phase timestamps in disposable copies of the application. Subtract consecutive timestamps within a sample to measure an interval before taking its median. `EditorRenderingMs` measures the first WPF rendering opportunity after the editor loads. The ordinary run follows both production geometry paths. `-ControlGeometry` supplies the fixture's saved geometry before and immediately after `Show()` even when the snapshot is missing, suppresses the redundant later application of that same geometry, and requires the same first-render bounds across both snapshot states and builds. This keeps the initial layout comparable while retaining the database preference wait on a snapshot miss.

[EF Core removal results and raw samples](results/sqlite-migration/README.md)

[Window startup profile and before/after samples](results/window-startup/README.md)

To exercise the deferred hamburger menu in a separate WPF process, run:

```powershell
./JoJot.Benchmarks/Run-MenuSmoke.ps1 -Dotnet <path-to-dotnet.exe>
```

The script builds a uniquely named disposable copy under `.local`, disables the normal startup handler, and redirects app data. It checks localization, theme changes, orphan state, menu reuse, and the preferences action.
