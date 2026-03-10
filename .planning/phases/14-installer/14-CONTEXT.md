# Phase 14: Installer - Context

**Gathered:** 2026-03-10
**Status:** Ready for planning

<domain>
## Phase Boundary

Create a Windows installer package (Inno Setup) so JoJot can be installed on a clean machine without manual file placement. Self-contained (.NET 10 runtime bundled). Post-install, all v1.1 behaviors work. No auto-update mechanism — manual download from GitHub releases.

</domain>

<decisions>
## Implementation Decisions

### Installer Technology
- Inno Setup — produces a single .exe installer
- Build flow: `dotnet publish` first (self-contained, win-x64), then compile .iss script with ISCC.exe as a separate step
- No MSBuild integration — two discrete commands
- No auto-update — manual download of new installer from GitHub releases

### Install Experience
- Minimal wizard: Welcome → Progress → Finish (no license page, no path picker, no component selection)
- Default install path: `C:\Program Files\JoJot\`
- Start Menu shortcut only (no desktop shortcut)
- Silent install supported via Inno Setup's built-in /SILENT and /VERYSILENT flags
- Finish page: "Launch JoJot" checkbox, checked by default

### Uninstall Behavior
- Uninstaller prompts: "Delete your JoJot data?" with default No
- If user declines, program files removed but `%LocalAppData%\JoJot\` preserved
- If user accepts, both program files and user data removed

### Upgrade Behavior
- In-place upgrade: detect existing install via AppId, overwrite files
- Force-close running JoJot silently during upgrade (auto-save protects data)
- User data always preserved during upgrades

### Branding & Metadata
- Create a simple app icon (.ico) — minimal design (notepad/pen motif or 'J' lettermark), multiple sizes (16/32/48/256px)
- Icon used for: EXE, installer, Start Menu shortcut, taskbar
- Version numbering: CalVer — `2026.3.0` (Year.Month.Build)
- Publisher name: "Vilém Procházka"
- EXE embedded metadata: AssemblyVersion, FileVersion, Company ("Vilém Procházka"), Product ("JoJot"), Description in .csproj

### Auto-start & Shell Integration
- Optional auto-start: installer checkbox "Launch JoJot when Windows starts" (default off), adds registry Run key
- No file associations — JoJot is a notepad app with database storage, not a .txt editor
- No PATH registration, no shell extensions, no context menu entries

### Claude's Discretion
- Icon design details (style, colors — should match app theme)
- Exact Inno Setup script structure and options
- How to implement the "delete user data" uninstall prompt
- Registry key details for auto-start

</decisions>

<specifics>
## Specific Ideas

- CalVer versioning (2026.3.0) chosen over SemVer — version reflects release date
- Publisher "Vilém Procházka" for both installer and EXE metadata
- Force-close (not prompt) during upgrades — auto-save at 500ms debounce means data loss risk is negligible

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `PublishReadyToRun` already enabled in .csproj — publish output is optimized
- Self-contained publish command already documented: `dotnet publish JoJot/JoJot.csproj -c Release -r win-x64 --self-contained`
- LICENSE file exists at repo root

### Established Patterns
- Data stored at `%LocalAppData%\JoJot\jojot.db` (SQLite WAL mode) — installer must not touch this location
- Single-instance via global mutex (`Global\JoJot_SingleInstance`) — installer's force-close needs to account for this
- Global hotkey `Win+Shift+N` via `RegisterHotKey` — works post-install without special registration

### Integration Points
- .csproj needs: ApplicationIcon, Version, Company, Product, Description, FileVersion properties
- New files needed: app icon (.ico), Inno Setup script (.iss)
- Auto-start registry key: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

### Missing Prerequisites
- No .ico file exists — must be created
- No version metadata in .csproj — must be added
- No AssemblyInfo version attributes — need CalVer scheme (2026.3.0)

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 14-installer*
*Context gathered: 2026-03-10*
