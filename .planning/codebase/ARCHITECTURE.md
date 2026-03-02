# Architecture

**Analysis Date:** 2026-03-02

## Pattern Overview

**Overall:** WPF Desktop Application (Windows Presentation Foundation)

**Key Characteristics:**
- Single-project C# solution using .NET 10
- XAML-based declarative UI with code-behind model
- Minimal current architecture (application entry point and main window established, core logic not yet implemented)
- Nullable reference types enabled project-wide
- Implicit using statements enabled for clean code

## Layers

**Presentation Layer:**
- Purpose: Render UI and handle user interactions
- Location: `JoJot/MainWindow.xaml`, `JoJot/MainWindow.xaml.cs`
- Contains: XAML markup defining window layout, code-behind for event handlers and UI logic
- Depends on: System.Windows (WPF framework)
- Used by: Application entry point

**Application Layer:**
- Purpose: Initialize and manage application lifecycle
- Location: `JoJot/App.xaml`, `JoJot/App.xaml.cs`
- Contains: Application resource definitions and startup configuration
- Depends on: System.Windows
- Used by: .NET runtime at application startup

**Assembly Configuration:**
- Purpose: Configure theme resources and WPF assembly metadata
- Location: `JoJot/AssemblyInfo.cs`
- Contains: Theme resource dictionary location attributes
- Depends on: System.Windows
- Used by: WPF framework during resource resolution

## Data Flow

**Application Startup:**

1. .NET 10 runtime launches the application (OutputType: WinExe)
2. `App.xaml` loads and designates `MainWindow.xaml` as the startup URI
3. `App.xaml.cs` (App class) initializes and inherits from Application
4. `MainWindow.xaml` is instantiated with its Grid-based layout
5. `MainWindow.xaml.cs` (MainWindow class) initializes component via `InitializeComponent()`
6. Main window displays to user

**State Management:**
- Currently minimal state management
- Application state would be managed at the code-behind level in `App.xaml.cs`
- Window-level state managed in `MainWindow.xaml.cs`
- No centralized state container or data binding infrastructure yet configured

## Key Abstractions

**Application Class:**
- Purpose: Represents the WPF application singleton
- Examples: `JoJot/App.xaml.cs`
- Pattern: Inherits from `System.Windows.Application`, partial class with XAML codebehind

**Window Class:**
- Purpose: Represents a top-level window container
- Examples: `JoJot/MainWindow.xaml.cs`
- Pattern: Inherits from `System.Windows.Window`, XAML-defined with code-behind for logic

**XAML Markup:**
- Purpose: Declaratively define UI structure and resources
- Examples: `JoJot/App.xaml`, `JoJot/MainWindow.xaml`
- Pattern: XML-based markup with code-behind partial classes

## Entry Points

**Application Entry:**
- Location: `JoJot/App.xaml`, `JoJot/App.xaml.cs`
- Triggers: Application startup by .NET 10 runtime
- Responsibilities: Initialize WPF application, load resources, set startup window to `MainWindow.xaml`

**Main Window:**
- Location: `JoJot/MainWindow.xaml`, `JoJot/MainWindow.xaml.cs`
- Triggers: Application.StartupUri reference in `App.xaml`
- Responsibilities: Render main UI surface, initialize UI components, handle window-level events

## Error Handling

**Strategy:** Default WPF exception handling

**Patterns:**
- No custom error handling currently implemented
- Uses default Application-level exception handling through inherited Application class
- Should implement error handling in `App.xaml.cs` via `Application_Startup` or `Application_DispatcherUnhandledException` events as needed

## Cross-Cutting Concerns

**Logging:** Not implemented

**Validation:** Not implemented

**Authentication:** Not applicable (desktop application)

---

*Architecture analysis: 2026-03-02*
