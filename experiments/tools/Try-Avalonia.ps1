#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Executable,
    [Parameter(Mandatory)] [string] $WpfRun,
    [string] $FixtureDirectory,
    [string] $DataRoot = (Join-Path $PSScriptRoot '..' '..' '.local' 'avalonia-aot' 'try')
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
$fixture = if ($FixtureDirectory) {
    (Resolve-Path -LiteralPath $FixtureDirectory).Path
} else {
    Join-Path (Resolve-Path -LiteralPath $WpfRun).Path 'fixture'
}
$desktop = (Get-Content -LiteralPath (Join-Path $fixture 'desktop.txt') -Raw).Trim()
$directory = (New-Item -ItemType Directory -Path (Join-Path $DataRoot ([Guid]::NewGuid().ToString('N')))).FullName
foreach ($file in 'jojot.db', 'startup-cache.json', '.clean-shutdown') {
    Copy-Item -LiteralPath (Join-Path $fixture $file) -Destination $directory
}
$info = [Diagnostics.ProcessStartInfo]::new($exe)
$info.ArgumentList.Add('--data-dir')
$info.ArgumentList.Add($directory)
$info.ArgumentList.Add('--desktop-guid')
$info.ArgumentList.Add($desktop)
$child = [Diagnostics.Process]::Start($info)
Write-Host "Started process $($child.Id). This disposable fixture copy is in $directory"
Write-Host "Desktop identifier for reopening the fixture: $desktop"
$child.Dispose()
