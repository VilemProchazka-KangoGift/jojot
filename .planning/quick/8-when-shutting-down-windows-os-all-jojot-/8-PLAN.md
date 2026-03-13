---
phase: quick-08
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/App.xaml.cs
autonomous: true
requirements: [QUICK-08]

must_haves:
  truths:
    - "When Windows shuts down or user logs off, JoJot exits gracefully without blocking the OS shutdown"
    - "All unsaved tab content is flushed to the database before the process terminates"
    - "Window geometry is saved during the session ending handler"
  artifacts:
    - path: "JoJot/App.xaml.cs"
      provides: "SessionEnding event handler for graceful OS shutdown"
      contains: "SessionEnding"
  key_links:
    - from: "App.SessionEnding handler"
      to: "MainWindow.FlushAndClose()"
      via: "iterates _windows and calls FlushAndClose on each"
      pattern: "FlushAndClose"
---

<objective>
Handle Windows OS shutdown/logoff so JoJot exits gracefully without blocking it.

Purpose: Currently, JoJot uses `ShutdownMode.OnExplicitShutdown` which means the WPF application does NOT automatically shut down when Windows sends session-ending signals (`WM_QUERYENDSESSION`). The `Application.SessionEnding` event is not handled, so JoJot can block OS shutdown -- Windows shows the "this app is preventing shutdown" dialog. The fix is to handle `SessionEnding` to flush all windows and call `Application.Shutdown()`.

Output: Modified `App.xaml.cs` with a `SessionEnding` event handler.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@JoJot/App.xaml.cs
@JoJot/Views/MainWindow.xaml.cs (OnClosing and FlushAndClose methods)
@JoJot/Views/MainWindow.HamburgerMenu.cs (ExitApplication reference pattern)
</context>

<tasks>

<task type="auto">
  <name>Task 1: Handle Application.SessionEnding for graceful OS shutdown</name>
  <files>JoJot/App.xaml.cs</files>
  <action>
In `App.xaml.cs`, subscribe to the `SessionEnding` event early in `OnAppStartup` (right after setting `ShutdownMode = ShutdownMode.OnExplicitShutdown` at line 105). Add a handler that:

1. Subscribes: `SessionEnding += OnSessionEnding;`

2. Creates a new method `OnSessionEnding(object sender, SessionEndingCancelEventArgs e)`:
   - Log: `LogService.Info("Windows session ending (reason: {Reason}) -- flushing and shutting down...", e.ReasonSessionEnding)`
   - Do NOT set `e.Cancel = true` -- allow Windows to proceed with shutdown
   - Iterate over a snapshot of `_windows.Values` (copy to list first since FlushAndClose modifies the dictionary via the Closed event handler)
   - Call `FlushAndClose()` on each window (this stops autosave, flushes content, commits pending deletions, saves geometry, and closes the window)
   - Call `Shutdown()` on the Application to trigger `OnExit` which handles IPC stop, hotkey shutdown, VirtualDesktop shutdown, ThemeService shutdown, database close, mutex release, and log flush

Important: Use the same pattern as `ExitApplication` in `MainWindow.HamburgerMenu.cs` but call `Shutdown()` instead of `Environment.Exit(0)`. The `Shutdown()` method is preferred here because it triggers the `OnExit` override cleanly, whereas `Environment.Exit(0)` would bypass it. The existing `ExitApplication` uses `Environment.Exit(0)` which works because it is a user-initiated action, but for OS session ending, we want the cleanest possible shutdown path via `OnExit`.

Do NOT use async/await in this handler -- `SessionEnding` is synchronous and the OS expects a prompt response. The `FlushAndClose()` method already handles its work synchronously (it calls `.GetAwaiter().GetResult()` for async DB operations).
  </action>
  <verify>
    <automated>dotnet build JoJot/JoJot.slnx</automated>
  </verify>
  <done>
    - `SessionEnding` event is subscribed in `OnAppStartup`
    - Handler flushes all open windows via `FlushAndClose()` and calls `Shutdown()`
    - Build succeeds with no errors or warnings
    - OS shutdown/logoff will no longer be blocked by JoJot
  </done>
</task>

</tasks>

<verification>
- `dotnet build JoJot/JoJot.slnx` compiles without errors
- `dotnet test JoJot.Tests/JoJot.Tests.csproj` passes (no regressions)
- Code review: `SessionEnding` handler does not set `e.Cancel = true`
- Code review: `FlushAndClose()` is called on each window before `Shutdown()`
</verification>

<success_criteria>
- JoJot handles `Application.SessionEnding` to flush all windows and shut down gracefully
- Windows OS shutdown/logoff is not blocked by JoJot
- All unsaved content is persisted before the process exits
- Existing tests pass with no regressions
</success_criteria>

<output>
After completion, create `.planning/quick/8-when-shutting-down-windows-os-all-jojot-/8-SUMMARY.md`
</output>
