# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

JoJot is a WPF desktop application targeting .NET 10 (net10.0-windows), written in C#.

## Build & Run Commands

```bash
# Build
dotnet build JoJot/JoJot.slnx

# Run
dotnet run --project JoJot/JoJot.csproj

# Clean
dotnet clean JoJot/JoJot.slnx
```

The solution file uses the new `.slnx` format. The project entry point is `JoJot/App.xaml` which launches `MainWindow.xaml`.

## Architecture

- **Framework**: WPF with C# (.NET 10)
- **Project structure**: Single-project solution rooted in `JoJot/`
- **Nullable reference types**: Enabled project-wide
- **Implicit usings**: Enabled
- **Namespace**: `JoJot`

All source files and XAML live under `JoJot/`. The `.slnx` solution file is also inside `JoJot/` (not at the repo root).
