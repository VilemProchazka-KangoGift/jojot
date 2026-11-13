#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BaselineExe,
    [Parameter(Mandatory)] [string] $CandidateExe,
    [Parameter(Mandatory)] [string] $FixtureDirectory,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [ValidateRange(1, 200)] [int] $Samples = 20,
    [ValidateRange(0, 20)] [int] $Warmups = 3,
    [switch] $Trace,
    [ValidateSet('default', 'gpu', 'software')] [string] $BaselineRenderMode = 'gpu',
    [ValidateSet('default', 'gpu', 'software')] [string] $CandidateRenderMode = 'gpu',
    [switch] $RenderWorkload,
    [switch] $ContinuousScroll
)
$ErrorActionPreference = 'Stop'
if ($RenderWorkload -and $ContinuousScroll) { throw 'Choose one workload per launch.' }
$executables = @((Resolve-Path -LiteralPath $BaselineExe).Path, (Resolve-Path -LiteralPath $CandidateExe).Path)
$fixture = (Resolve-Path -LiteralPath $FixtureDirectory).Path
$desktop = (Get-Content -LiteralPath (Join-Path $fixture 'desktop.txt') -Raw).Trim()
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory.' }
$root = (New-Item -ItemType Directory -Path $OutputDirectory).FullName
[pscustomobject]@{ Samples=$Samples; Warmups=$Warmups; Trace=[bool]$Trace;
    RenderWorkload=[bool]$RenderWorkload; ContinuousScroll=[bool]$ContinuousScroll; BaselineRenderMode=$BaselineRenderMode;
    CandidateRenderMode=$CandidateRenderMode } | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $root 'run-settings.json')
$files = @('jojot.db', 'startup-cache.json', '.clean-shutdown')
$files | ForEach-Object { Get-FileHash -LiteralPath (Join-Path $fixture $_) } |
    Export-Csv -LiteralPath (Join-Path $root 'fixture-hashes.csv') -NoTypeInformation
$executables | ForEach-Object { Get-FileHash -LiteralPath $_ } |
    Export-Csv -LiteralPath (Join-Path $root 'executable-hashes.csv') -NoTypeInformation
$rows = [Collections.Generic.List[object]]::new()
$geometry = $null
foreach ($phase in 'warmup', 'measured') {
    $count = if ($phase -eq 'warmup') { $Warmups } else { $Samples }
    for ($round = 1; $round -le $count; $round++) {
        $order = if ($round % 2) { @(0, 1) } else { @(1, 0) }
        foreach ($index in $order) {
            $variant = @('baseline', 'candidate')[$index]
            $directory = (New-Item -ItemType Directory -Path (Join-Path $root "runs/$phase/$round/$variant")).FullName
            foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $fixture $file) -Destination $directory }
            $result = Join-Path $directory 'startup-probe.json'
            $info = [Diagnostics.ProcessStartInfo]::new($executables[$index])
            $info.UseShellExecute = $false
            $info.CreateNoWindow = $true
            $info.RedirectStandardError = $true
            if ($Trace) { $info.Environment['JOJOT_STARTUP_TRACE'] = '1' }
            else { $info.Environment.Remove('JOJOT_STARTUP_TRACE') | Out-Null }
            $info.Environment['JOJOT_RENDER_MODE'] = @($BaselineRenderMode, $CandidateRenderMode)[$index]
            if ($RenderWorkload) { $info.Environment['JOJOT_RENDER_WORKLOAD'] = '1' }
            else { $info.Environment.Remove('JOJOT_RENDER_WORKLOAD') | Out-Null }
            if ($ContinuousScroll) { $info.Environment['JOJOT_CONTINUOUS_SCROLL'] = '1' }
            else { $info.Environment.Remove('JOJOT_CONTINUOUS_SCROLL') | Out-Null }
            foreach ($argument in @('--data-dir', $directory, '--desktop-guid', $desktop,
                    '--benchmark-output', $result, '--benchmark-width', '552', '--benchmark-height', '420')) {
                $info.ArgumentList.Add($argument)
            }
            $process = [Diagnostics.Process]::Start($info)
            try {
                $errors = $process.StandardError.ReadToEndAsync()
                if (-not $process.WaitForExit($(if ($RenderWorkload -or $ContinuousScroll) { 180000 } else { 30000 }))) {
                    $process.Kill(); $process.WaitForExit()
                    throw "Timed out: $directory"
                }
                $diagnostics = $errors.GetAwaiter().GetResult()
                if ($process.ExitCode -ne 0 -or $diagnostics -or -not (Test-Path -LiteralPath $result) -or
                    -not (Test-Path -LiteralPath (Join-Path $directory '.clean-shutdown'))) {
                    throw "Failed launch: $directory; exit $($process.ExitCode); $diagnostics"
                }
                if (Get-ChildItem -LiteralPath $directory -Filter 'jojot*.log' -File |
                    Select-String -Pattern '\[(ERROR|FATAL)\]') { throw "Application error: $directory" }
                $data = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
                if ($RenderWorkload -and -not (Test-Path -LiteralPath ($result + '.render.json'))) {
                    throw "Missing render workload: $directory"
                }
                if ($ContinuousScroll -and -not (Test-Path -LiteralPath ($result + '.scroll.json'))) {
                    throw "Missing continuous-scroll workload: $directory"
                }
                if ($data.Notes -ne 100 -or $data.SelectedNoteName -ne 'Note 0' -or $data.EditorTextLength -ne 1024) {
                    throw "Unexpected fixture selection: $directory"
                }
                $rectangle = "$($data.NativeLeft),$($data.NativeTop),$($data.NativeWidth),$($data.NativeHeight),$($data.ClientWidth),$($data.ClientHeight)"
                if ($geometry -and $geometry -ne $rectangle) { throw "Geometry changed: $directory" }
                $geometry = $rectangle
                $row = [pscustomobject]@{ Phase=$phase; Round=$round; Variant=$variant;
                    RenderMode=@($BaselineRenderMode, $CandidateRenderMode)[$index];
                    FirstRenderMs=$data.FirstRenderMs; EditorReadyMs=$data.EditorReadyMs;
                    EditorRenderingMs=$data.EditorRenderingMs; Geometry=$rectangle }
                $rows.Add($row)
                $row | Export-Csv -LiteralPath (Join-Path $root 'samples.csv') -NoTypeInformation -Append
            } finally { $process.Dispose() }
        }
    }
}
$summary = foreach ($variant in 'baseline', 'candidate') {
    foreach ($metric in 'FirstRenderMs', 'EditorReadyMs', 'EditorRenderingMs') {
        $values = @($rows | Where-Object { $_.Phase -eq 'measured' -and $_.Variant -eq $variant } |
            ForEach-Object { $_.$metric } | Sort-Object)
        $median = if ($values.Count % 2) { $values[[int][Math]::Floor($values.Count / 2)] }
            else { ($values[$values.Count / 2 - 1] + $values[$values.Count / 2]) / 2 }
        [pscustomobject]@{ Variant=$variant; Metric=$metric; Samples=$values.Count;
            MedianMs=[Math]::Round($median, 2); P95Ms=[Math]::Round($values[[int][Math]::Ceiling($values.Count * 0.95) - 1], 2) }
    }
}
$summary | Export-Csv -LiteralPath (Join-Path $root 'summary.csv') -NoTypeInformation
$summary | Format-Table
