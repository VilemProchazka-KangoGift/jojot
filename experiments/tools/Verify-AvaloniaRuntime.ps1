#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ManagedExe,
    [Parameter(Mandatory)] [string] $AotExe,
    [Parameter(Mandatory)] [string] $WpfRun,
    [Parameter(Mandatory)] [string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'Choose a new output directory.'
}
$root = (New-Item -ItemType Directory -Path $OutputDirectory).FullName
$fixture = Join-Path (Resolve-Path -LiteralPath $WpfRun).Path 'fixture'
$desktop = (Get-Content -LiteralPath (Join-Path $fixture 'desktop.txt') -Raw).Trim()
$executables = [ordered]@{ Managed = $ManagedExe; NativeAOT = $AotExe }
foreach ($variant in $executables.Keys) {
    $exe = (Resolve-Path -LiteralPath $executables[$variant]).Path
    $data = (New-Item -ItemType Directory -Path (Join-Path $root $variant)).FullName
    foreach ($file in 'jojot.db', 'startup-cache.json', '.clean-shutdown') {
        Copy-Item -LiteralPath (Join-Path $fixture $file) -Destination $data
    }
    $probe = Join-Path $data 'startup-probe.json'
    $info = [Diagnostics.ProcessStartInfo]::new($exe)
    foreach ($argument in '--data-dir', $data, '--desktop-guid', $desktop, '--benchmark-output', $probe, '--stay-open') {
        $info.ArgumentList.Add($argument)
    }
    $child = [Diagnostics.Process]::Start($info)
    try {
        $deadline = [Diagnostics.Stopwatch]::StartNew()
        while (-not $child.HasExited -and -not (Test-Path -LiteralPath $probe) -and $deadline.Elapsed.TotalSeconds -lt 30) {
            Start-Sleep -Milliseconds 50
        }
        if ($child.HasExited -or -not (Test-Path -LiteralPath $probe)) {
            throw "$variant did not reach the ready frame. Inspect $data."
        }
        $modules = @($child.Modules | Select-Object -ExpandProperty ModuleName | Sort-Object)
        $modules | Set-Content -LiteralPath (Join-Path $root "$variant-modules.txt")
        if ('e_sqlite3.dll' -notin $modules) {
            throw "$variant process did not load the fixture database's native SQLite provider."
        }
        $hasCoreClr = 'coreclr.dll' -in $modules
        if ($hasCoreClr -ne ($variant -eq 'Managed')) {
            throw "$variant unexpected CoreCLR module state."
        }
        $child.Refresh()
        if (-not $child.CloseMainWindow() -or -not $child.WaitForExit(10000) -or $child.ExitCode -ne 0) {
            throw "$variant did not exit cleanly after inspecting modules."
        }
        Write-Host "$variant loaded $($modules.Count) modules. CoreCLR loaded: $hasCoreClr"
    } finally {
        if (-not $child.HasExited) {
            $child.Kill()
            $child.WaitForExit()
        }
        $child.Dispose()
    }
}
