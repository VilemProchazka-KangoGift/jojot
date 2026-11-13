# JoJot Avalonia NativeAOT experiment

The Avalonia candidate reuses JoJot's production SQLite stores, schema, and migrations. It implements the tabbed editor, autosave, undo, file-backed notes, maintenance panels, preferences and localization, plus Windows IPC, hotkeys, virtual desktop sessions and move recovery. The parity matrix tracks evidence and outstanding desktop checks by feature. [Parity status](PARITY.md)

## Build

Use PowerShell 7 and the .NET 11 SDK on Windows x64. NativeAOT uses the Visual Studio C++ linker and Windows SDK. A standard installation uses Visual Studio 2022 or newer with the Desktop development with C++ workload. [Microsoft NativeAOT prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/index) · [Avalonia NativeAOT guide](https://docs.avaloniaui.net/docs/deployment/native-aot)

Publish both variants from the same frozen source with the same SDK. From the repository root, choose a new output path.

```powershell
$dotnet = 'dotnet'
$project = 'experiments/JoJot.Avalonia/JoJot.Avalonia.csproj'
$root = '.local/avalonia-ux/reproduction'
& $dotnet publish $project -c Release -r win-x64 --self-contained -p:PublishAot=false -p:PublishReadyToRun=true -o "$root/managed"
& $dotnet publish $project -c Release -r win-x64 --self-contained -p:PublishAot=true -p:PublishReadyToRun=false -o "$root/native"
& experiments/tools/Prepare-WpfBaseline.ps1 -Dotnet $dotnet
```

The workstation's installed toolchain exposed the modern linker and OneCore CRT libraries. The NativeAOT command on this workstation adds `-p:IlcUseEnvironmentalTools=true` with the following process-local paths.

```powershell
$msvc = 'C:/Program Files/Microsoft Visual Studio/18/Community/VC/Tools/MSVC/14.51.36231'
$sdk = 'C:/Program Files (x86)/Windows Kits/10/Lib/10.0.19041.0'
$env:Path = "$msvc/bin/Hostx64/x64;$env:Path"
$env:LIB = "$msvc/lib/onecore/x64;$sdk/ucrt/x64;$sdk/um/x64"
```

## Try the candidate

Run the launcher on the virtual desktop where the fixture was seeded. It copies the fixture into a fresh directory and prints that directory. To test persistence, edit a note, switch tabs, close the window, and reopen the executable with `--data-dir` set to that same copy. The installed JoJot data stays outside the experiment.

The latest UX builds on the test workstation have separate launchers. [NativeAOT launcher](../.local/avalonia-ux/final/Try-NativeAOT.cmd) · [ReadyToRun launcher](../.local/avalonia-ux/final/Try-ReadyToRun.cmd)

```powershell
$aotExe = "$root/native/JoJot.Avalonia.exe"
$managedExe = "$root/managed/JoJot.Avalonia.exe"
& experiments/tools/Try-Avalonia.ps1 -Executable $aotExe -WpfRun $wpfRun
```

Set `$wpfRun` to the path printed by `Prepare-WpfBaseline.ps1`. To refresh the fixture, run its companion `baseline/database/JoJot.Benchmarks.exe seed-startup <new-directory>` on the current virtual desktop. Pass the resulting path with `-FixtureDirectory` to the launcher, runtime verifier and timing harness.

## Validation

The managed headless suite exercises the Avalonia controls and SQLite data paths. Storage tests exercise migration and four-store persistence. The frontend compatibility script checks the released WPF editor → NativeAOT editor → released WPF editor sequence using a disposable fixture.

The UX candidate passed 54 managed Avalonia headless tests. Earlier checks passed all 6 storage tests, 1,297 WPF Release tests and 5 WPF Debug environment tests.

The earlier frozen NativeAOT build passed the WPF-to-Avalonia-to-WPF editor round trip, the invalid compatibility-input exit check, controlled IPC owner recovery, virtual desktop enumeration and notification registration, and delivery of a synthetic Windows hotkey message. An empty database opened and reopened with one welcome note. Additional Windows interaction checks covered file dialogs, a recorded shortcut, and the Keep Here, Go Back, and Merge move decisions. [Native interaction checks](NATIVE_INTERACTION_CHECKS_2026-11-12.md) · [Compatibility log](../.local/avalonia-parity/compat-final.log) · [Native validation artifacts](../.local/avalonia-parity/native-validation-042a6477563a4182b08aea2489772ccc)

```powershell
& $dotnet test experiments/JoJot.Storage.Tests/JoJot.Storage.Tests.csproj -c Release
& $dotnet build experiments/JoJot.Avalonia.Tests/JoJot.Avalonia.Tests.csproj -c Release -m:1
& $dotnet experiments/JoJot.Avalonia.Tests/bin/Release/net11.0/win-x64/JoJot.Avalonia.Tests.dll -xml "$root/headless-tests.xml"
& experiments/tools/Verify-DatabaseCompatibility.ps1 -NativeExecutable $aotExe -Dotnet $dotnet
```

The direct xUnit runner avoids the VSTest host connection timeout observed on this workstation. Subsequent interactive native checks passed for file dialogs, a recorded non-Win global shortcut, and desktop Keep Here, Go Back and Merge after COM-initiated moves. The [native interaction report](NATIVE_INTERACTION_CHECKS_2026-11-12.md) records the evidence and reproducible probe commands. Win-modifier shortcut input, manual Task View dragging, monitor/DPI placement and OS shutdown still need verification. [Feature-level validation](PARITY.md)

## Measure

The current run compares WPF `v2026.11.2` ReadyToRun with both deployment modes of the Avalonia UX update. The two Avalonia builds share application source. The earlier four-build run also includes the original NativeAOT prototype from `2f2f70a`, which has a shorter startup path. [Earlier four-build results](results/parity/README.md) · [Original prototype results](results/README.md)

Prepare the WPF probe from the release tag. The WPF probe starts with isolated IPC names, suppresses foreground activation and the global hotkey, and exits automatically. Both full Avalonia builds detect the actual Windows desktop and use isolated fixture copies. The WPF build is a loose self-contained publish from the release source.

Run on the display and desktop used to seed the fixture. On the 150%-scale test display use a copy of the WPF fixture with the `startup-cache.json` window geometry height set to 686 physical pixels. Pass this copy as `$fixture`. This matches Avalonia's 552 by 420 DIP client size at that scale. On another display use `-Samples 1 -Warmups 0` to calibrate both rectangles before timing. Allow competing builds and other intensive work to finish before timing.

```powershell
& experiments/tools/Verify-AvaloniaRuntime.ps1 -ManagedExe $managedExe -AotExe $aotExe -WpfRun $wpfRun -OutputDirectory "$root/runtime-proof"
& experiments/tools/Compare-AvaloniaStartup.ps1 -WpfRun $wpfRun -FixtureDirectory $fixture -FullAvalonia -ManagedExe $managedExe -AotExe $aotExe -OutputDirectory "$root/measurement" -Width 552 -Height 420
```

Each build gets three warmups and 20 measured fresh processes. All launches start from copies of the same schema 2 fixture containing 100 notes of 1 KiB each, with a warm OS file cache. The measured schedule balances launch slot and immediate predecessor. The raw CSV includes warmups and every measured sample.

- **First render** is process creation to WPF `ContentRendered` or Avalonia's first requested composition batch completion.
- **Editor ready** is process creation to the enabled editor with the selected note loaded.
- **Editor render** is process creation to the next WPF `CompositionTarget.Rendering` event or Avalonia's editor batch completion. These renderer callbacks occur at different stages. Display scanout and keystroke latency require their own measurements.

The runtime verifier inspects modules after editor rendering in a separate run. ReadyToRun loads `coreclr.dll`. NativeAOT runs with the native SQLite provider and without the separate CoreCLR module. Payload totals count uncompressed publish-directory bytes, excluding PDB, XML, and DBG files.

[UX update results and raw data](results/ux/README.md)

[Reviewed build results for ordinary and file-backed notes](results/reviewed/README.md)
