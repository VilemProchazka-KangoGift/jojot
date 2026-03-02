# Codebase Structure

**Analysis Date:** 2026-03-02

## Directory Layout

```
JoJot/ (repository root)
├── JoJot/                          # Main project directory
│   ├── App.xaml                    # Application resource definitions
│   ├── App.xaml.cs                 # Application lifecycle and initialization
│   ├── MainWindow.xaml             # Main window UI definition
│   ├── MainWindow.xaml.cs          # Main window code-behind
│   ├── AssemblyInfo.cs             # Assembly metadata and theme configuration
│   ├── JoJot.csproj                # Project file (net10.0-windows, WPF)
│   ├── JoJot.csproj.user           # User-specific project settings
│   ├── JoJot.slnx                  # Solution file (new .slnx format)
│   ├── bin/                        # Build output (generated)
│   ├── obj/                        # Build artifacts (generated)
│   └── .vs/                        # Visual Studio local settings (generated)
├── .planning/
│   └── codebase/                   # Documentation (ARCHITECTURE.md, STRUCTURE.md, etc.)
├── .claude/                        # Claude Code configuration
├── .git/                           # Git repository metadata
├── LICENSE                         # License file
└── CLAUDE.md                       # Project guidance for Claude Code
```

## Directory Purposes

**JoJot/:**
- Purpose: Main WPF application project containing all source code and configuration
- Contains: C# source files (.cs), XAML UI definitions (.xaml), project configuration files
- Key files: `App.xaml`, `MainWindow.xaml`, `JoJot.csproj`, `JoJot.slnx`

**JoJot/bin/:**
- Purpose: Build output directory (Debug/Release subdirectories)
- Contains: Compiled assemblies and runtime dependencies
- Generated: Yes
- Committed: No (in .gitignore)

**JoJot/obj/:**
- Purpose: Intermediate build artifacts and temporary build files
- Contains: Object files, metadata, build cache
- Generated: Yes
- Committed: No (in .gitignore)

**.planning/codebase/:**
- Purpose: Architecture and codebase analysis documentation
- Contains: ARCHITECTURE.md, STRUCTURE.md, CONVENTIONS.md, TESTING.md, CONCERNS.md, STACK.md, INTEGRATIONS.md
- Generated: Yes (by GSD tools)
- Committed: Yes

## Key File Locations

**Entry Points:**
- `JoJot/App.xaml`: Application resource and startup configuration entry point
- `JoJot/MainWindow.xaml`: Primary UI window launched by application

**Configuration:**
- `JoJot/JoJot.csproj`: Project settings, target framework (net10.0-windows), WPF configuration
- `JoJot/JoJot.slnx`: Solution file in new .slnx format
- `JoJot/AssemblyInfo.cs`: Assembly-level attributes and WPF theme configuration

**Core Logic:**
- `JoJot/App.xaml.cs`: Application lifecycle, initialization, and event handling
- `JoJot/MainWindow.xaml.cs`: Main window initialization and interaction logic

**Testing:**
- Not yet implemented (no test projects present)

## Naming Conventions

**Files:**
- XAML UI files: PascalCase.xaml (e.g., `MainWindow.xaml`, `App.xaml`)
- Code-behind files: PascalCase.xaml.cs matching XAML file name
- C# source files: PascalCase.cs (e.g., `AssemblyInfo.cs`)
- Project files: PascalCase.csproj
- Solution files: PascalCase.slnx

**Directories:**
- Single root project directory: PascalCase (JoJot/)
- Output directories: lowercase (bin/, obj/)
- Configuration directories: dot-prefixed (`.planning/`, `.claude/`, `.git/`, `.vs/`)

**Namespaces:**
- All code uses namespace `JoJot` matching project name
- Implicit usings enabled, no explicit System.* using statements required

## Where to Add New Code

**New Feature:**
- Primary code: `JoJot/Features/[FeatureName]/` (subdirectory suggested, currently not created)
- UI components: `JoJot/[ComponentName].xaml` and `JoJot/[ComponentName].xaml.cs`
- Logic classes: `JoJot/[ServiceName].cs` or `JoJot/Services/[ServiceName].cs`
- Tests: Create `JoJot.Tests/` project (currently not present)

**New Window/Control:**
- XAML file: `JoJot/[WindowName].xaml`
- Code-behind: `JoJot/[WindowName].xaml.cs` (automatically linked by VS)
- Namespace: `JoJot`

**Utilities/Services:**
- Shared helpers: `JoJot/Utilities/` or `JoJot/Services/` (suggested structure, not yet created)
- Keep in root `JoJot/` directory if few in number, organize into subdirectories as project grows

## Project Settings & Structure

**Target Framework:** net10.0-windows (.NET 10)

**Key Features Enabled:**
- `UseWPF: true` - Enables WPF support
- `Nullable: enable` - Nullable reference types enabled (use `string?` for nullable strings)
- `ImplicitUsings: enable` - No need for explicit `using System;` statements

**Output Type:** WinExe (Windows executable, no console window)

**Startup Configuration:**
- Solution file location: `JoJot/JoJot.slnx` (inside project folder, not at repo root)
- Application entry point: `App.xaml` (StartupUri points to `MainWindow.xaml`)
- Main window: `MainWindow.xaml`

---

*Structure analysis: 2026-03-02*
