#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $WpfRun,
    [Parameter(Mandatory)] [string] $ManagedExe,
    [Parameter(Mandatory)] [string] $AotExe,
    [string] $PrototypeAotExe,
    [switch] $FullAvalonia,
    [string] $FixtureDirectory,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [Parameter(Mandatory)] [double] $Width,
    [Parameter(Mandatory)] [double] $Height,
    [ValidateRange(1, 200)] [int] $Samples = 20,
    [ValidateRange(0, 20)] [int] $Warmups = 3,
    [int] $OrderSeed = 20261112
)

$ErrorActionPreference = 'Stop'
$fixture = if ($FixtureDirectory) {
    (Resolve-Path -LiteralPath $FixtureDirectory).Path
} else {
    Join-Path (Resolve-Path -LiteralPath $WpfRun).Path 'fixture'
}
$desktop = (Get-Content -LiteralPath (Join-Path $fixture 'desktop.txt') -Raw).Trim()
$fixtureDesktopId = [guid]::Empty
if (($PrototypeAotExe -or $FullAvalonia) -and
    (-not [guid]::TryParse($desktop, [ref]$fixtureDesktopId) -or $fixtureDesktopId -eq [guid]::Empty)) {
    throw 'The full Avalonia comparison requires a fixture seeded on a detected Windows virtual desktop.'
}
$fixtureFiles = @('jojot.db', 'startup-cache.json', '.clean-shutdown')
$executables = [ordered]@{ 'WPF ReadyToRun' = (Resolve-Path -LiteralPath (Join-Path $WpfRun 'baseline/wpf/JoJot.exe')).Path }
if ($PrototypeAotExe) {
    $executables['Prototype NativeAOT'] = (Resolve-Path -LiteralPath $PrototypeAotExe).Path
}
if ($PrototypeAotExe -or $FullAvalonia) {
    $executables['Full Avalonia ReadyToRun'] = (Resolve-Path -LiteralPath $ManagedExe).Path
    $executables['Full Avalonia NativeAOT'] = (Resolve-Path -LiteralPath $AotExe).Path
} else {
    $executables['Avalonia ReadyToRun'] = (Resolve-Path -LiteralPath $ManagedExe).Path
    $executables['Avalonia NativeAOT'] = (Resolve-Path -LiteralPath $AotExe).Path
}
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'Choose a new output directory to preserve earlier raw samples.'
}
$runRoot = (New-Item -ItemType Directory -Path $OutputDirectory).FullName
$rawPath = Join-Path $runRoot 'samples.csv'
$invariant = [Globalization.CultureInfo]::InvariantCulture
$fixtureFiles | ForEach-Object {
    $hash = Get-FileHash -LiteralPath (Join-Path $fixture $_)
    "$($hash.Hash.ToLowerInvariant())  $_"
} | Set-Content -LiteralPath (Join-Path $runRoot 'fixture.sha256')

function Invoke-Sample([string] $Variant, [string] $Directory) {
    New-Item -ItemType Directory -Path $Directory | Out-Null
    foreach ($file in $fixtureFiles) {
        Copy-Item -LiteralPath (Join-Path $fixture $file) -Destination $Directory
    }

    $info = [Diagnostics.ProcessStartInfo]::new($executables[$Variant])
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardError = $true
    $resultPath = Join-Path $Directory 'startup-probe.json'
    if ($Variant -eq 'WPF ReadyToRun') {
        $info.Environment['JOJOT_STARTUP_PROBE_DATA'] = $Directory
        $info.Environment['JOJOT_STARTUP_PROBE_FIXED_GEOMETRY'] = '1'
    } else {
        $arguments = @('--data-dir', $Directory, '--desktop-guid', $desktop, '--benchmark-output', $resultPath,
            '--benchmark-width', $Width.ToString($invariant), '--benchmark-height', $Height.ToString($invariant))
        foreach ($argument in $arguments) {
            $info.ArgumentList.Add($argument)
        }
    }

    $child = [Diagnostics.Process]::Start($info)
    try {
        $stderr = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(30000)) {
            $child.Kill()
            $child.WaitForExit()
            throw "Startup timed out. Inspect $Directory."
        }
        if ($child.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath) -or
            -not (Test-Path -LiteralPath (Join-Path $Directory '.clean-shutdown'))) {
            throw "Startup failed (exit $($child.ExitCode)). Inspect $Directory. $($stderr.GetAwaiter().GetResult())"
        }
        $isFullAvalonia = $Variant.StartsWith('Full Avalonia ', [StringComparison]::Ordinal)
        if ($Variant -eq 'WPF ReadyToRun' -or $isFullAvalonia) {
            $logs = Get-ChildItem -LiteralPath $Directory -Filter 'jojot*.log' -File
            if ($logs | Select-String -Pattern '\[(ERROR|FATAL)\]' | Select-Object -First 1) {
                throw "$Variant reported an application error. Inspect $Directory."
            }
        }
        if ($Variant -eq 'WPF ReadyToRun') {
            $desktopMessage = if ($desktop -eq 'default') {
                'Virtual desktop: fallback mode (single-notepad)'
            } else {
                "Virtual desktop: $desktop ("
            }
            if (-not ($logs | Select-String -SimpleMatch $desktopMessage | Select-Object -First 1)) {
                throw "WPF detected a different virtual desktop. Inspect $Directory."
            }
        }
        $diagnostics = $stderr.GetAwaiter().GetResult()
        if (-not [string]::IsNullOrWhiteSpace($diagnostics)) {
            Set-Content -LiteralPath (Join-Path $Directory 'startup-stderr.txt') -Value $diagnostics
            if ($isFullAvalonia) {
                throw "$Variant reported a startup or shutdown error on stderr. Inspect $Directory."
            }
        }
        Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    } finally {
        $child.Dispose()
    }
}

$artifactInfo = foreach ($variant in $executables.Keys) {
    $exe = Get-Item -LiteralPath $executables[$variant]
    $payload = @(Get-ChildItem -LiteralPath $exe.Directory.FullName -File -Recurse |
        Where-Object Extension -NotIn '.pdb', '.xml', '.dbg')
    [pscustomobject]@{
        Variant = $variant
        ExeSha256 = (Get-FileHash -LiteralPath $exe.FullName).Hash
        PayloadBytes = ($payload | Measure-Object Length -Sum).Sum
        PayloadFiles = $payload.Count
    }
}
$artifactInfo | Export-Csv -LiteralPath (Join-Path $runRoot 'payloads.csv') -NoTypeInformation

# Each four-build block balances launch slot and immediate predecessor. The
# three-build comparison retains its original complete set of permutations.
$permutations = if ($PrototypeAotExe) {
    @('0132', '1203', '2310', '3021')
} else {
    @('012', '021', '102', '120', '201', '210')
}
$random = [Random]::new($OrderSeed)
$variants = @($executables.Keys)
foreach ($phase in 'warmup', 'measured') {
    $rounds = if ($phase -eq 'warmup') { $Warmups } else { $Samples }
    for ($round = 1; $round -le $rounds; $round++) {
        $index = ($round - 1) % $permutations.Length
        if ($index -eq 0) {
            $orderBlock = $permutations.Clone()
            for ($position = $orderBlock.Length - 1; $position -gt 0; $position--) {
                $other = $random.Next($position + 1)
                $orderBlock[$position], $orderBlock[$other] = $orderBlock[$other], $orderBlock[$position]
            }
        }
        $order = $orderBlock[$index]
        for ($slot = 0; $slot -lt $variants.Count; $slot++) {
            $variantIndex = [int]::Parse($order[$slot].ToString())
            $variant = $variants[$variantIndex]
            $directory = Join-Path $runRoot 'runs' $phase "$round" "$variantIndex"
            $result = Invoke-Sample $variant $directory
            [pscustomobject]@{
                Phase = $phase
                Round = $round
                Slot = $slot + 1
                Variant = $variant
                Notes = $result.Notes
                SelectedNoteName = $result.SelectedNoteName
                EditorTextLength = $result.EditorTextLength
                FirstRenderMs = $result.FirstRenderMs
                EditorReadyMs = $result.EditorReadyMs
                EditorRenderingMs = $result.EditorRenderingMs
                FirstRenderLeft = $result.FirstRenderLeft
                FirstRenderTop = $result.FirstRenderTop
                FirstRenderWidth = $result.FirstRenderWidth
                FirstRenderHeight = $result.FirstRenderHeight
                NativeLeft = $result.NativeLeft
                NativeTop = $result.NativeTop
                NativeWidth = $result.NativeWidth
                NativeHeight = $result.NativeHeight
                ClientWidth = $result.ClientWidth
                ClientHeight = $result.ClientHeight
            } | Export-Csv -LiteralPath $rawPath -NoTypeInformation -Append
            if ($variant -ne 'WPF ReadyToRun' -and
                ($result.SelectedNoteName -ne 'Note 0' -or $result.EditorTextLength -ne 1024)) {
                throw "Avalonia did not select and load the first fixture note. Inspect $directory."
            }
            if ($result.Notes -ne 100 -or $result.FirstRenderMs -le 0 -or $result.EditorReadyMs -le 0 -or
                $result.EditorRenderingMs -lt $result.EditorReadyMs -or
                $result.NativeWidth -le 0 -or $result.NativeHeight -le 0 -or
                $result.ClientWidth -le 0 -or $result.ClientHeight -le 0) {
                throw "Unexpected fixture, timing, or render dimensions. Inspect $directory."
            }
        }
    }
}

$raw = Import-Csv -LiteralPath $rawPath
$reference = $raw | Where-Object Variant -eq 'WPF ReadyToRun' | Select-Object -First 1
foreach ($row in $raw) {
    foreach ($dimension in 'NativeLeft', 'NativeTop', 'NativeWidth', 'NativeHeight', 'ClientWidth', 'ClientHeight') {
        if ([Math]::Abs([int]$row.$dimension - [int]$reference.$dimension) -gt 2) {
            throw "The first-frame $dimension differed across variants. Inspect $rawPath and recalibrate Avalonia dimensions."
        }
    }
}
$measured = $raw | Where-Object Phase -eq 'measured'
$summary = foreach ($variant in $variants) {
    $rows = $measured | Where-Object Variant -eq $variant
    foreach ($metric in 'FirstRenderMs', 'EditorReadyMs', 'EditorRenderingMs') {
        $values = @($rows | ForEach-Object { [double]$_.$metric } | Sort-Object)
        $mid = [int][Math]::Floor($values.Count / 2)
        $median = if ($values.Count % 2 -eq 0) { ($values[$mid - 1] + $values[$mid]) / 2 } else { $values[$mid] }
        [pscustomobject]@{
            Variant = $variant
            Metric = $metric
            Samples = $values.Count
            MedianMs = [Math]::Round($median, 2)
            P95Ms = [Math]::Round($values[[int][Math]::Ceiling($values.Count * 0.95) - 1], 2)
            MinMs = [Math]::Round($values[0], 2)
            MaxMs = [Math]::Round($values[-1], 2)
        }
    }
}
$summary | Export-Csv -LiteralPath (Join-Path $runRoot 'summary.csv') -NoTypeInformation
$summary | Format-Table -AutoSize
