# Windows startup comparison

Measured on 2026-11-12 with Windows 11 Pro build 26300, an AMD Ryzen 9 3900X, 64 GiB RAM, AMD Ryzen Balanced power profile, and 150% display scaling. All builds used .NET SDK 11.0.100 and Windows x64. The Avalonia versions use the same prototype source and Avalonia 12.1.6. The WPF probe uses the published `v2026.11.2` source at `554272b8a5c472827708d0d7ed9f388c8d6ef8a0`.

| Build | First render, median / p95 | Editor ready, median / p95 | Editor render, median / p95 | Publish payload |
| --- | ---: | ---: | ---: | ---: |
| WPF ReadyToRun | 754 / 790 ms | 798 / 828 ms | 842 / 872 ms | 142.1 MiB |
| Avalonia ReadyToRun | 829 / 918 ms | 788 / 892 ms | 840 / 932 ms | 127.5 MiB |
| Avalonia NativeAOT | 473 / 499 ms | 443 / 463 ms | 476 / 499 ms | 38.0 MiB |

NativeAOT cut median first-render latency by 43% and median editor-render latency by 43% against the identical Avalonia ReadyToRun source. It was faster on both render milestones in every measured round and reduced the published runtime payload by 70%. The deployment comparison includes trimming and native runtime linking. The managed Avalonia prototype took about as long as WPF to render the loaded editor and showed its first frame 75 ms later at the median. WPF and the prototype have different feature coverage, so a port with the full WPF feature set would need its own measurement.

## Measurement details

- Three warmups and 20 measured fresh processes per variant, using the same synthetic schema 2 fixture and a warm OS file cache. The fixture contains 100 notes with 1 KiB of ASCII text per note. Every sample selected the first note and loaded its text. The WPF probe checks the enabled editor, first selected index, note count, and editor text length. The Avalonia runner additionally checks the selected fixture note name.
- All 69 launches produced a first-frame outer rectangle of 850 by 630 physical pixels at (80, 80), with an 828 by 574 physical pixel client area. The WPF fixture specifies 560 physical pixels of height and the WPF minimum height results in 630 physical pixels at 150%. Both benchmark windows start in light mode and suppress foreground activation.
- WPF timestamps come from `ContentRendered`, the enabled and loaded editor, and the following `CompositionTarget.Rendering` event. Avalonia timestamps use the first requested composition batch completion, the enabled and loaded editor, and a subsequent editor batch completion. The Avalonia editor can become ready on the UI thread before its first batch completes. These framework milestones cover different render stages. The run measured neither display scanout nor interactive editing latency.
- Payload is the uncompressed publish directory with PDB, XML, and DBG files excluded. Memory and installer size are outside these results.

[Reproduction and launch instructions](../README.md) · [Raw samples](samples.csv) · [Summary statistics](summary.csv) · [Payload bytes and executable hashes](payloads.csv) · [Fixture file hashes](fixture.sha256) · [Prototype source hashes](prototype-source.sha256)

The source hashes describe the frozen snapshot used for both publishes. Final repository cleanup removed trailing blank lines from `MainWindow.axaml`, `Program.cs`, and `HostAdapters.cs`; their code is otherwise unchanged from that measured snapshot.

## Native runtime proof and dependencies

Module lists were captured after the editor-render milestone, with the window held open in a separate verification run. Both processes loaded `e_sqlite3.dll`. CoreCLR appeared only in the ReadyToRun process. Both processes then exited cleanly. [ReadyToRun module list](Managed-modules.txt) · [NativeAOT module list](NativeAOT-modules.txt)

The NativeAOT compiler completed with two diagnostics about generated Avalonia MicroCom reverse vtable methods that would throw on invocation because they take a non-blittable Boolean. The tested startup and SQLite path completed successfully. Other Windows graphics configurations and interop paths remain outside this run. [Native compiler diagnostics](native-publish-diagnostics.txt)

NuGet reported zero known vulnerabilities in the direct and transitive package graph on the run date. Package metadata declares MIT for Avalonia, SkiaSharp, and HarfBuzzSharp, BSD-3-Clause for the ANGLE package, Apache-2.0 for Serilog and SQLitePCLRaw, and public domain for SQLite. The inventory lists the resolved package licenses, including build-time and other-platform dependencies. A binary redistribution review would also need the notices for bundled native components. [NuGet audit](dependency-audit.json) · [Package license inventory](package-licenses.csv)
