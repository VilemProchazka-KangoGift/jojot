# Technology Stack

**Analysis Date:** 2026-03-02

## Languages

**Primary:**
- C# - Full application implementation

## Runtime

**Environment:**
- .NET 10.0 (net10.0-windows)
- Windows Desktop runtime (WPF support)

**SDK:**
- .NET SDK 10.0.103

## Frameworks

**Core:**
- WPF (Windows Presentation Foundation) - Desktop UI framework for Windows applications

**UI:**
- XAML - Markup language for UI definitions (`App.xaml`, `MainWindow.xaml`)

## Key Dependencies

**Framework Packages:**
- Microsoft.NETCore.App - Core .NET runtime
- Microsoft.WindowsDesktop.App.WPF - WPF runtime support

**No external NuGet packages** - Project has zero external dependencies (clean `project.assets.json`)

## Configuration

**Build Configuration:**
- Project file: `JoJot/JoJot.csproj`
- Solution file: `JoJot/JoJot.slnx` (new format)
- Output type: WinExe (Windows executable)

**Language Features:**
- Nullable reference types: Enabled
- Implicit usings: Enabled

**Platform:**
- Target platform: Windows 7.0 and above
- Architecture: Windows desktop application

## Build & Run

**Build:**
```bash
dotnet build JoJot/JoJot.slnx
```

**Run:**
```bash
dotnet run --project JoJot/JoJot.csproj
```

**Clean:**
```bash
dotnet clean JoJot/JoJot.slnx
```

## Project Structure

**Entry Points:**
- `JoJot/App.xaml` - Application entry point
- `JoJot/MainWindow.xaml` - Main window UI definition

**Source Files:**
- `JoJot/App.xaml.cs` - Application code-behind
- `JoJot/MainWindow.xaml.cs` - Main window code-behind
- `JoJot/AssemblyInfo.cs` - Assembly metadata and theme configuration

## Platform Requirements

**Development:**
- Windows operating system (WPF is Windows-only)
- .NET 10 SDK
- Visual Studio or compatible IDE (supports new `.slnx` format)

**Production:**
- Windows 7.0 or higher
- .NET 10 runtime installed

---

*Stack analysis: 2026-03-02*
