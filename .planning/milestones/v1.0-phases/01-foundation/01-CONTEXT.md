# Phase 1: Foundation - Context

**Gathered:** 2026-03-02
**Status:** Ready for planning

<domain>
## Phase Boundary

SQLite data model, single-instance process, named pipe IPC, and startup sequence skeleton. The app can launch as a single instance, enforce the named mutex, accept IPC connections, read/write SQLite with WAL mode, and execute the startup sequence — all before any UI beyond the main window is presented. Requirements: DATA-01 through DATA-07, PROC-01 through PROC-06, STRT-01 through STRT-04.

</domain>

<decisions>
## Implementation Decisions

### Second-instance behavior
- Second instance triggers focus & bring to front of the existing JoJot window for the current desktop
- If the existing window is minimized, restore it to saved geometry and focus it
- Design the full IPC command vocabulary from day one (activate, new-tab, show-desktop-X, etc.) even though only "activate" is implemented in this phase — future-proofs the protocol
- IPC timeout/force-kill recovery is silent: kill zombie, start fresh, log the event, no user-facing dialog or message

### Failure & recovery
- Database corruption: attempt SQLite repair first (integrity_check / recovery tools), then backup corrupt file to jojot.db.corrupt and recreate fresh if repair fails
- Background migration failure: log and continue, retry on next launch — user never sees anything, app functions with current schema
- Error philosophy is tiered: critical failures (can't write to DB at all) show a dialog and exit; minor failures (one migration, one IPC message) are logged silently
- Logging goes to both AppData\Local\JoJot\jojot.log file AND System.Diagnostics.Debug output

### Startup experience
- No splash screen or loading indicator — window appears only when fully ready (startup steps are invisible)
- First-ever launch creates a single "Welcome to JoJot" tab with brief content explaining basics (virtual desktops, keyboard shortcuts) — user can delete it
- Startup timing (duration from launch to window shown) logged to both debug console and log file on every launch
- Quick database integrity check on every launch (verify all expected tables exist) — catches issues before user notices

### Claude's Discretion
- Log file rotation strategy and size limits
- Exact welcome tab content and formatting
- Database integrity check implementation details (pragma vs query approach)
- IPC protocol message format (JSON schema, versioning)
- Startup sequence error handling order and retry logic

</decisions>

<specifics>
## Specific Ideas

- The app should feel unbreakable — degrade gracefully, never show unhandled exception dialogs
- Recovery from zombie processes and corrupt databases should be invisible to the user when possible
- Full IPC vocabulary designed upfront to avoid protocol migrations in later phases
- "Just appears" startup philosophy — confidence over caution (no splash screens or loading states)

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- None — bare WPF skeleton only (App.xaml, MainWindow.xaml, AssemblyInfo.cs)
- Zero external NuGet packages — SQLite package will be the first dependency added

### Established Patterns
- Namespace: `JoJot` (single root namespace)
- PascalCase for classes/methods, camelCase for private fields with underscore prefix
- Nullable reference types enabled — all new code must handle nullability
- Implicit usings enabled — System.* namespaces available without explicit imports
- XAML code-behind uses partial classes

### Integration Points
- App.xaml.cs: Application lifecycle — startup sequence hooks here
- MainWindow.xaml.cs: Main window — will become the target for IPC "activate" command
- JoJot.csproj: Needs SQLite NuGet package reference added

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 01-foundation*
*Context gathered: 2026-03-02*
