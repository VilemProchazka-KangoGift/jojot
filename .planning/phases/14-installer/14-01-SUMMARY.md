---
phase: 14-installer
plan: 01
subsystem: infra
tags: [inno-setup, installer, calver, ico, windows]

# Dependency graph
requires: []
provides:
  - CalVer 2026.3.0 version metadata embedded in EXE
  - Multi-resolution app icon (16/32/48/256px) at JoJot/Assets/jojot.ico
  - Complete Inno Setup installer script at installer/jojot.iss
affects: []

# Tech tracking
tech-stack:
  added: [Inno Setup]
  patterns: [CalVer versioning, ICO generation, two-step build-then-package]

key-files:
  created:
    - JoJot/Assets/jojot.ico
    - installer/jojot.iss
  modified:
    - JoJot/JoJot.csproj

key-decisions:
  - "ASCII-safe publisher name (Vilem Prochazka) in .csproj to avoid PE metadata encoding issues"
  - "PNG-compressed ICO entries for all sizes (Vista+ compatible, simpler than BMP)"
  - "Stable AppId GUID for upgrade detection across versions"

patterns-established:
  - "CalVer versioning: Year.Month.Build (2026.3.0)"
  - "Two-step installer build: dotnet publish then iscc compile"

requirements-completed: [DIST-01]

# Metrics
duration: 3min
completed: 2026-03-10
---

# Phase 14 Plan 01: Installer Script and Metadata Summary

**CalVer 2026.3.0 metadata in .csproj, 4-resolution app icon, and complete Inno Setup installer script with auto-start, force-close upgrade, and data-deletion prompt**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-10T08:28:29Z
- **Completed:** 2026-03-10T08:31:34Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Added CalVer version (2026.3.0), publisher, product, description, and copyright metadata to .csproj
- Created multi-resolution app icon (blue rounded-square with white "J" lettermark) at 16/32/48/256px
- Authored complete Inno Setup script with minimal wizard, Start Menu shortcuts, optional auto-start, force-close upgrade, uninstall data-deletion prompt (default No), and Launch JoJot finish checkbox

## Task Commits

Each task was committed atomically:

1. **Task 1: Add version metadata and create app icon** - `14e5f06` (feat)
2. **Task 2: Author the Inno Setup installer script** - `7459305` (feat)

## Files Created/Modified
- `JoJot/JoJot.csproj` - Added Version, AssemblyVersion, FileVersion, Company, Authors, Product, Description, Copyright, ApplicationIcon properties
- `JoJot/Assets/jojot.ico` - Multi-resolution icon (16x16, 32x32, 48x48, 256x256) with blue #4A90D9 background and white bold "J"
- `installer/jojot.iss` - Complete Inno Setup script defining the full install/uninstall/upgrade experience

## Decisions Made
- Used ASCII-safe "Vilem Prochazka" (no diacritics) for Company/Authors in .csproj to avoid PE metadata encoding issues with "Vilem Prochazka"
- PNG compression for all ICO sizes (Vista+ compatible, simpler than uncompressed BMP format)
- Generated stable AppId GUID `{B7E45A2C-8D31-4F6A-9E52-1C3D7A8B9F04}` for consistent upgrade detection
- Used Segoe UI Bold for the J lettermark (Windows system font, always available)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All installer source artifacts are ready
- To build the installer: run `dotnet publish` then `iscc installer/jojot.iss`
- Inno Setup (ISCC.exe) must be installed on the build machine to compile the .iss script
- No further plans in Phase 14

## Self-Check: PASSED

All files verified present, all commits verified in git log.

---
*Phase: 14-installer*
*Completed: 2026-03-10*
