# Full Avalonia startup comparison

Measured on 2026-11-12 on Windows 11 Pro build 26300 with an AMD Ryzen 9 3900X, 64 GiB RAM, the AMD Ryzen Balanced power profile, and 150% display scaling. All builds target Windows x64. The two full Avalonia builds use .NET SDK 11.0.100, Avalonia 12.1.6, and identical frozen source.

| Build | First render, median / p95 | Editor ready, median / p95 | Editor render, median / p95 | Runtime payload |
| --- | ---: | ---: | ---: | ---: |
| WPF ReadyToRun, release source | 728 / 763 ms | 767 / 805 ms | 803 / 845 ms | 142.1 MiB |
| Original Avalonia NativeAOT prototype | 452 / 496 ms | 419 / 462 ms | 452 / 496 ms | 38.0 MiB |
| Full Avalonia ReadyToRun | 861 / 923 ms | 865 / 931 ms | 925 / 1,002 ms | 128.4 MiB |
| Full Avalonia NativeAOT | 493 / 555 ms | 471 / 520 ms | 505 / 555 ms | 41.2 MiB |

Full NativeAOT reached the rendered editor **298 ms sooner than WPF at the median**, a 37% reduction. It reached the same milestone **419 ms sooner than the identical-source Avalonia ReadyToRun build**, a 45% reduction. The full NativeAOT candidate took 54 ms longer than the original, narrower NativeAOT prototype. The same-source deployment comparison includes trimming and native runtime linking.

## Controls and provenance

- The WPF executable is the instrumented ReadyToRun build of `v2026.11.2`, source commit `554272b8a5c472827708d0d7ed9f388c8d6ef8a0`. The prototype NativeAOT executable matches the recorded executable for `2f2f70a`. The full builds share a 50-file frozen snapshot, including the linked JoJot storage sources and build configuration. Its manifest SHA-256 is `2AC1035A278FFE905E3A1BE4CD18C45C457ADF76B55D52E97427A93279C47BFE`. After timing, four inputs had extra blank lines removed at the end of the file. An ordinal comparison after trimming trailing whitespace verified those four files, and the other 46 inputs remained byte-for-byte identical. The measured snapshot was retained. The final checkout manifest records the post-cleanup hashes.
- Every build received three warmups and 20 measured fresh processes. The schedule put each build in each launch slot five times and after each other build five times across the measured rounds. The OS file cache was warm. All observations are retained, including the WPF sample that reached editor render in 1,253 ms.
- All 92 launches used copies of the same synthetic schema 2 fixture with 100 notes of 1 KiB each, loaded the first note, and reported identical Win32 bounds. The first-frame window was 850 by 630 physical pixels at (80, 80), with an 828 by 574 pixel client area. All builds started light and suppressed foreground activation.
- The full Avalonia probe uses the normal IPC, virtual desktop detection, session matching, storage, welcome and recovery paths. It validates the fixture desktop against the current detected Windows desktop and lets deferred recovery finish before auto-close. Timing excludes global hotkey registration in both WPF and full Avalonia to avoid changing the workstation's hotkey ownership. The original prototype has a narrower startup path.
- WPF uses `ContentRendered` and the next editor `CompositionTarget.Rendering` event. Avalonia waits for compositor batch completion. The enabled editor can be ready before the Avalonia frame completes. These timestamps describe process creation to framework milestones. Physical display presentation, memory and keystroke latency require separate measurements.
- Payload counts uncompressed publish-directory bytes excluding PDB, XML and DBG files. Installer size is outside the comparison.

[Reproduction and launch instructions](../../README.md) · [Raw samples](samples.csv) · [Summary](summary.csv) · [Payload and executable hashes](payloads.csv) · [Measured source manifest](source.sha256) · [Final checkout manifest](final-source.sha256) · [Fixture hashes](fixture.sha256)

## Runtime, dependencies and validation

Both full builds loaded the native SQLite module after rendering the editor. CoreCLR appeared only in the ReadyToRun module list. The NativeAOT compiler emitted two diagnostics about generated Avalonia MicroCom reverse vtable methods with non-blittable Boolean parameters. The measured Windows startup path and the separate native checks completed successfully. [ReadyToRun modules](Managed-modules.txt) · [NativeAOT modules](NativeAOT-modules.txt) · [Native compiler diagnostics](native-publish-diagnostics.txt)

NuGet reported zero advisories in the resolved direct and transitive package graph on the run date. The package metadata inventory includes MIT, Apache-2.0, the BSD-3-Clause ANGLE package, and public-domain SQLite. It includes build-time and other-platform packages. Bundled native notices would need their own review before redistribution. [NuGet audit](dependency-audit.json) · [Package licenses](package-licenses.csv)

Separate NativeAOT checks passed the WPF editor round trip, Czech resource rendering, and an empty-database first launch followed by reopen. The latter produced one 470-character welcome note on both launches and clean shutdown markers. Native visual exports establish rendering of the application visual tree. Real desktop switching, physical hotkey input and other interactive parity checks remain tracked in the feature matrix. [Feature-level validation](../../PARITY.md)
