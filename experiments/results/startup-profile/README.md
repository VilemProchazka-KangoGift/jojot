# NativeAOT startup phase investigation

Measured on the same Windows workstation and fixtures as the reviewed comparison, from base commit `a6fdd3200584d4bb75237cbc55df94741440a7a8`. Each A/B pair alternates order, uses a fresh disposable copy of the same database/snapshot/clean-shutdown marker, and requires 100 notes, selected Note 0, 1024 editor characters, matching native geometry, successful exit and clean shutdown. Timing excludes foreground activation and hotkey registration; caches are warm. No builds ran during timed batches. Outliers are retained.

Instrumentation is opt-in (`JOJOT_STARTUP_TRACE=1`). It records elapsed milliseconds from Main entry, unlike benchmark timestamps which include process age at Main entry. Raw timestamps and phase names are buffered under a lock; formatting and file writing happen at benchmark editor-render completion, or process shutdown for interactive launches. Files are named `startup-trace-<UTC timestamp>-<PID>.tsv` in the chosen experiment data directory. They contain no note text. Timing comparisons run with tracing disabled.

## Experiments

| Trial | Pairs | Baseline editor render median / p95 | Candidate editor render median / p95 | Decision |
| --- | ---: | ---: | ---: | --- |
| Start DB before window construction, ordinary | 20 | 810 / 946 ms | 742 / 1161 ms | Not retained |
| Same change, ordinary repeat | 30 | 832 / 980 ms | 845 / 1218 ms | Gain did not reproduce |
| Same change, file-backed | 20 | 883 / 1053 ms | 811 / 1120 ms | Median improved, tail worsened |
| DirectComposition first, ordinary | 20 | 880 / 1200 ms | 858 / 994 ms | Not retained: small mixed change and rendering tradeoff |

All batches include three additional warmups per build. The DirectComposition experiment kept ANGLE rendering but replaced the default WinUIComposition-first compositor preference with DirectComposition plus RedirectionSurface fallback; it exists only in a local frozen snapshot, not application source.

Representative initial traces put platform initialization at roughly 280–335 ms, window construction at 25–80 ms, and database opening at roughly 65 ms. Those are diagnostic observations, not statistically isolated component costs. Starting DB earlier overlaps construction but did not reliably improve full startup. Repeated UI-dispatcher round trips in the database pipeline were the next candidate.

Evidence directories contain raw samples, summaries, fixture and binary hashes. Source manifests describe the exact frozen variants. The example traces use the original diagnostic filename/format; each was collected into a separate run directory, so no prior trace was overwritten. The retained tracer now uses unique filenames and synchronized timestamp buffering.

The batched database phase also measured flat: 30 pairs, rendered-editor median 811.45 → 809.16 ms and p95 1120.35 → 1123.14 ms; editor-ready median 716.26 → 724.43 ms. It was reverted. No experimental startup or rendering change remains in application source.

## Phase evidence and next step

Six initial baseline traces (three pairs of the same binary) yielded these median spans:

| Span | Median |
| --- | ---: |
| Platform setup before styles | 298.1 ms |
| Window construction | 34.5 ms |
| Opened to database-open completion | 63.5 ms |
| Database-open to preference-cache completion | 6.0 ms |
| Preferences to remaining storage startup completion | 12.5 ms |
| Editor ready to compositor batch completion | 76.9 ms |

These spans include scheduling and concurrent activity; they are not exclusive CPU times and cannot be summed into a universal startup budget. The last span is a compositor milestone, not physical screen presentation. Batching reduced the DB-open-to-preferences gap to less than 1 ms in an example trace, but that small phase was not the overall bottleneck.

The next grounded experiment is an ETW/WPA CPU and image-load trace restricted to the approximately 300 ms platform-start→styles-start interval, identifying graphics-driver/device initialization, native library loads and font setup separately. Only after identifying the dominant stack should renderer/font settings change. A software-renderer A/B could isolate GPU initialization cost, but would also require scrolling/input/CPU and visual checks before adoption. This investigation did not establish those runtime tradeoffs.

## Reproduction

Run `experiments/tools/Compare-AvaloniaPair.ps1` with `-BaselineExe`, `-CandidateExe`, `-FixtureDirectory`, a new `-OutputDirectory`, `-Samples 30 -Warmups 3`. Add `-Trace` only for diagnostic runs. The fixture directory must contain `desktop.txt`, `jojot.db`, `startup-cache.json`, and `.clean-shutdown`. Native desktop COM must be available; restricted-sandbox probes are not timing evidence.

The local frozen variants are under `.local/avalonia-startup-profile/{baseline,overlap,direct-composition,batched-db}/native`. They are experimental artifacts, not a replacement for the reviewed launcher. Source/binary hashes are included to distinguish them. A native fresh database and subsequent reopen in the batching trial both produced exactly one welcome note with 470 characters; no duplicate welcome was created.

## Final validation

The final diagnostic-only source passed an isolated serial Release rebuild with zero warnings/errors and all 87 headless tests (xUnit XML: total 87, passed 87, failed/errors/skipped 0). The named-pipe tests require running outside the restricted sandbox: the sandbox run failed three pipe connections with access-denied errors. The suite intentionally exercises a missing-table failure and logs that exception during a passing run. Native trial publishes completed with the same two previously documented Avalonia MicroCom Boolean-parameter AOT diagnostics; NuGet audit returned no advisories.

Local final test evidence: `.local/avalonia-startup-profile/tests-unrestricted.xml` and `test-build.log`. No foreground computer automation was used. The previously reviewed release launcher remains unchanged.

Final diagnostic preview: `.local/avalonia-startup-profile/final/Try-NativeAOT.cmd`; opt-in launcher: `Try-NativeAOT-With-Trace.cmd` beside it. Exit the application through its menu to flush an interactive trace (closing all windows keeps the process alive). The build is self-contained. Four final non-activating native smoke launches passed: two opted-in launches each wrote 21 phases ending in `editor-rendered`; two tracing-disabled launches wrote no trace file. The final source manifest matches the workspace. The first smoke launch was cold after publishing; smoke timings are validation only and are excluded from performance comparisons.

Final publishing initially encountered NU1900 because NuGet vulnerability metadata was unavailable; retry completed, still showing the storage restore's cached NU1900 warning. The subsequent package audit completed successfully and returned no advisories (`final-dependency-audit.json`). The two existing native MicroCom diagnostics were unchanged.
