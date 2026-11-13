# Platform initialization attribution

Base commit: `35c9ead29e883317a7b72e240ea5b697d5dd153a`.

Windows Performance Recorder's CPU profile could not start: `0xc5585011`, missing system-profiling privilege. The unrestricted tool execution has no `SeSystemProfilePrivilege`; WPR remained idle. No ETW stacks, scheduler waits, or module-load timeline were captured. See `etw-unavailable.txt`.

As an unprivileged alternative, opt-in `JOJOT_STARTUP_TRACE=1` wraps Avalonia's existing public AppBuilder initializer delegates without reordering or replacing their behavior. It records wall-clock boundaries and whole-process CPU deltas. CPU accounting has coarse ticks, may include parallel process threads, and can exceed elapsed time; elapsed minus CPU is not a valid wait-time measurement. With tracing disabled, no wrappers or CPU queries are added.

Six measured NativeAOT diagnostic launches (plus two warmups) gave:

| Initializer | Median wall time | Median process CPU delta |
| --- | ---: | ---: |
| Runtime services | 0.14 ms | 0 ms |
| Text shaping | 0.01 ms | 0 ms |
| Skia rendering/font service | 9.81 ms | 15.62 ms |
| Windows windowing | 363.45 ms | 382.81 ms |

The Windows windowing initializer dominates and consumes substantial CPU. This narrows the prior broad platform interval; it does not identify individual native call stacks. [Avalonia's versioned source](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.6/src/Windows/Avalonia.Win32/Win32Platform.cs) shows that this initializer includes graphics setup, OLE, screen/display refresh and compositor setup. [Its graphics manager](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.6/src/Windows/Avalonia.Win32/Win32GlManager.cs) shows software rendering bypasses ANGLE initialization, making an isolated software trial useful for attribution. The default renderer is unchanged.

## Renderer trial

Twenty matched pairs, three warmups per mode, 100 notes of 1 KiB, trace disabled:

| Metric | Default GPU median / p95 | Software median / p95 |
| --- | ---: | ---: |
| First rendered frame | 815 / 1303 ms | 522 / 732 ms |
| Editor ready | 768 / 1306 ms | 444 / 663 ms |
| Populated editor render | 878 / 1405 ms | 561 / 780 ms |

Software reached the populated editor 36.1% sooner at the median on this workstation. The comparison alternates launch order and validates the same selected note, content length, native geometry, error-free exit and clean shutdown. Raw samples, hashes and summaries are under `software-timing/`. This local result does not establish performance on other GPUs, remote sessions or high-DPI/multi-monitor configurations.

The retained mode selector is opt-in: `JOJOT_RENDER_MODE=software`. With the variable unset or any other value, Avalonia's existing hardware-first defaults remain unchanged. The mode is selected before platform initialization and applies for the lifetime of the process.

## Steady-state workload

`JOJOT_RENDER_WORKLOAD=1` is honored only by the existing benchmark path. After recording startup, it edits a disposable note to a 2000-line, approximately 136 KiB text buffer, performs 90 full-buffer updates with one additional character each, then 90 alternating viewport jumps to the beginning/end. Every operation awaits a compositor batch. It records individual frame latencies and process CPU, then restores the original editor content before normal benchmark shutdown. This is a full-text replacement stress workload, not a claim about physical-key-to-screen latency. It does not simulate human input or activate the native window.

Run the pair harness with the same executable for both arguments, `-CandidateRenderMode software -RenderWorkload`. Ordinary startup comparisons explicitly remove the workload flag and tracing; renderer flags are set independently for each child. Frame latency includes layout, scheduling and compositor completion, not physical monitor presentation. The probe's final validation requires a genuinely scrollable extent and observed viewport movement.

Three measured workload pairs (one warmup pair) found:

| Workload | GPU median frame range across runs | Software median frame range | CPU per 90 operations |
| --- | ---: | ---: | --- |
| Full-buffer edit | 216–217 ms | 203–220 ms | GPU 17.47–17.80 s; software 17.30–17.81 s |
| Scroll | 33.23–33.44 ms | 48.64–61.05 ms | GPU 0.69–0.84 s; software 0.61–1.00 s |

Software scroll updates were materially slower despite similar CPU costs. Hardware remains the default. Raw per-frame results are in `render-frames/`; per-run summaries are in `render-workload.csv`. Every persisted note ID/content in all eight completed workload databases matched the original fixture after restoration (100 notes each). The final probe adds assertions and module inventory to confirm the viewport actually moved and the selected native renderer differs; the earlier measured workload used the same editing/scrolling operations.

## Native selector validation

The final same-binary validation confirmed `av_libGLESv2.dll`, `d3d11.dll`, `d3dcompiler_47.dll` and `dcomp.dll` loaded in default mode; none loaded in software mode. Both loaded Skia. In this diagnostic pair, windowing initialization took 386.15 ms / 390.63 ms process CPU with GPU versus 26.45 ms / 15.63 ms CPU with software. This supports graphics/composition initialization as the dominant cost. It is an isolated mode comparison, not ETW attribution to individual driver functions.

Both modes exercised viewport Y offsets from 0 to 65257.33 pixels, completed all 180 operations, shut down cleanly, and persisted the original contents of all 100 notes. Module inventories, traces and workload outputs are in `native-validation/`. This pair was a post-publish validation with tracing enabled and is excluded from startup performance comparisons.

The effect replicated with the file-backed fixture using the final executable for both modes: 20 matched pairs plus three warmups, tracing/workload disabled. Populated-editor median was **839.72 ms GPU → 492.18 ms software** (41.4% sooner); p95 **1149.99 → 845.48 ms**. Editor-ready median was **742.62 → 383.51 ms**. The file-backed fixture has 100 local 1 KiB notes, including 25 unsaved drafts, and matching window geometry/selection checks. This does not measure completion of every background file baseline read or network-share behavior. The gain repeated across two workloads, but all measurements remain from one workstation; the measured scroll tradeoff is the reason the software option is not the default.

## Final verification and preview

The final source passed a serial Release Rebuild with zero warnings/errors, and all **87 headless regression tests** (XML total87, passed87, failed0, errors0). The suite's intentional missing-table error-path test logs an exception during an otherwise passing run. Named-pipe tests ran outside the restricted sandbox. NativeAOT publishing succeeded with the two previously documented Avalonia MicroCom Boolean-parameter diagnostics; the package audit reported no advisories. The final source manifest matches the workspace. All 100 external source-file hashes in the file-backed fixture remain unchanged.

Preview launchers: `.local/avalonia-platform-profile/final/Try-NativeAOT.cmd` (hardware default), `Try-Software.cmd` (software opt-in), `Try-Trace.cmd` (detailed startup tracing). They use the existing disposable-fixture launch harness. The prior reviewed preview and installed application are unchanged. Local build/test evidence is under `.local/avalonia-platform-profile/`, including `tests.xml` and `test-build.log`.

No foreground computer automation, security-policy change, or commit was performed. The retained software option is a measured startup/runtime tradeoff, not a universal renderer recommendation.
