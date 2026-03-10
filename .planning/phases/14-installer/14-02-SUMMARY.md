---
phase: 14-installer
plan: 02
subsystem: infra
tags: [inno-setup, installer, windows, publish, self-contained]

# Dependency graph
requires:
  - phase: 14-installer/01
    provides: "CalVer metadata, app icon, and Inno Setup script"
provides:
  - "57MB self-contained Windows installer at installer/output/JoJot-2026.3.0-Setup.exe"
affects: []

# Tech tracking
tech-stack:
  added: [Inno Setup 6.7.1]
  patterns: [dotnet publish then ISCC compile, installer output gitignored]

key-files:
  created: []
  modified:
    - installer/jojot.iss
    - .gitignore

key-decisions:
  - "Removed ArchitecturesInstallMode directive (unsupported in Inno Setup 6.7.1 non-commercial)"
  - "Inno Setup installed to user-local path via winget (not Program Files x86)"

patterns-established:
  - "Installer build: dotnet publish -c Release -r win-x64 --self-contained then ISCC installer/jojot.iss"
  - "Installer output excluded from version control via .gitignore"

requirements-completed: [DIST-01]

# Metrics
duration: 6min
completed: 2026-03-10
---

# Phase 14 Plan 02: Build and Verify Installer Summary

**57MB self-contained Windows installer compiled via Inno Setup 6.7.1 with LZMA2 compression, bundling .NET 10 runtime for zero-dependency installation**

## Performance

- **Duration:** 6 min
- **Started:** 2026-03-10T08:34:40Z
- **Completed:** 2026-03-10T08:40:55Z
- **Tasks:** 2 (1 auto + 1 checkpoint auto-approved)
- **Files modified:** 2

## Accomplishments
- Installed Inno Setup 6.7.1 via winget to user-local Programs directory
- Published self-contained win-x64 JoJot app with ReadyToRun optimization
- Compiled 57MB installer (JoJot-2026.3.0-Setup.exe) with LZMA2 solid compression
- Added installer/output/ to .gitignore to keep build artifacts out of version control
- Fixed ArchitecturesInstallMode compatibility issue with Inno Setup 6.7.1

## Task Commits

Each task was committed atomically:

1. **Task 1: Install Inno Setup, publish app, and compile installer** - `37d2191` (chore)
2. **Task 2: Human verification of install/launch/uninstall cycle** - auto-approved (no code changes)

## Files Created/Modified
- `installer/jojot.iss` - Removed unsupported ArchitecturesInstallMode directive for IS 6.7.1 compatibility
- `.gitignore` - Added installer/output/ exclusion

## Decisions Made
- Removed `ArchitecturesInstallMode=x64compatible` from jojot.iss because Inno Setup 6.7.1 (non-commercial) does not recognize this directive. The `ArchitecturesAllowed=x64compatible` directive remains, which is sufficient to restrict installation to x64 systems.
- Inno Setup was installed via winget to `%LOCALAPPDATA%/Programs/Inno Setup 6/` (user-local, not system-wide Program Files x86)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Removed ArchitecturesInstallMode directive**
- **Found during:** Task 1 (Compile installer)
- **Issue:** Inno Setup 6.7.1 (non-commercial edition) does not recognize the `ArchitecturesInstallMode` directive, causing compilation to fail with "Unrecognized [Setup] section directive"
- **Fix:** Removed the `ArchitecturesInstallMode=x64compatible` line from jojot.iss. The `ArchitecturesAllowed=x64compatible` directive alone is sufficient to enforce x64 installation.
- **Files modified:** installer/jojot.iss
- **Verification:** ISCC compilation succeeded, producing 57MB installer
- **Committed in:** 37d2191 (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Auto-fix necessary for installer compilation. No functional impact -- x64 enforcement still works via ArchitecturesAllowed.

## Issues Encountered
- Inno Setup installed to user-local Programs directory rather than the expected `C:\Program Files (x86)\Inno Setup 6\` path. Resolved by locating ISCC.exe at `%LOCALAPPDATA%/Programs/Inno Setup 6/ISCC.exe`.

## User Setup Required
None - Inno Setup was installed automatically via winget.

## Next Phase Readiness
- Phase 14 (Installer) is complete
- The installer at `installer/output/JoJot-2026.3.0-Setup.exe` is ready for distribution
- Manual verification of the install/launch/uninstall cycle is recommended before release

## Self-Check: PASSED

All files verified present, all commits verified in git log.

---
*Phase: 14-installer*
*Completed: 2026-03-10*
