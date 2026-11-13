# Reviewed Avalonia startup comparison

The Avalonia builds use the same snapshot of commit `7e454eaf07d19d2137c853d977a55711d0a7e8e6` with .NET SDK 11.0.100 and Avalonia 12.1.6. The WPF probe uses release `v2026.11.2` source at `554272b8a5c472827708d0d7ed9f388c8d6ef8a0`. Measured on Windows 11 Pro build 26300 on an AMD Ryzen 9 3900X at 150% scale.

| Workload | Build | First render, median / p95 | Editor ready, median / p95 | Editor render, median / p95 |
| --- | --- | ---: | ---: | ---: |
| Ordinary | WPF ReadyToRun | 1,048 / 1,750 ms | 993 / 1,597 ms | 1,069 / 1,723 ms |
| Ordinary | Avalonia ReadyToRun | 1,152 / 2,027 ms | 1,162 / 2,174 ms | 1,275 / 2,341 ms |
| Ordinary | Avalonia NativeAOT | 783 / 1,453 ms | 731 / 1,470 ms | 823 / 1,564 ms |
| File-backed | WPF ReadyToRun | 1,272 / 1,578 ms | 1,152 / 1,447 ms | 1,255 / 1,526 ms |
| File-backed | Avalonia ReadyToRun | 1,190 / 1,455 ms | 1,209 / 1,485 ms | 1,331 / 1,643 ms |
| File-backed | Avalonia NativeAOT | 736 / 895 ms | 682 / 776 ms | 777 / 914 ms |

NativeAOT reached the rendered editor 23% sooner than WPF and 35% sooner than Avalonia ReadyToRun at the median for ordinary notes. For file-backed notes the reductions were 38% and 42%. The workload batches ran sequentially, so their medians cannot isolate the cost of file-backed notes. Within each workload the three builds were interleaved. The NativeAOT payload was 42.1 MiB, compared with 128.7 MiB for Avalonia ReadyToRun and 142.1 MiB for WPF.

## Fixture and measurement

- Both workloads use 100 notes of 1 KiB each. The file-backed workload points each note at a separate local text file. Twenty-five stored drafts differ from their saved contents, including the initially selected note. File contents were unchanged after the run. These local fixtures do not exercise disconnected network shares or quantify the time to finish background baseline reads.
- Each workload recorded 3 warmups and 20 measured launches per build. All 138 launches passed the selected-note, clean-shutdown, and geometry checks. Native rectangles matched at 850 × 686 outer and 828 × 630 client pixels. The warm OS file cache and all outliers were retained.
- WPF timestamps come from its content-rendered event and the subsequent editor rendering event. Avalonia timestamps come from compositor batch completion. These are framework milestones and can occur in different order relative to editor readiness. The benchmark excludes foreground activation, hotkey registration, and interactive input.
- The 59-input source manifest includes the authoritative `JoJot.csproj` version input, and was checked again against the workspace after timing. Both Avalonia builds use the same snapshot. The NativeAOT executable reports file version `2026.11.2.0`.
- Runtime payload is the uncompressed publish directory excluding PDB, XML, and DBG files. Both Avalonia builds loaded native SQLite after rendering the editor. CoreCLR appeared only in ReadyToRun. NuGet reported no advisories for direct or transitive dependencies. The NativeAOT compiler repeated two diagnostics about generated Avalonia MicroCom reverse vtable methods with non-blittable Boolean parameters.

[Ordinary samples](ordinary/samples.csv) · [Ordinary summary](ordinary/summary.csv) · [File-backed samples](file-heavy/samples.csv) · [File-backed summary](file-heavy/summary.csv) · [Payloads and executable hashes](ordinary/payloads.csv) · [Managed application assembly hash](managed-app-assembly.sha256) · [Source hashes](source.sha256) · [External fixture hashes](files.sha256.csv) · [Runtime modules](NativeAOT-modules.txt) · [NuGet audit](dependency-audit.json)
