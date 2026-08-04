<#
.SYNOPSIS
    Links .agents/skills -> .claude/skills so Codex can discover this repo's skills.

.DESCRIPTION
    Claude Code and Codex look for repo skills in different places:

      Claude Code   .claude/skills/          <- the real, committed directory
      Codex         .agents/skills/          <- a link to it, created here, gitignored

    One link exposes the whole directory without duplicating anything in git. Route
    pointers in AGENTS.md name the real `.claude/skills/...` path, so every skill stays
    readable even if this link is missing -- it only powers Codex's implicit
    (auto-trigger) discovery.

    CONVERGE-TO-CORRECT, not create-if-missing. If .agents/skills exists but points
    somewhere else -- the classic case is a worktree copied by hand, whose link still
    points at the ORIGINAL and therefore silently serves another branch's skills -- it
    is removed and recreated. That is the one failure mode here that is silently wrong
    rather than merely absent, so it is checked every run.

    Safe to run repeatedly, and from any clone or worktree.

.PARAMETER Quiet
    Only emit output when something changed or a check failed.

.NOTES
    KEEP THIS FILE PURE ASCII, no BOM. Windows PowerShell 5.1 decodes a BOM-less file as
    the ANSI code page, so a UTF-8 em dash (E2 80 94) ends in 0x94 = a curly closing
    quote, which inside a DOUBLE-quoted string terminates it early and breaks the parse
    with an error pointing at an unrelated line. Verify with ParseFile (not ParseInput,
    which cannot see an encoding defect) or by running under 5.1.
#>
[CmdletBinding()]
param(
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$RealDir = Join-Path $RepoRoot '.claude/skills'
$LinkDir = Join-Path $RepoRoot '.agents/skills'
$LinkParent = Split-Path -Parent $LinkDir

function Write-Info([string]$Message) {
    if (-not $Quiet) { Write-Host $Message }
}

# A dangling link is worse than no link, so fail rather than create one.
if (@(Get-ChildItem -LiteralPath $RealDir -Directory -ErrorAction SilentlyContinue).Count -eq 0) {
    Write-Host "ERROR: $RealDir does not exist, or holds no skills." -ForegroundColor Red
    Write-Host '       This is a repo problem, not a setup problem -- the skills directory is committed.' -ForegroundColor Red
    exit 1
}

$existing = Get-Item -LiteralPath $LinkDir -Force -ErrorAction SilentlyContinue
if ($existing) {
    $isLink = [bool]$existing.LinkType
    # .Target is a collection on Windows PowerShell 5.1 and a string on PS 7+.
    $target = if ($isLink) { @($existing.Target)[0] } else { $null }

    if ($isLink) {
        $resolvedTarget = $null
        try { $resolvedTarget = (Resolve-Path -LiteralPath $target -ErrorAction Stop).Path } catch { }
        $resolvedReal = (Resolve-Path -LiteralPath $RealDir).Path
        if ($resolvedTarget -and $resolvedTarget.TrimEnd('\', '/') -eq $resolvedReal.TrimEnd('\', '/')) {
            Write-Info '  .agents/skills -> .claude/skills (already correct)'
            exit 0
        }
        $where = if ($target) { "points at '$target'" } else { 'has no readable target' }
        Write-Host "  .agents/skills $where -- not this repo's .claude/skills. Recreating." -ForegroundColor Yellow

        # Delete the reparse point ITSELF, never recursively: some PowerShell servicing
        # levels recurse INTO a junction, and the blast radius is another worktree's
        # tracked skills. A non-recursive directory delete cannot do that on any version.
        [System.IO.Directory]::Delete($existing.FullName, $false)
    }
    else {
        # A REAL directory. Never delete its contents: .agents/skills is exactly where
        # someone would hand-author Codex-only skills, and it is gitignored, so anything
        # here is unrecoverable. Converge only when there is provably nothing to lose.
        $contents = @(Get-ChildItem -LiteralPath $LinkDir -Force -ErrorAction SilentlyContinue)
        if ($contents.Count -gt 0) {
            Write-Host "ERROR: $LinkDir is a real directory holding $($contents.Count) item(s), not a link." -ForegroundColor Red
            Write-Host '       Refusing to delete it -- it is gitignored, so its contents cannot be recovered.' -ForegroundColor Red
            Write-Host '       Move those skills into .claude/skills (where both agents see them), or' -ForegroundColor Red
            Write-Host '       remove the directory yourself, then re-run this script.' -ForegroundColor Red
            exit 1
        }
        Write-Info '  .agents/skills was an empty directory, not a link. Replacing it with the link.'
        Remove-Item -LiteralPath $LinkDir -Force
    }
}

if (-not (Test-Path -LiteralPath $LinkParent)) {
    New-Item -ItemType Directory -Path $LinkParent -Force | Out-Null
}

if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    # A junction, NOT a symlink: New-Item -ItemType SymbolicLink needs Administrator or
    # Developer Mode; mklink /J needs neither. Local artifact, never committed.
    $out = cmd /c mklink /J "`"$LinkDir`"" "`"$RealDir`"" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: mklink failed: $out" -ForegroundColor Red
        exit 1
    }
}
else {
    New-Item -ItemType SymbolicLink -Path $LinkDir -Target $RealDir | Out-Null
}

$skillCount = @(Get-ChildItem -LiteralPath $LinkDir -Directory -ErrorAction SilentlyContinue).Count
if ($skillCount -eq 0) {
    # Remove it before failing, or the next run resolves this empty link as "already
    # correct", prints success, and the failure reported here is never seen again.
    try { [System.IO.Directory]::Delete($LinkDir, $false) } catch { }
    if (Test-Path -LiteralPath $LinkDir) {
        Write-Host 'ERROR: the link was created but reads as empty, AND could not be removed.' -ForegroundColor Red
        Write-Host "       Delete it by hand: $LinkDir" -ForegroundColor Red
    }
    else {
        Write-Host 'ERROR: the link was created but reads as empty; removed it again.' -ForegroundColor Red
    }
    exit 1
}

Write-Info "  created .agents/skills -> .claude/skills ($skillCount skills visible to Codex)"

# Set the success code explicitly: otherwise $LASTEXITCODE is whatever the last native
# command left behind (on Windows, mklink's -- 0, but incidentally), and on macOS/Linux
# the New-Item branch sets nothing at all, so a stale value survives.
exit 0
