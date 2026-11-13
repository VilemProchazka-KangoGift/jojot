# Continuous scrolling: wrap × Find highlights

Base commit: `19a22efbed9e3323348423f12aa65c1d6c576a21`; Avalonia 12.1.6 NativeAOT, same Windows workstation as prior renderer experiments.

The opt-in `JOJOT_CONTINUOUS_SCROLL=1` benchmark uses a 2000-line text note with two `needle` matches per line (4000 total). A 16 ms dispatcher timer requests 180 successive 24-DIP vertical increments, independently of compositor-batch waits. It records every actual offset and input interval, observed batch-completion intervals, process CPU, layout events and TextLayout identity changes, plus timed/allocated bytes inside the Find overlay's Render method. It verifies all 180 offsets are distinct and Find-on actually emits highlight rectangles; Find-off must emit none. The Find panel stays open with an empty query when highlights are off, keeping geometry and panel effects constant across the 2×2 matrix. The window is non-activating and the original note content is restored.

Frame intervals describe the probe's compositor-completion observations, not physical monitor presentation or externally generated mouse-wheel delivery. Input scheduling intervals are reported separately. The 16 ms timer can be delayed by UI work, so this measures an owned continuous-increment workload rather than promising an exact 60 Hz input stream. Layout events are a count, not exclusive layout CPU. Process CPU includes application background threads. ETW remains unavailable because the session lacks system-profile privilege.

The first probe setup was excluded because its Find-match assertion caught queued TextChanged invalidating the search generation. The corrected probe settles that event before starting Find, then asserts 4000 matches before measurement. There was no silent retry or accepting a zero-highlight case.

Baseline source evidence: [Avalonia TextLayout.HitTestTextRange](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.6/src/Avalonia.Base/Media/TextFormatting/TextLayout.cs) constructs a rectangle list sized to the complete line count, and scans preceding lines. JoJot previously called that API for each visible match on each scroll repaint. Initial measured runs allocated approximately 650–746 MB within highlight rendering over 180 increments, while TextLayout identity changes remained zero.

## Implemented change

`FindHighlightGeometry` caches cumulative line heights for the current immutable TextLayout, locates visible lines with a binary search, and asks those TextLine objects for only overlapping match bounds. It preserves selected-match exclusion and the framework's text-bound geometry, including wrapped/multiline, tab and bidirectional text. The line-height index is rebuilt when the TextLayout identity changes and released when Find matches are cleared. No cache of entire document match rectangles is created.

The allocation regression was reproduced red with the old visible-range algorithm: **1,937,096 bytes for one viewport**. The new implementation passes the under-100,000-byte bound. Geometry parity tests compare rectangles against Avalonia's original full-range API across wrapping, widths, tabs, Hebrew, line breaks and selection exclusion. These target shape and allocation behavior, not wall-clock thresholds.

Normal editing and scrolling keep the existing renderer selection. The additional timing/allocation counters are enabled only by the diagnostic probe. They describe the UI-side highlight drawing-command generation, not exclusive GPU paint time.

## Matched results

Each comparison interleaves three measured baseline/candidate pairs after one warmup pair. Both binaries contain the same probe and counters; baseline uses the original highlight algorithm, candidate uses the indexed visible-line geometry. Each launch runs all four cells. Values below are medians across three runs, before → after. Allocation is MiB, times are milliseconds. Raw measured arrays, executable/fixture hashes, harness samples and settings are preserved in the adjacent comparison directories.

| Renderer | Wrap | Find | Process CPU | Observed batch median | Highlight Render time | Highlight allocation MiB |
|---|---|---|---:|---:|---:|---:|
| GPU | Off | Off | 1156 → 906 | 33.66 → 33.44 | ~0 → ~0 | 0 → 0 |
| GPU | Off | On | 1031 → 781 | 33.29 → 33.33 | 160.90 → 37.11 | 719.33 → 3.89 |
| GPU | On | Off | 1844 → 1703 | 34.23 → 34.63 | ~0 → ~0 | 0 → 0 |
| GPU | On | On | 2203 → 1688 | 41.17 → 34.49 | 133.12 → 17.12 | 640.40 → 1.37 |
| Software | Off | Off | 1406 → 1438 | 49.70 → 48.01 | ~0 → ~0 | 0 → 0 |
| Software | Off | On | 1359 → 1344 | 47.98 → 52.71 | 176.81 → 36.71 | 638.82 → 3.30 |
| Software | On | Off | 1859 → 2109 | 49.80 → 50.12 | ~0 → ~0 | 0 → 0 |
| Software | On | On | 3141 → 2125 | 50.91 → 49.95 | 161.47 → 18.61 | 631.34 → 1.27 |

The supported improvement is over 99% less highlight allocation and approximately 77–88% less UI-side highlight Render time. Whole-process CPU and batch timings are noisier: unchanged Find-off controls also vary substantially, and software/no-wrap/Find-on batch observations worsened. This is not a universal frame-rate improvement. GPU remains the default; software remains opt-in.

Every measured cell recorded 180 distinct offsets. TextLayout identity changes were zero throughout: scrolling reused layout, while the old overlay repeatedly allocated document-sized buffers. Wrapping still costs more process CPU even with Find off; this experiment does not isolate that remaining cost into exclusive layout versus paint. Timer input medians were approximately 18 ms with GPU and 22 ms with software. Overlay render counts and observed batch counts differ, so the roughly 33/50 ms batch intervals must not be interpreted as monitor frame cadence. No physical display smoothness claim is made.

The `baseline-matrix-valid` directory is a separate initial three-pair GPU/software matrix using the same baseline binary. `gpu-before-after` and `software-before-after` are the matched optimization comparisons used in the table. All are from this run, not historical renderer benchmarks. Fixed cell order and one workstation limit generalization.

## Reproduction and preview

Publish snapshots live under `.local/avalonia-scroll-profile/baseline-valid` and `candidate`; their `source.sha256` files are copied here. From the repository root:

```powershell
& experiments/tools/Compare-AvaloniaPair.ps1 -BaselineExe .local/avalonia-scroll-profile/baseline-valid/native/JoJot.Avalonia.exe -CandidateExe .local/avalonia-scroll-profile/candidate/native/JoJot.Avalonia.exe -FixtureDirectory .local/avalonia-ux/final/fixture -OutputDirectory .local/avalonia-scroll-profile/repeat-gpu -Samples 3 -Warmups 1 -ContinuousScroll
# Repeat with -BaselineRenderMode software -CandidateRenderMode software for software rendering.
& experiments/tools/Summarize-ContinuousScroll.ps1 -RunDirectory .local/avalonia-scroll-profile/repeat-gpu -OutputCsv .local/avalonia-scroll-profile/repeat-gpu.csv
```

The isolated manual previews are `.local/avalonia-scroll-profile/candidate/Try-NativeAOT.cmd` and `Try-Software.cmd`. `Scroll-sample.txt` provides the measured long note; open it, Find `needle`, and toggle wrapping to inspect all four states. Launchers use disposable experiment data. No foreground automation was used.

## Validation

The final Release build has zero warnings/errors. The full suite passes **89/89 tests**, zero failures/errors/skips (see `final-tests.xml`). Geometry tests cover LF/CRLF, wrapped and unwrapped text, two widths, both paragraph flow directions, Hebrew, tabs, multiword and multiline ranges, and exclusion of a selected match proven visible. The initial strengthened test incorrectly required a nonempty collection after excluding the only visible match; that assertion was corrected without changing runtime code. The allocation test was demonstrated red before the optimization and green afterward.

All 25 completed probe databases (including the excluded setup attempt) preserve every original note ID and content. All 62 candidate publish source inputs match the current runtime working tree. Dependency-audit output, final build log, test XML and source hashes are included. `git diff --check` passes; existing line-ending conversion notices are informational. Native publishing retains the pre-existing Avalonia MicroCom Boolean interop diagnostics.
