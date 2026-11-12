# SQLite startup comparison

Measured on November 12, 2026 with .NET SDK 11.0.100 and Windows build 26300, using x64 self-contained Release builds with ReadyToRun. The baseline was commit `b1694cff743c68db9d60e3dad2baba5817defdb7`. The candidate source manifest covers the production files under `JoJot/` before adding the probe. Its canonical LF bytes have SHA-256 `75017e0e95637e650dfa55e7a36abe6185d1efb5e65ce1fa06bef7844f0d3414`. [Candidate source manifest](candidate-source.sha256)

## Startup times

| Scenario | Milestone | Baseline median | Direct SQLite median | Baseline p95 | Direct SQLite p95 |
| --- | --- | ---: | ---: | ---: | ---: |
| WPF with snapshot | First render | 776.70 ms | 774.74 ms | 932.12 ms | 800.67 ms |
| WPF with snapshot | Rendered and editor enabled | 849.26 ms | 817.61 ms | 1,004.72 ms | 840.55 ms |
| WPF with snapshot | Deferred startup complete | 911.32 ms | 865.75 ms | 1,065.41 ms | 889.92 ms |
| WPF without snapshot | First render | 891.10 ms | 696.99 ms | 995.81 ms | 745.56 ms |
| WPF without snapshot | Rendered and editor enabled | 987.68 ms | 752.90 ms | 1,087.27 ms | 823.72 ms |
| WPF without snapshot | Deferred startup complete | 1,040.42 ms | 793.89 ms | 1,150.03 ms | 871.40 ms |
| Database only | Process start to notes loaded | 361.22 ms | 136.71 ms | 393.72 ms | 143.39 ms |
| Database only | Database open through notes loaded | 281.64 ms | 56.39 ms | 307.27 ms | 59.77 ms |

With the snapshot, median first render moved by 2 ms and readiness improved by 32 ms (3.7%). Without the snapshot, median readiness improved by 235 ms (23.8%). The candidate reached readiness sooner in 19 of 20 measured pairs with the snapshot and all 20 without it. The median isolated database path fell by 80%.

The probe publications contained 164,158,923 bytes and 149,321,252 bytes respectively, a 14.15 MiB reduction. These are published directory totals including PDBs. The candidate publish and restored application dependency graph contained no EF Core assemblies or packages.

## Method and raw data

Each scenario used 20 measured pairs after three warmup pairs, alternating the variant launched first. Each launch used a fresh process and copy of a version 2 database created by the EF baseline, with the same 100 one-KiB notes, settings, and clean-shutdown marker. The OS file cache was left intact. The primary runs used normal Windows virtual desktop access and the same current desktop fixture. The sandbox runs used the app's fallback desktop after Windows denied access to virtual desktop COM.

The WPF clock starts at process creation. The readiness measurement is the later of the first WPF `ContentRendered` event and the enabled editor loading the seeded note with all 100 tabs present. Both probe copies suppressed foreground activation, editor focus, and global hotkey registration. Each WPF sample verified the fixture desktop and tab count, exited with a clean-shutdown marker, and produced no error or fatal log entries.

The database-only case calls the application's storage services for open, schema and integrity checks, preference load, session check and ensure, migrations, the welcome-note check, and note load. It calls EF model warmup when the baseline provides it. The clean marker exercises the periodic integrity-check skip path.

- Primary samples with warmups and summaries. [WPF with snapshot](wpf.csv) · [WPF without snapshot](wpf-without-snapshot.csv) · [Database only](database.csv) · [All summary statistics](summary.csv)
- Supplemental sandbox samples. [WPF with snapshot](sandbox-wpf.csv) · [Database only](sandbox-database.csv)
- Reproduction. [Benchmark instructions](../../README.md) · [PowerShell runner](../../Compare-Startup.ps1)

## Migration verification

- Baseline Debug passed 1,268 of 1,268 tests. Baseline Release passed 1,264 tests and failed four tests that assume Debug behavior.
- The candidate passed all 1,295 Debug tests and all 251 focused Release storage tests. The Release rebuild completed with zero warnings and errors.
- The candidate coverage run executed every executable line that sets up or issues SQLite commands in `DatabaseCore` and the four stores. [Query coverage map](../../../docs/sqlite-query-coverage.md)
