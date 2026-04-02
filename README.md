<div align="center">
  <img src="resources/icon-jj-3.png" width="80" alt="JoJot" />
  <h1>JoJot</h1>
  <p><strong>Notes that follow your virtual desktops.</strong><br>
  Switch desktops, switch context — your notes are already there.</p>
</div>

---

<!-- Screenshot: Add hero screenshot here -->

## Features

- **Virtual desktop notes** — each virtual desktop gets its own set of notes. Switch desktops and your notes switch with you. No other notepad does this.
- **Tabbed editing** — multiple notes per window. Pin important tabs, drag to reorder, close what you don't need.
- **No save, no naming** — just type. JoJot never asks you to save a file or name a note. Everything is automatic.
- **Auto-save** — everything persists the moment you stop typing. Nothing is ever lost.
- **Super fast, no bloat** — instant startup, minimal footprint. It's a notepad, not a platform.
- **Easy cleanup** — when notes outlive their purpose, cleaning them up takes seconds.

## Download

[Download the latest release](https://github.com/vproc/JoJot/releases) — Windows 10/11, no install required.

## Build from Source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download) and Windows 10/11.

```bash
dotnet build JoJot/JoJot.slnx        # Build
dotnet run --project JoJot/JoJot.csproj  # Run
dotnet test JoJot.Tests/JoJot.Tests.csproj  # Test
```

To create a self-contained executable:

```bash
dotnet publish JoJot/JoJot.csproj -c Release -r win-x64 --self-contained
```

## Architecture

JoJot is a WPF application (.NET 10) using hand-rolled MVVM, SQLite for local storage, and undocumented Windows COM APIs for virtual desktop integration. Each virtual desktop gets its own window instance with a separate note session. The app enforces single-instance via a global mutex and named-pipe IPC.

See [`CLAUDE.md`](CLAUDE.md) for full architectural details, service descriptions, and coding conventions.

### Project Structure

```
JoJot/                  # WPF application
  Controls/             # Reusable UI controls (panels, overlays)
  Data/                 # EF Core database context
  Interop/              # Windows COM interop for virtual desktops
  Models/               # Domain models (NoteTab, AppState, etc.)
  Services/             # Static services (database, IPC, theming, undo, etc.)
  ViewModels/           # MVVM view models
  Views/                # MainWindow and partial code-behind files
  Themes/               # Light and Dark resource dictionaries
JoJot.Tests/            # xUnit test project
installer/              # Inno Setup installer script
```

## Contributing

Contributions are welcome. Please open an issue first to discuss what you'd like to change. See [`CLAUDE.md`](CLAUDE.md) for build commands, architecture, and coding conventions.

## Tech

.NET 10 &middot; WPF &middot; SQLite &middot; Serilog &middot; CalVer

---

[MIT License](LICENSE)
