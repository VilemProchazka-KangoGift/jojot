# Software-first / GPU-later feasibility

**Neither supported initialization approach produced a software-to-GPU transition in Avalonia 12.1.6.** Option mutation retained software rendering; backend reinitialization was explicitly rejected. The production application is unchanged.

Baseline commit: `ef5ebdce3f73b72e2ed79d4a72e4080312b04ea0`, Avalonia 12.1.6, NativeAOT.

The source constrains a live transition:

- [Compositor](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.6/src/Avalonia.Base/Rendering/Composition/Compositor.cs) constructs its server with the graphics implementation at initialization.
- [PlatformRenderInterfaceContextManager](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.6/src/Avalonia.Base/Rendering/PlatformRenderInterfaceContextManager.cs) retains that graphics implementation in a readonly field. Software starts with no graphics implementation. Resetting/recovering its backend still uses the same original graphics object.
- [Win32 WindowImpl](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.6/src/Windows/Avalonia.Win32/WindowImpl.cs) chooses its native surface during construction and stores the GL surface in a readonly field.

A `Win32PlatformOptions.RenderingMode` assignment after the first frame does not itself reinitialize those objects. A custom transition would need to coordinate compositor/context recreation and native surfaces, not only mutate the preference. Private rendering interfaces also changed in Avalonia 12; the [official breaking-change notes](https://github.com/AvaloniaUI/avalonia-docs/blob/main/docs/avalonia12-breaking-changes.md) identify `ISkiaGpu` as internal.

Native probes are built in a frozen `.local/avalonia-hybrid-profile/probe/source` copy. They run only with `JOJOT_HYBRID_PROBE=mutate` or `reinitialize`, after the first populated editor frame. The mutation probe changes options and creates a replacement MainWindow. The reinitialization probe additionally invokes the original windowing initializer before creating that replacement. Both capture module inventories, compositor identity and transition time. The experiment is deliberately isolated from application source because reinitializing a process-wide backend is not a supported application lifecycle contract.

## Native outcomes from this run

| Attempt | Repeats | Replacement created | Compositor | GPU modules | Result |
| --- | ---: | --- | --- | --- | --- |
| Change options, create a new window | 2 | Yes | Same software compositor | Absent before and after | Not a renderer transition; replacement cost 218.75 / 252.54 ms |
| Reinvoke Win32 initializer, then recreate | 2 | No | Original software compositor | Absent before and after | Rejected in 0.13–0.15 ms: `InvalidOperationException: UI thread dispatcher is already initialized` |

All four probes exited cleanly and retained the seeded selection/content checks. The initial startup timestamps in their raw CSVs precede the transition attempt, so they must not be described as successful hybrid GPU-ready timings. The very first mutation launch was cold after publication and is not a fair performance sample. No working hybrid renderer was produced; consequently there is no valid hybrid scrolling benchmark.

No private guard was bypassed, no reflection/UnsafeAccessor patch was attempted, and no hybrid code was added to the application. Replacing only the application window does not recreate the process-wide compositor. A real software→GPU transition would require upstream/framework-level support for safely rebuilding contexts, surfaces, caches and render-loop ownership, or a separately designed process handoff. A process handoff would also need to preserve edits, undo state, IPC/DB ownership and window identity; it is not equivalent to an in-place graphics switch and was not implemented here.

The isolated probe source is `HybridProbe.cs.txt`; `program-probe.diff` and `benchmark-probe.diff` show its exact integration points. `probe-source.sha256` and the binary hashes beside each raw outcome identify the tested artifact. NativeAOT compilation succeeded with the existing two MicroCom diagnostics. The subsequent NuGet audit request failed because api.nuget.org DNS was unavailable; no dependency was changed.

## Reproduction

From the repository root with the existing local .NET11/Visual Studio toolchain and package cache:

```powershell
& experiments/results/hybrid-profile/Publish-Probe.ps1 -SnapshotOnly
& experiments/results/hybrid-profile/Prepare-Probe.ps1 -SnapshotDirectory .local/avalonia-hybrid-profile/reproduce/source
& experiments/results/hybrid-profile/Publish-Probe.ps1 -PublishOnly
$env:JOJOT_HYBRID_PROBE = 'mutate' # repeat separately with 'reinitialize'
& experiments/tools/Compare-AvaloniaPair.ps1 `
  -BaselineExe .local/avalonia-hybrid-profile/reproduce/native/JoJot.Avalonia.exe `
  -CandidateExe .local/avalonia-hybrid-profile/reproduce/native/JoJot.Avalonia.exe `
  -FixtureDirectory .local/avalonia-ux/final/fixture `
  -OutputDirectory .local/avalonia-hybrid-profile/reproduce-mutation `
  -Samples 1 -Warmups 0
Remove-Item Env:JOJOT_HYBRID_PROBE
```

Choose a fresh output directory for the second mode. The two executables are intentionally identical for these feasibility repetitions. Each child uses a fresh disposable fixture and never activates its window. Native desktop COM must be available (the restricted execution sandbox cannot provide it). The scripts refuse to reuse the snapshot/output directories and do not replace existing data.

## Startup comparison measured in this investigation

The current unchanged executable was compared in 20 alternating GPU/software pairs, with three warmups per mode, the same ordinary-note fixture and tracing/workload disabled:

| Metric | GPU median / p95 | Software median / p95 | Hybrid |
| --- | ---: | ---: | --- |
| First rendered frame | 915 / 2691 ms | 470 / 2554 ms | No working transition |
| Editor ready | 878 / 2553 ms | 405 / 2405 ms | No working transition |
| Populated editor render | 977 / 2942 ms | 494 / 2564 ms | No working transition |

Software's median remained substantially lower. Both modes had large tail outliers in this run; all samples were retained. These are warm-cache desktop measurements from one workstation, not a universal timing guarantee. No GPU-ready hybrid latency can be quoted from the failed attempts. `current-mode-timing/` contains raw samples and identities.

Historical evidence from the preceding [platform investigation](../platform-profile/README.md), not newly measured here: ordinary rendered-editor medians 878 ms GPU / 561 ms software; file-backed 840 / 492 ms. Three prior steady-state pairs gave GPU scroll medians 33.23–33.44 ms, software 48.64–61.05 ms, with broadly similar CPU cost. Those results explain why GPU remains the default and software is an explicit opt-in.


## Scrolling/editing measured in this investigation

One new matched GPU/software workload pair (90 edits + 90 viewport jumps per mode; no warmup pair):

| Scenario | GPU median / p95 frame | Software median / p95 frame | GPU / software CPU |
| --- | ---: | ---: | ---: |
| Full-buffer edit | 167.26 / 214.40 ms | 168.96 / 201.89 ms | 13.44 / 12.88 s |
| Scroll | 33.20 / 36.23 ms | 47.62 / 62.38 ms | 0.719 / 0.703 s |

Both modes moved the viewport from Y=0 to Y=65257.33. The software scroll cadence remained slower despite similar CPU; edits were similar. This single pair corroborates the prior multi-pair result, but is not a new statistical characterization. These are compositor-completion times for a 136 KiB stress note, not physical-input latency. Raw frame arrays and native module inventories are in `current-render-workload/`. Hybrid scrolling is unavailable because neither transition probe created a GPU renderer.

## Validation and conclusion

The reproduction scripts were executed through snapshot preparation; every regenerated source hash exactly matched the inputs of the tested NativeAOT probe. Both scripts parse successfully. Every persisted note ID/content in all 52 completed disposable databases from this investigation matched the original fixture (46 normal startup samples including warmups, four transition probes, two steady-state runs). The current GPU/software executable's source manifest matches the unchanged application tree.

Only this evidence/reproducer directory was added. Application source, renderer defaults and the existing software opt-in were not changed, so the previously passing 87-test suite was not rerun for a documentation-only retained change. Native probes provide the new feasibility evidence. There are no running test processes or foreground automation actions.

**Recommendation:** keep the existing explicit GPU/software modes. A software-first/GPU-later feature needs a supported custom-renderer or framework transition design before application integration. It cannot be implemented safely by changing the mode option or recreating a normal window with this pinned backend. Do not present the early software frame as a successful hybrid startup, or bypass the dispatcher guard as a production solution.
