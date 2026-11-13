# Avalonia UX update startup comparison

Measured on 2026-11-13 on Windows 11 Pro build 26300 with an AMD Ryzen 9 3900X, 64 GiB RAM and 150% display scaling. The two Windows x64 Avalonia builds use the same 54-file source snapshot, .NET SDK 11.0.100 and Avalonia 12.1.6. WPF uses the instrumented ReadyToRun build of release `v2026.11.2` at `554272b8a5c472827708d0d7ed9f388c8d6ef8a0`.

| Build | First render, median / p95 | Editor ready, median / p95 | Editor render, median / p95 | Runtime payload |
| --- | ---: | ---: | ---: | ---: |
| WPF ReadyToRun | 924 / 1,644 ms | 969 / 1,675 ms | 1,029 / 1,747 ms | 142.1 MiB |
| Avalonia ReadyToRun | 1,101 / 1,651 ms | 1,123 / 1,705 ms | 1,216 / 1,833 ms | 128.6 MiB |
| Avalonia NativeAOT | 648 / 963 ms | 596 / 963 ms | 659 / 1,070 ms | 41.3 MiB |

NativeAOT reached the rendered editor **370 ms sooner than WPF** at the median, a 36% reduction. It reached the same milestone **557 ms sooner than same-source Avalonia ReadyToRun**, a 46% reduction. The deployment comparison includes trimming and runtime linking. WPF was also slower in this session than in the earlier parity comparison, and the window geometry changed. The table compares builds measured together in this run.

## Controls and provenance

- Each build received three warmups and 20 measured launches. The schedule shuffles each set of six launch permutations. Each launch used a fresh copy of the same light-theme schema 2 fixture containing 100 notes of 1 KiB each. All 69 observations passed fixture, exit, log and geometry checks. The summary retains all outliers.
- The new 420 DIP Avalonia minimum applies to its client area. An initial run at the prior geometry failed the equality check and was discarded. The accepted run used Avalonia client dimensions of 552 by 420 DIP and a WPF startup snapshot with an outer height of 686 physical pixels. The fixture database and clean-shutdown marker came from the original fixture. Every accepted launch had an 850 by 686 pixel outer rectangle at (80, 80) and an 828 by 630 pixel client area.
- WPF uses `ContentRendered` and the next editor `CompositionTarget.Rendering` event. Avalonia waits for compositor batch completion. These process-age timestamps capture different framework stages. Physical scanout and interactive keystroke latency remain separate measurements.
- Full Avalonia uses IPC, actual virtual desktop detection, session matching, storage, welcome and recovery. Both benchmark hosts suppress foreground activation and skip hotkey registration. Payload counts uncompressed publish files excluding PDB, XML and DBG.
- The measured source manifest SHA-256 is `32F1D6815423CA33FCAFFEBFEEC470D95476A76515F0C1A466CB2FC4BC86FA68`. All source inputs matched the workspace after timing. The managed executable is an app host, so the provenance also includes the application assembly hashes.

[Reproduction](../../README.md) · [Raw samples](samples.csv) · [Summary](summary.csv) · [Payload and executable hashes](payloads.csv) · [Managed assembly hashes](managed-assemblies.csv) · [Source hashes](source.sha256) · [Fixture hashes](fixture.sha256)

## Runtime and compatibility

Both full builds loaded native SQLite after rendering the editor. CoreCLR appeared only in the ReadyToRun module list. The two NativeAOT compiler diagnostics concern generated Avalonia MicroCom reverse vtable methods with non-blittable Boolean parameters. [ReadyToRun modules](Managed-modules.txt) · [NativeAOT modules](NativeAOT-modules.txt) · [Compiler diagnostics](native-publish-diagnostics.txt)

NuGet reported zero advisories in the direct and transitive package graph. Resolved package versions match the prior full parity run. [Audit](dependency-audit.json) · [Package licenses](package-licenses.csv)

The released WPF editor → UX NativeAOT editor → released WPF editor round trip passed against disposable data. The UX build also passed 54 managed Avalonia headless tests. The startup and compatibility checks ran without Computer Use input. [Compatibility log](../../../.local/avalonia-ux/final/compatibility.log)
