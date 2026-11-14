# Run JoJot on Linux and macOS

JoJot's Avalonia frontend can be built from the repository root with the .NET 11 SDK. The Windows production installer remains separate.

## Linux and WSL

On desktop Linux, run this in your normal X11 or XWayland session. In WSL, use a WSLg-enabled distribution or set DISPLAY to an X server you control.

```bash
dotnet run --project experiments/JoJot.Avalonia -c Release -p:PublishAot=false
```

The preview writes to `$XDG_DATA_HOME/JoJot.AvaloniaExperiment` when XDG_DATA_HOME is absolute. Otherwise it writes to `~/.local/share/JoJot.AvaloniaExperiment`. For isolated testing use `-- --data-dir /path/to/a/disposable/directory`. Launching again with the same data directory requests a new note in the existing process. Use the application's Exit command to end the resident process.

A native Linux x64 build requires the .NET NativeAOT system toolchain and the normal Avalonia X11 prerequisites.

```bash
dotnet publish experiments/JoJot.Avalonia -c Release -r linux-x64 -p:PublishAot=true -o .local/jojot-linux
./.local/jojot-linux/JoJot.Avalonia
```

The local Windows workspace launcher is [Try-JoJot-Kali.cmd](../.local/linux-dotnet/Try-JoJot-Kali.cmd). It starts the WSL build with its normal user data path. Keep the Linux executable and its shared libraries together when copying the build to another directory.

To repeat headless verification, build and run the application test executable, then run the storage suite.

```bash
dotnet build experiments/JoJot.Avalonia.Tests -c Release -p:PublishAot=false
dotnet experiments/JoJot.Avalonia.Tests/bin/Release/net11.0/JoJot.Avalonia.Tests.dll
dotnet test experiments/JoJot.Storage.Tests -c Release
```

The two Windows Shell tests are skipped on Linux. The X11 smoke uses a private Xvfb display, starts an editor with disposable notes, launches a second process to verify new-tab IPC, and confirms a clean shutdown.

## macOS

JoJot keeps one note session across macOS Spaces. AppKit reports when the active Space changes, but the notification carries no stable Space identifier. The application keeps its database keys independent of these notifications. [Apple's active Space notification](https://developer.apple.com/documentation/appkit/nsworkspace/activespacedidchangenotification)

On a Mac, verify that changing Spaces leaves the same notes available, closing and reopening preserves edits, and a second launch targets the running instance. Check shortcut recording, duplicate shortcut rejection, and activation from a separate app. Test the Command and Option modifiers and the keyboard layout you use. The shortcut currently maps common keys to ANSI positions.

Run the same managed command with a .NET 11 SDK that matches the Mac's architecture. A framework-dependent ARM64 publish from Linux compiled successfully. The preview stores its data in `~/Library/Application Support/JoJot.AvaloniaExperiment`. Installing, signing, and testing a macOS app bundle require a Mac.

## Platform scope

Linux uses separate note sessions for X11 window managers that expose EWMH workspace properties. Sessions follow workspace positions, so reordering workspaces may change which set of notes appears at a position. The first workspace uses the existing default session. On a display without workspace properties, JoJot uses that default session. Opening JoJot or invoking its global shortcut targets the current workspace. Moving a window between workspaces leaves its notes in their original session. The workspace routing was exercised with Openbox under private Xvfb.

Global shortcuts on Linux use X11 key grabs. A compositor or another application can reserve the requested key combination. Native Wayland system-wide shortcuts are outside this implementation. macOS uses a single default session. Both platforms provide best-effort window placement. Windows desktop moves, Task View integration, and taskbar relaunch properties remain Windows-specific.
