---
phase: 14-installer
verified: 2026-03-10T10:15:00Z
status: human_needed
score: 6/9 must-haves verified
re_verification: false
human_verification:
  - test: "Run installer and verify minimal wizard flow"
    expected: "Welcome -> Progress -> Finish (no license, no path picker, no component selection). Auto-start checkbox unchecked by default. Launch JoJot checkbox checked by default."
    why_human: "Requires running the actual installer executable and observing the GUI wizard"
  - test: "Verify installed app launches and works"
    expected: "JoJot launches from C:\\Program Files\\JoJot\\, tabs/text/themes all work, custom icon visible in taskbar and Alt+Tab, Start Menu shortcut exists, no desktop shortcut"
    why_human: "Requires running the installed application and visually verifying behavior"
  - test: "Verify EXE metadata in file properties"
    expected: "Right-click JoJot.exe -> Properties -> Details: Product version 2026.3.0, Company Vilem Prochazka, Product JoJot, Description Per-virtual-desktop notepad for Windows"
    why_human: "Requires inspecting Windows file properties dialog on the installed EXE"
  - test: "Verify uninstall flow with data deletion prompt"
    expected: "Uninstaller prompts 'Delete your JoJot data?' with default No. Choosing No removes program files but preserves %LocalAppData%\\JoJot\\. Choosing Yes removes both."
    why_human: "Requires running the uninstaller and observing the prompt behavior"
  - test: "Verify upgrade scenario (force-close)"
    expected: "Running installer while JoJot is open force-closes the app and installs over existing files. Notes preserved after upgrade."
    why_human: "Requires multi-step manual test with running application"
---

# Phase 14: Installer Verification Report

**Phase Goal:** JoJot can be installed on a clean Windows machine via a standard installer package
**Verified:** 2026-03-10T10:15:00Z
**Status:** human_needed
**Re-verification:** No -- initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | The .csproj contains CalVer version 2026.3.0, publisher Vilem Prochazka, and ApplicationIcon pointing to Assets/jojot.ico | VERIFIED | All 9 metadata fields present: Version=2026.3.0, AssemblyVersion=2026.3.0.0, FileVersion=2026.3.0.0, Company, Authors, Product, Description, Copyright, ApplicationIcon |
| 2 | A multi-resolution .ico file exists at JoJot/Assets/jojot.ico containing at least 16x16, 32x32, 48x48, and 256x256 sizes | VERIFIED | File exists (4.2KB), valid ICO header (reserved=0, type=1, count=4), all 4 resolutions present: 16x16, 32x32, 48x48, 256x256 |
| 3 | An Inno Setup script exists at installer/jojot.iss that defines a minimal Welcome-Progress-Finish wizard installer | VERIFIED | 78-line script with all 7 required sections. DisableDirPage=yes, DisableReadyPage=yes, DisableProgramGroupPage=yes produce minimal wizard |
| 4 | The .iss script references the dotnet publish output for self-contained win-x64 | VERIFIED | Line 45: `Source: "..\JoJot\bin\Release\net10.0-windows\win-x64\publish\*"` |
| 5 | The .iss script includes uninstall data-deletion prompt, upgrade force-close, auto-start checkbox, and Launch JoJot finish checkbox | VERIFIED | All four features present: CurUninstallStepChanged with MsgBox (line 61-77), CloseApplications=force (line 38), autostart task unchecked (line 52), postinstall run (line 58) |
| 6 | A JoJot-2026.3.0-Setup.exe installer file exists in installer/output/ | VERIFIED | File exists, 57MB, reasonable size for self-contained .NET 10 WPF app |
| 7 | The installer runs on the dev machine, installs JoJot to Program Files, and creates a Start Menu shortcut | ? NEEDS HUMAN | Cannot run installer programmatically; script defines {autopf}\JoJot and {group} shortcuts correctly |
| 8 | The installed JoJot.exe launches and all v1.1 behaviors are intact | ? NEEDS HUMAN | Cannot launch installed app programmatically; self-contained publish includes runtime |
| 9 | The uninstaller prompts about data deletion and removes program files | ? NEEDS HUMAN | Cannot run uninstaller programmatically; Pascal script code is syntactically correct with MB_DEFBUTTON2 |

**Score:** 6/9 truths verified (3 require human testing)

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `JoJot/JoJot.csproj` | Version metadata, publisher, icon reference | VERIFIED | All 9 properties present with correct values, build succeeds with 0 warnings |
| `JoJot/Assets/jojot.ico` | Multi-resolution application icon | VERIFIED | Valid ICO, 4.2KB, 4 sizes (16/32/48/256px), 32-bit color depth |
| `installer/jojot.iss` | Inno Setup installer script | VERIFIED | 78 lines, 7 sections, all user-specified behaviors implemented |
| `installer/output/JoJot-2026.3.0-Setup.exe` | Windows installer executable | VERIFIED | 57MB file exists |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `JoJot/JoJot.csproj` | `JoJot/Assets/jojot.ico` | `<ApplicationIcon>` property | WIRED | Line 25: `<ApplicationIcon>Assets\jojot.ico</ApplicationIcon>`, file exists at target path |
| `installer/jojot.iss` | `JoJot/bin/.../publish/` | `[Files] Source` directive | WIRED | Line 45: Source references `publish\*` with recursesubdirs flag |
| `installer/jojot.iss` | `installer/output/JoJot-2026.3.0-Setup.exe` | ISCC compiler | WIRED | OutputBaseFilename=JoJot-2026.3.0-Setup (line 30), output file exists at 57MB |
| `installer/jojot.iss` | `JoJot/Assets/jojot.ico` | SetupIconFile | WIRED | Line 31: `SetupIconFile=..\JoJot\Assets\jojot.ico` |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| DIST-01 | 14-01, 14-02 | Windows installer (MSI or MSIX) support | SATISFIED | Inno Setup .exe installer produced (functionally equivalent to MSI). Script authored (14-01), compiled to 57MB setup executable (14-02). All installer features per context decisions implemented. |

No orphaned requirements. DIST-01 is the only requirement mapped to Phase 14 in REQUIREMENTS.md, and it appears in both plan frontmatters.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| (none) | - | - | - | No anti-patterns found in any modified files |

No TODOs, FIXMEs, placeholders, empty implementations, or stub code found in any phase artifacts.

### Build and Test Verification

| Check | Result |
|-------|--------|
| `dotnet build JoJot/JoJot.slnx` | Build succeeded, 0 warnings, 0 errors |
| `dotnet test JoJot.Tests/JoJot.Tests.csproj` | 1029 passed, 0 failed, 0 skipped |
| `.gitignore` includes `installer/output/` | Yes (line 11) |
| `.gitignore` includes `bin/` and `obj/` | Yes (lines 7, 8) |

### Git Commits

All commits referenced in summaries are present in git log:

| Commit | Message | Plan |
|--------|---------|------|
| `14e5f06` | feat(14-01): add CalVer metadata, publisher info, and app icon | 14-01 Task 1 |
| `7459305` | feat(14-01): add Inno Setup installer script | 14-01 Task 2 |
| `37d2191` | chore(14-02): build installer and fix Inno Setup compatibility | 14-02 Task 1 |

### Human Verification Required

### 1. Installer Wizard Flow

**Test:** Double-click `installer/output/JoJot-2026.3.0-Setup.exe`. Observe the wizard pages.
**Expected:** Welcome page -> Progress bar -> Finish page. No license page, no directory picker, no component selection. "Launch JoJot when Windows starts" checkbox appears and is UNCHECKED. Finish page has "Launch JoJot" checkbox CHECKED by default.
**Why human:** Requires running the actual installer GUI and observing each wizard page.

### 2. Installed Application Functionality

**Test:** After install, launch JoJot from the Finish page or Start Menu. Create tabs, type text, switch themes.
**Expected:** App launches from `C:\Program Files\JoJot\`. All tab, editor, theme, and virtual desktop features work normally. Custom blue "J" icon visible in taskbar and Alt+Tab. Start Menu has "JoJot" shortcut. No desktop shortcut exists.
**Why human:** Requires running the installed application and visually verifying full functionality.

### 3. EXE File Properties

**Test:** Right-click `C:\Program Files\JoJot\JoJot.exe` -> Properties -> Details tab.
**Expected:** Product version: 2026.3.0. Company: Vilem Prochazka. Product name: JoJot. File description: Per-virtual-desktop notepad for Windows.
**Why human:** Requires inspecting the Windows file properties dialog.

### 4. Uninstall with Data Deletion Prompt

**Test:** Uninstall via Settings -> Apps or Start Menu uninstaller.
**Expected:** Prompt appears: "Delete your JoJot data (notes and preferences)?" with default button on No. Choosing No removes program files but preserves `%LocalAppData%\JoJot\`. Choosing Yes removes both.
**Why human:** Requires running the uninstaller and testing both prompt outcomes.

### 5. Upgrade with Force-Close

**Test:** Install, launch JoJot, create a note. Run the installer again while JoJot is running.
**Expected:** JoJot is force-closed automatically. Installation proceeds. After upgrade, launch JoJot -- previously created note is preserved.
**Why human:** Requires multi-step manual interaction with both running app and installer.

### Gaps Summary

No automated verification gaps were found. All source artifacts (csproj metadata, icon file, Inno Setup script) are complete and correctly wired. The 57MB installer executable was successfully compiled. Build and all 1029 tests pass.

Three truths require human verification because they involve running the actual installer, observing the GUI wizard, and testing the installed application's behavior. These cannot be verified programmatically. The automated checks confirm all prerequisites are in place for these human tests to succeed.

---

_Verified: 2026-03-10T10:15:00Z_
_Verifier: Claude (gsd-verifier)_
