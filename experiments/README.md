# JoJot Avalonia NativeAOT experiment

The prototype uses the production SQLite store code against a disposable copy of a schema 2 database. It opens a tab list and an editable note. The WPF release covers substantially more behavior, including virtual desktops, IPC, hotkey registration, recovery, and advanced editor features. The WPF-to-Avalonia comparison measures these two builds as they exist. The Avalonia ReadyToRun-to-NativeAOT comparison holds the application source constant.

## Build

Use PowerShell 7 and the .NET 11 SDK on Windows x64. A normal NativeAOT setup uses Visual Studio 2022 or newer with the Desktop development with C++ workload. [Microsoft NativeAOT prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/index) · [Avalonia NativeAOT guide](https://docs.avaloniaui.net/docs/deployment/native-aot)

From the repository root, publish into two new directories. Use the same SDK for both variants and the WPF probe.

```powershell
$dotnet = 'dotnet'
$project = 'experiments/JoJot.Avalonia/JoJot.Avalonia.csproj'
$root = '.local/avalonia-aot'
& $dotnet publish $project -c Release -r win-x64 --self-contained -p:PublishAot=false -p:PublishReadyToRun=true -o "$root/managed"
& $dotnet publish $project -c Release -r win-x64 --self-contained -p:PublishAot=true -p:PublishReadyToRun=false -o "$root/native"
& experiments/tools/Prepare-WpfBaseline.ps1 -Dotnet $dotnet
```

The preparation script prints the WPF run path. It exports the WPF source from tag `v2026.11.2`, instruments a copy with the repository's startup probe, and seeds 100 notes of 1 KiB each. All instrumented WPF runs require a fixture path, use per-process mutex and IPC names, suppress foreground activation and the global hotkey, and exit automatically after the render milestones. The WPF publish is a loose self-contained ReadyToRun folder from the release source. Its payload size excludes the installer and installer compression.

On the measurement workstation the Visual Studio installation had the x64 linker and OneCore CRT libraries, while the desktop CRT path was absent. The NativeAOT build used the following process-local environment with `-p:IlcUseEnvironmentalTools=true`. This is specific to that installed toolchain.

```powershell
$msvc = 'C:/Program Files/Microsoft Visual Studio/18/Community/VC/Tools/MSVC/14.51.36231'
$sdk = 'C:/Program Files (x86)/Windows Kits/10/Lib/10.0.19041.0'
$env:Path = "$msvc/bin/Hostx64/x64;$env:Path"
$env:LIB = "$msvc/lib/onecore/x64;$sdk/ucrt/x64;$sdk/um/x64"
```

## Try the prototype

Set `$wpfRun` to the WPF run path and `$aotExe` to the published NativeAOT executable. The launcher creates a new copy of the fixture on every run and prints its location. Edit a note, switch tabs, close the window, then reuse the printed directory with the executable's `--data-dir` and `--desktop-guid` options to verify persistence.

```powershell
& experiments/tools/Try-Avalonia.ps1 -Executable $aotExe -WpfRun $wpfRun
```

The installed app's notes live in a different directory. The benchmark and try scripts only pass synthetic fixture copies. The prototype's custom data path checks are safeguards against accidental misuse. Use a fresh copy for experiments.

The prototype supports creating, editing, renaming, pinning, searching, and permanently deleting notes, with debounced autosave and light/dark/system themes. It uses one selected desktop session. Virtual desktop integration, IPC, global hotkeys, recovery, soft-delete undo, file-backed saving, and full localization are not implemented here.

## Validation

Three storage integration cases passed against the existing EF-created database fixture and schema versions 0–2. Ten Avalonia headless cases passed, covering real SQLite persistence on debounce, tab switching and immediate close, rename/pin/search/delete, theme persistence, queued startup close, and protected paths. These tests run managed code; the separate native startup runs verify the published AOT executable.

```powershell
& $dotnet test experiments/JoJot.Storage.Tests/JoJot.Storage.Tests.csproj -c Release
& $dotnet build experiments/JoJot.Avalonia.Tests/JoJot.Avalonia.Tests.csproj -c Release -m:1
& $dotnet experiments/JoJot.Avalonia.Tests/bin/Release/net11.0/win-x64/JoJot.Avalonia.Tests.dll -xml "$root/headless-tests.xml"
```

The direct xUnit runner was used because the VSTest host connection timed out in this environment. Interactive desktop inspection was attempted, but Computer Use app approval timed out before a screenshot was returned. Visual appearance and native keyboard/editing behavior remain manual checks; headless tests and successful native render milestones do not establish those results.

## Measure

Run on the same Windows desktop and display used to seed the fixture. At 150% scale the calibrated Avalonia client size is 552 by 382.666667 device-independent pixels. On another display, run with -Samples 1 -Warmups 0 and divide the WPF client dimensions in the resulting CSV by the display scale to obtain the Avalonia dimensions. The comparison checks the Win32 window and client rectangles across all builds. The scripts preserve the intermediate samples if a geometry check fails. Wait for builds and other intensive work to finish before collecting timings.

```powershell
& experiments/tools/Verify-AvaloniaRuntime.ps1 -ManagedExe $managedExe -AotExe $aotExe -WpfRun $wpfRun -OutputDirectory "$root/runtime-proof"
& experiments/tools/Compare-AvaloniaStartup.ps1 -WpfRun $wpfRun -ManagedExe $managedExe -AotExe $aotExe -OutputDirectory "$root/measurement" -Width $width -Height $height
```

Each measured sample uses a fresh process and a fresh copy of the same seeded files, with a warm OS file cache. Three warmup rounds precede 20 measured rounds by default. Each round launches the three variants in a seeded permutation. Every complete block of six rounds uses every permutation once. The CSV includes the warmups and all measured samples.

- **First render** measures process creation to WPF `ContentRendered` or completion of Avalonia's first requested composition batch.
- **Editor ready** measures process creation to the enabled editor with the selected note loaded.
- **Editor render** measures process creation to WPF's next `CompositionTarget.Rendering` event or Avalonia's editor composition batch completion. These are framework-specific rendering callbacks. Display scanout and keystroke latency require separate measurements.

The runtime proof observes loaded process modules after the editor renders. ReadyToRun loads `coreclr.dll`, while the NativeAOT build is expected to run without that module. Both builds load the native SQLite provider. Payload totals are uncompressed publish directory bytes, excluding PDB, XML, and DBG files.



[Measured results and raw data](results/README.md)
