# JoJot window startup profile

## Ordinary startup

All times are in milliseconds. The baseline and candidate median columns summarize each build independently. The median paired change summarizes candidate minus baseline within each round.

| Snapshot | Milestone | Baseline median | Candidate median | Baseline p95 | Candidate p95 | Median paired change | Candidate faster |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Present | First render | 719.93 | 704.91 | 751.33 | 746.29 | -5.54 | 11/20 |
| Present | Rendered and editor enabled | 761.79 | 742.65 | 787.62 | 784.26 | -11.39 | 13/20 |
| Present | Editor rendering opportunity | 795.77 | 780.15 | 823.36 | 825.73 | -10.19 | 13/20 |
| Present | Startup complete | 799.32 | 784.00 | 828.10 | 829.38 | -9.86 | 13/20 |
| Absent | First render | 685.63 | 683.76 | 716.40 | 752.80 | -2.48 | 11/20 |
| Absent | Rendered and editor enabled | 744.18 | 730.83 | 772.68 | 820.81 | -8.94 | 14/20 |
| Absent | Editor rendering opportunity | 778.72 | 765.28 | 809.17 | 864.39 | -9.29 | 14/20 |
| Absent | Startup complete | 782.93 | 769.57 | 812.98 | 868.37 | -9.70 | 14/20 |

Median readiness improved by 19.14 ms (2.5%) with the snapshot and 13.36 ms (1.8%) without it. The no-snapshot p95 rose by 48.13 ms. The p95 change with the snapshot was −3.37 ms, and the single slowest candidate launch took 977.22 ms to be ready.

- With the snapshot, the 977 ms candidate launch spent 232 ms in `Show()`, 50 ms moving the window to the virtual desktop, and 121 ms from placement and move completion to `ContentRendered`. The paired baseline spent 147 ms, 5 ms, and 2 ms in those intervals.
- Without the snapshot, the 821 ms candidate p95 launch spent 279 ms in window XAML initialization and 71 ms from placement and move completion to editor readiness. The paired baseline took 268 ms and 59 ms. The slowest no-snapshot candidate launch took 911 ms overall, with 343 ms in window XAML initialization versus 259 ms in its baseline pair.

The tails span several startup phases. All launches remain in the reported statistics. The ordinary first-render bounds also differed by snapshot condition. The snapshot opened at the saved 567 × 420 DIPs, while the no-snapshot condition rendered at the initial 1,250 × 600 DIPs before the database geometry was applied.

[Snapshot present raw data](ordinary-present.csv) · [Snapshot absent raw data](ordinary-absent.csv) · [Snapshot present summary](ordinary-present-summary.csv) · [Snapshot absent summary](ordinary-absent-summary.csv)

## Startup with matching first-render bounds

The controlled experiment fixed the first-render WPF rectangle at (53.33, 53.33, 566.67, 420) DIPs across all launches.

| Snapshot | Milestone | Baseline median | Candidate median | Baseline p95 | Candidate p95 | Median paired change | Candidate faster |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Present | First render | 712.62 | 707.67 | 779.13 | 738.49 | -14.57 | 15/20 |
| Present | Rendered and editor enabled | 748.70 | 745.15 | 817.84 | 778.72 | -16.33 | 15/20 |
| Present | Editor rendering opportunity | 783.54 | 780.44 | 861.39 | 817.60 | -15.16 | 15/20 |
| Present | Startup complete | 787.45 | 784.08 | 865.63 | 821.35 | -15.13 | 15/20 |
| Absent | First render | 708.01 | 708.47 | 726.56 | 759.11 | -1.01 | 11/20 |
| Absent | Rendered and editor enabled | 756.61 | 746.67 | 776.10 | 808.79 | -9.74 | 14/20 |
| Absent | Editor rendering opportunity | 791.57 | 781.75 | 814.08 | 844.25 | -10.53 | 14/20 |
| Absent | Startup complete | 797.14 | 785.63 | 824.13 | 849.55 | -10.66 | 14/20 |

Within the candidate, including the snapshot changed paired first-render time by −10.02 ms and paired readiness by −12.58 ms relative to omitting it. Both milestones were faster in 14 of 20 rounds. In the baseline, including the snapshot changed paired first-render time by +1.63 ms and readiness by −9.03 ms.

The candidate no-snapshot p95 also rose in the controlled run. In the 809 ms candidate launch, synchronous `Show()` took 163 ms and the interval from placement and move completion to editor readiness took 76 ms. The paired baseline spent 151 ms and 50 ms. In the slowest candidate launch (949 ms), window XAML initialization took 303 ms and `Show()` took 183 ms, compared with 256 ms and 148 ms in its baseline pair.

[Snapshot present raw data](controlled-present.csv) · [Snapshot absent raw data](controlled-absent.csv) · [Snapshot present summary](controlled-present-summary.csv) · [Snapshot absent summary](controlled-absent-summary.csv)

## Phase profile at matched bounds

| Phase | Baseline, snapshot | Candidate, snapshot | Baseline, no snapshot | Candidate, no snapshot |
| --- | ---: | ---: | ---: | ---: |
| Process creation to managed checkpoint | 61.88 | 61.82 | 61.65 | 62.32 |
| Data directory and logging | 38.25 | 38.29 | 38.25 | 38.23 |
| App constructor | 48.96 | 49.20 | 48.41 | 48.88 |
| App.xaml loading | 29.49 | 29.28 | 30.02 | 29.73 |
| Snapshot load | 29.61 | 17.40 | 0.17 | 0.17 |
| Preference preparation after snapshot attempt | 0.01 | 0.07 | 22.80 | 22.83 |
| Theme setup | 6.68 | 6.87 | 6.71 | 6.91 |
| Virtual desktop initialization | 20.27 | 21.04 | 20.81 | 20.79 |
| Window base and fields | 16.66 | 16.21 | 16.71 | 16.56 |
| Window view model | 0.10 | 0.10 | 0.10 | 0.10 |
| MainWindow.InitializeComponent | 259.57 | 254.91 | 259.81 | 256.68 |
| Constructor after XAML | 0.66 | 0.66 | 0.64 | 0.67 |
| Pre-show geometry and title | 6.20 | 6.17 | 6.21 | 6.15 |
| Window.Show | 150.74 | 151.79 | 150.86 | 149.95 |
| Native post-show placement | 17.63 | 17.43 | 17.20 | 17.38 |
| Move window to virtual desktop | 3.60 | 3.94 | 4.47 | 4.61 |
| Positioned to first render | 4.99 | 4.84 | 3.74 | 3.90 |

Main window XAML initialization and synchronous `Show()` consumed about 410 ms together in the baseline. The candidate with the deferred hamburger tree recorded XAML initialization medians 3–5 ms lower. Native placement consumed about 17 ms and moving to the desktop consumed about 4 ms. The snapshot load median fell from 29.61 to 17.40 ms. On a cache miss, about 23 ms remained before the database preferences were available.

An earlier eight-pair trial with just the JSON change reduced median snapshot loading from 31.18 to 18.02 ms. Both variants began with the same fixture bounds in that trial, and the cache-miss path also reapplied the database geometry after showing the window. [Snapshot present raw data](snapshot-only-present.csv) · [Snapshot absent raw data](snapshot-only-absent.csv) · [Snapshot-only source](snapshot-only-source.sha256)

## No-snapshot repeat

The two initial no-snapshot runs had a higher candidate p95. A separate 50-pair ordinary no-snapshot run reused the frozen binaries and added three warmup pairs.

| Milestone | Baseline median | Candidate median | Baseline p95 | Candidate p95 | Median paired change | Candidate faster |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| First render | 709.73 | 699.53 | 862.97 | 842.94 | -6.17 | 29/50 |
| Rendered and editor enabled | 770.09 | 750.10 | 940.37 | 909.97 | -14.47 | 32/50 |
| Editor rendering opportunity | 805.51 | 785.99 | 974.57 | 959.39 | -13.93 | 32/50 |
| Startup complete | 809.67 | 789.83 | 980.95 | 968.54 | -14.35 | 32/50 |

The repeat again showed lower median readiness. The candidate readiness p95 was lower by 30.40 ms in the repeat, and both builds recorded higher absolute p95s than in the two initial runs. The p95 direction varied by run.

[Repeat raw data](no-snapshot-repeat.csv) · [Repeat summary](no-snapshot-repeat-summary.csv)


## Method

Measured on November 12, 2026 with Windows build 26300, 24 logical CPUs, and .NET SDK 11.0.100. Both variants were x64 self-contained Release publications with ReadyToRun. Baseline source was exported from `e1e5ad7776edcc43634ea5254726f7260fe402b5`, after the direct SQLite migration. The candidate adds source-generated JSON metadata for startup snapshots and defers the hamburger menu's visual tree until its first open. The SHA-256 manifests cover the copied files before instrumentation. The measured candidate copy included an unreferenced build log, IDE settings, and a project user file with an empty property group. The runner now excludes these local files. [Baseline source](baseline-source.sha256) · [Candidate source](candidate-source.sha256)

Each final condition used 20 measured baseline/candidate pairs after three warmup pairs. The runner interleaved the snapshot conditions, balanced variant order across rounds, and used the same published binaries for both geometry experiments. Every launch started a fresh process and copied the same seeded database and clean-shutdown marker. The fixture used 100 one-KiB notes, English, the light theme, and the current Windows virtual desktop. The no-snapshot condition starts with this existing database and omits only the cache file. The OS file cache was left intact.

The ordinary run kept the application geometry paths. The controlled run applied the same seeded bounds around `Show()` for both snapshot states and skipped the extra application of those bounds after the database geometry lookup. The runner checked the actual WPF first-render rectangle across all controlled launches. Both modes preserved database preference loading and verified all 100 tabs and the seeded editor content. The probes isolated the data, mutex, and IPC pipe, suppressed foreground activation, keyboard focus, and hotkey registration, checked the current desktop and logs, and shut down cleanly.

`FirstRenderMs` measures the first WPF `ContentRendered` event from process creation. `ReadyToUseMs` is the later of that event and the enabled editor loading the seeded note. `EditorRenderingMs` is the first WPF rendering opportunity after the editor loads. These events measure application readiness and rendering work inside WPF. The instrumentation starts with a process age estimate at the first managed probe checkpoint and uses the monotonic stopwatch for phase durations. Phase medians are calculated from within-launch timestamp differences. The p95 uses nearest rank.

[Reproduction instructions](../../README.md) · [Runner](../../Compare-Startup.ps1) · [Probe](../../StartupProbe.cs.txt)

## Behavior checks

- Debug test suite passed 1,297 of 1,297 tests.
- Strict Release rebuild completed with zero warnings and errors. The 41 focused Release startup snapshot, theme, and geometry tests passed.
- The isolated WPF menu check verified lazy creation, recovery visibility as orphan sessions were added and removed, Czech menu text, distinct light and dark theme resources, menu reuse after reopening, and the Preferences action. [Smoke check runner](../../Run-MenuSmoke.ps1)
- Two deliberate changes in disposable copies made the WPF check fail as expected. Skipping the initial recovery update exposed the empty recovery item. Recreating the tree on reopen failed the reuse check.
