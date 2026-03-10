---
name: release
description: Bump version, publish self-contained build, and compile Inno Setup installer
argument-hint: "<version>"
allowed-tools:
  - Read
  - Edit
  - Bash
  - Glob
  - AskUserQuestion
---
<objective>
Build a release of JoJot: bump the version in csproj and installer script, run `dotnet publish`, and compile the Inno Setup installer to produce the Setup.exe.
</objective>

<process>
## 1. Determine version

If `$ARGUMENTS` is provided, use it as the new version (expected format: `YYYY.M.N`, e.g. `2026.3.2`).

If no argument is provided, read the current version from `JoJot/JoJot.csproj` and ask the user what the new version should be.

## 2. Bump version in both files

Update **`JoJot/JoJot.csproj`**:
- `<Version>` → new version
- `<AssemblyVersion>` → new version + `.0` suffix (e.g. `2026.3.2.0`)
- `<FileVersion>` → same as AssemblyVersion

Update **`installer/jojot.iss`**:
- `AppVersion=` → new version
- `AppVerName=JoJot ` → new version
- `OutputBaseFilename=JoJot-` → `JoJot-{version}-Setup`
- Comment line with output path → update to match

## 3. Publish the application

Run from the repo root:

```
dotnet publish JoJot/JoJot.csproj -c Release -r win-x64 --self-contained
```

This produces the multi-file publish output that the Inno Setup script expects at `JoJot/bin/Release/net10.0-windows/win-x64/publish/`.

If the build fails, stop and report the error.

## 4. Compile the installer

Run the Inno Setup compiler:

```
"/c/Users/vproc/AppData/Local/Programs/Inno Setup 6/ISCC.exe" installer/jojot.iss
```

The output will be at `installer/output/JoJot-{version}-Setup.exe`.

If ISCC.exe is not found at that path, search for it and report to the user.

## 5. Report results

Show:
- Version: old → new
- Setup exe path and file size
- Any warnings from the build
</process>
