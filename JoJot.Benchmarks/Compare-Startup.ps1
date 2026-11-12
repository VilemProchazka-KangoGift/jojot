[CmdletBinding()]
param(
    [string] $Baseline,
    [string] $Candidate = (Join-Path $PSScriptRoot '..'),
    [string] $Dotnet = 'dotnet',
    [string] $PackageSource,
    [string] $OutputRoot = (Join-Path $PSScriptRoot '..' '.local' 'startup-benchmark'),
    [ValidateRange(1, 200)] [int] $Samples = 20,
    [ValidateRange(0, 20)] [int] $Warmups = 3,
    [switch] $WindowProfile,
    [switch] $ControlGeometry,
    [switch] $PrepareOnly,
    [string] $PreparedRun,
    [string] $MeasurementRoot,
    [ValidateSet('All', 'Database', 'Wpf', 'WpfWithoutSnapshot', 'WpfBoth')] [string] $Scenario = 'All'
)

$ErrorActionPreference = 'Stop'
if ($ControlGeometry -and -not $WindowProfile) {
    throw '-ControlGeometry requires -WindowProfile.'
}
if (-not $Baseline -and -not $PreparedRun) {
    throw 'Supply -Baseline when building a new comparison.'
}
$publishRoot = if ($PreparedRun) {
    (Resolve-Path -LiteralPath $PreparedRun).Path
} else {
    $newRun = [IO.Path]::GetFullPath((Join-Path $OutputRoot ([Guid]::NewGuid().ToString('N'))))
    New-Item -ItemType Directory -Path $newRun | Out-Null
    $newRun
}
$runRoot = if ($MeasurementRoot) {
    $newMeasurement = [IO.Path]::GetFullPath($MeasurementRoot)
    New-Item -ItemType Directory -Path $newMeasurement -Force | Out-Null
    $newMeasurement
} else {
    $publishRoot
}
Write-Host "Startup benchmark artifacts are in $runRoot"

function Copy-Project([string] $Source, [string] $Destination) {
    robocopy $Source $Destination /E /XD bin obj /XF '*.user' build.log /NJH /NJS /NDL /NFL /NP
    if ($LASTEXITCODE -gt 7) {
        throw "Could not copy $Source (robocopy exit $LASTEXITCODE)."
    }
}

function Replace-Once([string] $Path, [string] $From, [string] $To) {
    $source = [IO.File]::ReadAllText($Path)
    $index = $source.IndexOf($From, [StringComparison]::Ordinal)
    if ($index -lt 0 -or $source.IndexOf($From, $index + $From.Length, [StringComparison]::Ordinal) -ge 0) {
        throw "Expected exactly one instrumentation point in $Path for $From."
    }
    [IO.File]::WriteAllText($Path, $source.Remove($index, $From.Length).Insert($index, $To))
}

function Add-StartupProbe([string] $AppSource) {
    Copy-Item (Join-Path $PSScriptRoot 'StartupProbe.cs.txt') (Join-Path $AppSource 'StartupProbe.cs')
    $environment = Join-Path $AppSource 'Services' 'AppEnvironment.cs'
    Replace-Once $environment 'Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),' @'
Environment.GetEnvironmentVariable("JOJOT_STARTUP_PROBE_DATA")
            ?? throw new InvalidOperationException("Supply a disposable startup fixture directory."),
'@
    Replace-Once $environment 'private const string FolderName = "JoJot";' 'private const string FolderName = "";'
    Replace-Once $environment 'private const string Suffix = "";' 'private static readonly string Suffix = $".StartupProbe.{Environment.ProcessId}";'
    $program = Join-Path $AppSource 'Program.cs'
    Replace-Once $program 'Directory.CreateDirectory(AppEnvironment.AppDataDirectory);' @'
StartupProbe.MarkEntry();
        Directory.CreateDirectory(AppEnvironment.AppDataDirectory);
'@
    Replace-Once $program 'var result = SendNewTabToPrimary();' 'var result = StartupProbe.RejectSecondInstance();'
    $app = Join-Path $AppSource 'App.xaml.cs'
    Replace-Once $app 'window.Show();' @'
StartupProbe.ObserveWindow(window);
        window.Show();
'@
    Replace-Once $app 'WindowActivationHelper.ActivateWindow(window);' '// The probe window leaves foreground activation alone.'
    Replace-Once $app 'LogPhase("tabs-loaded");' @'
StartupProbe.MarkEditorReady(window);
        LogPhase("tabs-loaded");
'@
    Replace-Once $app 'await HotkeyService.InitializeAsync(OnGlobalHotkeyPressed);' '// Leave the user''s global hotkey registration undisturbed.'
    Replace-Once $app 'LogService.Info("Startup complete in {ElapsedMs}ms", _startupStopwatch.ElapsedMilliseconds);' @'
LogService.Info("Startup complete in {ElapsedMs}ms", _startupStopwatch.ElapsedMilliseconds);
            await StartupProbe.CompleteAsync(this);
'@
    Replace-Once $app 'LogService.Error("Fatal error during application startup", ex);' @'
LogService.Error("Fatal error during application startup", ex);
            StartupProbe.Fail(this, ex);
'@
    if ($WindowProfile) {
        $window = Join-Path $AppSource 'Views' 'MainWindow.xaml.cs'
        Replace-Once $program 'LogService.Initialize(AppEnvironment.AppDataDirectory);' 'LogService.Initialize(AppEnvironment.AppDataDirectory); StartupProbe.Mark("LoggingInitialized");'
        Replace-Once $program 'RunPrimary(mutex, tookOverFromZombie: !createdNew);' 'StartupProbe.Mark("PrimaryStarting"); RunPrimary(mutex, tookOverFromZombie: !createdNew);'
        Replace-Once $program 'var app = new App();' 'StartupProbe.Mark("WpfEntry"); var app = new App();'
        Replace-Once $program 'app.InitializeComponent();' 'StartupProbe.Mark("AppConstructed"); app.InitializeComponent();'
        Replace-Once $program 'app.Run();' 'StartupProbe.Mark("AppXamlLoaded"); app.Run();'
        Replace-Once $app '_startupStopwatch = Stopwatch.StartNew();' 'StartupProbe.Mark("AppStartup"); _startupStopwatch = Stopwatch.StartNew();'
        Replace-Once $app 'bool haveSnapshot = StartupSnapshot.TryLoad();' 'StartupProbe.Mark("SnapshotStarting"); bool haveSnapshot = StartupSnapshot.TryLoad();'
        Replace-Once $app 'if (haveSnapshot)' 'StartupProbe.Mark("SnapshotFinished"); if (haveSnapshot)'
        Replace-Once $app 'LogPhase("db-kickoff+snapshot");' 'StartupProbe.Mark("PreferencesAvailable"); LogPhase("db-kickoff+snapshot");'
        Replace-Once $app 'await ThemeService.InitializeAsync();' 'StartupProbe.Mark("ThemeStarting"); await ThemeService.InitializeAsync();'
        Replace-Once $app 'await VirtualDesktopService.InitializeAsync();' 'StartupProbe.Mark("ThemeFinished"); await VirtualDesktopService.InitializeAsync();'
        Replace-Once $app 'VirtualDesktopService.SubscribeNotifications();' 'VirtualDesktopService.SubscribeNotifications(); StartupProbe.Mark("DesktopInitialized");'
        Replace-Once $app 'var window = new MainWindow(desktopGuid);' 'StartupProbe.Mark("WindowConstructing"); var window = new MainWindow(desktopGuid);'
        Replace-Once $app 'LogPhase("window-ctor");' 'StartupProbe.Mark("WindowConstructed"); LogPhase("window-ctor");'
        Replace-Once $app 'window.Show();' 'StartupProbe.Mark("WindowShowing"); window.Show();'
        Replace-Once $app 'LogPhase("window-shown");' 'StartupProbe.Mark("WindowShown"); LogPhase("window-shown");'
        Replace-Once $app 'WindowPlacementHelper.ApplyGeometry(window, geo); // SetWindowPlacement needs the HWND' 'StartupProbe.Mark("PlacementStarting"); WindowPlacementHelper.ApplyGeometry(window, geo); StartupProbe.Mark("PlacementFinished");'
        Replace-Once $app 'VirtualDesktopService.TryMoveWindowToDesktop(hwnd, desktopGuid);' 'StartupProbe.Mark("DesktopMoveStarting"); VirtualDesktopService.TryMoveWindowToDesktop(hwnd, desktopGuid); StartupProbe.Mark("DesktopMoveFinished");'
        Replace-Once $app 'LogPhase("window-moved");' 'StartupProbe.Mark("WindowPositioned"); LogPhase("window-moved");'
        Replace-Once $window 'ViewModel = new MainWindowViewModel(desktopGuid);' 'StartupProbe.Mark("WindowConstructorEntered"); ViewModel = new MainWindowViewModel(desktopGuid);'
        Replace-Once $window '// Initialize checkpoint timer before InitializeComponent.' 'StartupProbe.Mark("ViewModelConstructed"); // Initialize checkpoint timer before InitializeComponent.'
        Replace-Once $window 'InitializeComponent();' 'StartupProbe.Mark("WindowXamlStarting"); InitializeComponent();'
        Replace-Once $window 'InitializeInputBindings();' 'StartupProbe.Mark("WindowXamlLoaded"); InitializeInputBindings();'
        Replace-Once $app 'var geo = StartupSnapshot.GetGeometry(desktopGuid);' 'var geo = StartupSnapshot.GetGeometry(desktopGuid) ?? StartupProbe.ProfileGeometry;'
        Replace-Once $app '                WindowPlacementHelper.ApplyGeometry(window, geo);' @'
                if (StartupProbe.ProfileGeometry is null)
                {
                    WindowPlacementHelper.ApplyGeometry(window, geo);
                }
'@
    }
    $placement = Join-Path $AppSource 'Services' 'WindowPlacementHelper.cs'
    Replace-Once $placement 'showCmd = corrected.IsMaximized ? SW_SHOWMAXIMIZED : SW_SHOWNORMAL,' 'showCmd = 4, // SW_SHOWNOACTIVATE for the startup probe'
    $tabs = Join-Path $AppSource 'Views' 'MainWindow.Tabs.cs'
    $source = [IO.File]::ReadAllText($tabs)
    [IO.File]::WriteAllText($tabs, $source.Replace('Keyboard.Focus(ContentEditor);', '// Preserve the active application keyboard focus.'))
}

function Publish-Project([string] $Project, [string] $Destination) {
    $sourceArguments = @()
    if ($PackageSource) {
        $sourceArguments = @('--source', $PackageSource)
    }
    & $Dotnet publish $Project -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true -p:NuGetAudit=false -p:UseSharedCompilation=false -o $Destination @sourceArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $Project (exit $LASTEXITCODE)."
    }
}

function Invoke-Sample([string] $Executable, [string] $DataDirectory, [string[]] $Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new($Executable)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.Environment['JOJOT_STARTUP_PROBE_DATA'] = $DataDirectory
    $info.Environment['JOJOT_STARTUP_PROBE_FIXED_GEOMETRY'] = if ($ControlGeometry) { '1' } else { '0' }
    foreach ($argument in $Arguments) {
        $info.ArgumentList.Add($argument)
    }
    $child = [Diagnostics.Process]::Start($info)
    try {
        $stdout = $child.StandardOutput.ReadToEndAsync()
        $stderr = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(30000)) {
            $child.Kill()
            $child.WaitForExit()
            throw "Startup sample timed out for $Executable in $DataDirectory."
        }
        $resultPath = Join-Path $DataDirectory 'startup-probe.json'
        $result = if (Test-Path -LiteralPath $resultPath) {
            Get-Content -LiteralPath $resultPath -Raw
        } else {
            $stdout.GetAwaiter().GetResult()
        }
        if ($child.ExitCode -ne 0 -or -not $result -or
            -not (Test-Path -LiteralPath (Join-Path $DataDirectory '.clean-shutdown'))) {
            throw "Sample failed (exit $($child.ExitCode)) in $DataDirectory. $result $($stderr.GetAwaiter().GetResult())"
        }
        return $result | ConvertFrom-Json
    } finally {
        $child.Dispose()
    }
}

$fixture = Join-Path $runRoot 'fixture'
if (-not $PreparedRun) {
    foreach ($variant in @('baseline', 'candidate')) {
        $source = (Resolve-Path -LiteralPath $(if ($variant -eq 'baseline') { $Baseline } else { $Candidate })).Path
        $copy = Join-Path $publishRoot $variant
        $appSource = Join-Path $copy 'JoJot'
        Copy-Project (Join-Path $source 'JoJot') $appSource
        $benchmarkCopy = New-Item -ItemType Directory -Path (Join-Path $copy 'JoJot.Benchmarks')
        Copy-Item -Path (Join-Path $PSScriptRoot '*.cs'), (Join-Path $PSScriptRoot '*.csproj') -Destination $benchmarkCopy
        Copy-Item -LiteralPath (Join-Path $source '.editorconfig') -Destination $copy
        @(Get-Item -LiteralPath (Join-Path $copy '.editorconfig')) + @(Get-ChildItem -LiteralPath $appSource -File -Recurse) | Sort-Object FullName | ForEach-Object {
            $path = [IO.Path]::GetRelativePath($copy, $_.FullName).Replace('\', '/')
            '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant(), $path
        } | Set-Content -LiteralPath (Join-Path $publishRoot "$variant-source.sha256")
        Publish-Project (Join-Path $copy 'JoJot.Benchmarks' 'JoJot.Benchmarks.csproj') (Join-Path $copy 'database')
        if ($Scenario -ne 'Database') {
            Add-StartupProbe $appSource
            Publish-Project (Join-Path $appSource 'JoJot.csproj') (Join-Path $copy 'wpf')
        }
    }
}
if (-not (Test-Path -LiteralPath $fixture)) {
    & (Join-Path $publishRoot 'baseline' 'database' 'JoJot.Benchmarks.exe') seed-startup $fixture
    if ($LASTEXITCODE -ne 0) {
        throw "Baseline database seed failed (exit $LASTEXITCODE)."
    }
}
if ($PrepareOnly) {
    return
}
$desktop = Get-Content -LiteralPath (Join-Path $fixture 'desktop.txt') -Raw
$fixtureFiles = @('jojot.db', 'startup-cache.json', '.clean-shutdown')
foreach ($file in $fixtureFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $fixture $file))) {
        throw "The fixture is missing $file."
    }
}

$scenarios = switch ($Scenario) {
    'Database' { 'database' }
    'Wpf' { 'wpf' }
    'WpfWithoutSnapshot' { 'wpf-without-snapshot' }
    'WpfBoth' { 'wpf'; 'wpf-without-snapshot' }
    'All' { 'database'; 'wpf'; 'wpf-without-snapshot' }
}
$fieldsByScenario = @{}
foreach ($benchmarkScenario in $scenarios) {
    $raw = Join-Path $runRoot "$benchmarkScenario.csv"
    if (Test-Path -LiteralPath $raw) {
        throw "Measurements already exist in $raw. Choose a new prepared run."
    }
    $fieldsByScenario[$benchmarkScenario] = if ($benchmarkScenario -ne 'database') {
        @('FirstRenderMs', 'EditorReadyMs', 'ReadyToUseMs', 'StartupCompleteMs')
        if ($WindowProfile) { 'EditorRenderingMs' }
    } else {
        @('ProcessToNotesMs', 'StorageToNotesMs')
    }
}
for ($round = 1 - $Warmups; $round -le $Samples; $round++) {
    $roundScenarios = @($scenarios)
    if ($round % 2 -eq 0) {
        [Array]::Reverse($roundScenarios)
    }
    # Vary first scenario and first variant independently over four rounds.
    $variants = if ([Math]::Floor($round / 2) % 2 -eq 0) { @('candidate', 'baseline') } else { @('baseline', 'candidate') }
    foreach ($benchmarkScenario in $roundScenarios) {
        $raw = Join-Path $runRoot "$benchmarkScenario.csv"
        $fields = $fieldsByScenario[$benchmarkScenario]
        foreach ($variant in $variants) {
            $data = Join-Path $runRoot 'runs' $benchmarkScenario "$round" $variant
            New-Item -ItemType Directory -Path $data | Out-Null
            foreach ($file in $fixtureFiles) {
                if ($benchmarkScenario -eq 'wpf-without-snapshot' -and $file -eq 'startup-cache.json') {
                    continue
                }
                Copy-Item -LiteralPath (Join-Path $fixture $file) -Destination $data
            }
            if ($benchmarkScenario -ne 'database') {
                $measurement = Invoke-Sample (Join-Path $publishRoot $variant 'wpf' 'JoJot.exe') $data @()
                $measurement | Add-Member -NotePropertyName ReadyToUseMs -NotePropertyValue (
                    [Math]::Max($measurement.FirstRenderMs, $measurement.EditorReadyMs))
                $desktopMessage = if ($desktop -eq 'default') {
                    'Virtual desktop: fallback mode (single-notepad)'
                } else {
                    "Virtual desktop: $desktop ("
                }
                $log = Get-ChildItem -LiteralPath $data -Filter 'jojot*.log' -File
                if (-not ($log | Select-String -SimpleMatch $desktopMessage | Select-Object -First 1)) {
                    throw "The app started on a different desktop from the fixture in $data."
                }
                if ($log | Select-String -Pattern '\[(ERROR|FATAL)\]' | Select-Object -First 1) {
                    throw "The application logged a startup or shutdown error in $data."
                }
            } else {
                $measurement = Invoke-Sample (Join-Path $publishRoot $variant 'database' 'JoJot.Benchmarks.exe') $data @(
                    'measure-startup-db', (Join-Path $data 'jojot.db'), $desktop)
            }
            $row = [ordered]@{ Round = $round; Variant = $variant; Notes = $measurement.Notes }
            foreach ($field in $fields) {
                if ($measurement.Notes -ne 100 -or $measurement.$field -le 0) {
                    throw "Invalid $benchmarkScenario startup measurement in $data."
                }
                $row[$field] = $measurement.$field
            }
            if ($WindowProfile -and $benchmarkScenario -ne 'database') {
                if (-not $measurement.Phases.WindowShown) {
                    throw 'Build and measure with -WindowProfile.'
                }
                foreach ($phase in $measurement.Phases.PSObject.Properties) {
                    $row[$phase.Name + 'Ms'] = $phase.Value
                }
                $row['FirstRenderLeft'] = $measurement.FirstRenderLeft
                $row['FirstRenderTop'] = $measurement.FirstRenderTop
                $row['FirstRenderWidth'] = $measurement.FirstRenderWidth
                $row['FirstRenderHeight'] = $measurement.FirstRenderHeight
            }
            [pscustomobject]$row | Export-Csv -LiteralPath $raw -NoTypeInformation -Append
        }
    }
}
if ($ControlGeometry) {
    $renderBounds = foreach ($benchmarkScenario in $scenarios | Where-Object { $_ -ne 'database' }) {
        Import-Csv -LiteralPath (Join-Path $runRoot "$benchmarkScenario.csv") | ForEach-Object {
            if (-not $_.FirstRenderLeft -or -not $_.FirstRenderTop -or
                [double]$_.FirstRenderWidth -le 0 -or [double]$_.FirstRenderHeight -le 0) {
                throw 'Controlled geometry probe did not report valid first-render bounds.'
            }
            "$($_.FirstRenderLeft),$($_.FirstRenderTop),$($_.FirstRenderWidth),$($_.FirstRenderHeight)"
        }
    }
    $rectangles = @($renderBounds | Sort-Object -Unique)
    if ($rectangles.Count -gt 1) {
        throw "First-render bounds differed across controlled samples: $($rectangles -join '; ')."
    }
}
foreach ($benchmarkScenario in $scenarios) {
    $raw = Join-Path $runRoot "$benchmarkScenario.csv"
    $fields = $fieldsByScenario[$benchmarkScenario]
    $statistics = foreach ($variant in @('baseline', 'candidate')) {
        $measured = Import-Csv -LiteralPath $raw | Where-Object { [int]$_.Round -gt 0 -and $_.Variant -eq $variant }
        foreach ($field in $fields) {
            $values = @($measured | ForEach-Object { [double]$_.$field } | Sort-Object)
            $middle = [int][Math]::Floor($values.Count / 2)
            $median = if ($values.Count % 2 -eq 0) { ($values[$middle - 1] + $values[$middle]) / 2 } else { $values[$middle] }
            [pscustomobject]@{
                Variant = $variant
                Milestone = $field
                Samples = $values.Count
                MedianMs = [Math]::Round($median, 2)
                P95Ms = [Math]::Round($values[[int][Math]::Ceiling($values.Count * 0.95) - 1], 2)
                MinMs = [Math]::Round($values[0], 2)
                MaxMs = [Math]::Round($values[-1], 2)
            }
        }
    }
    $statistics | Export-Csv -LiteralPath (Join-Path $runRoot "$benchmarkScenario-summary.csv") -NoTypeInformation
    $statistics | Format-Table -AutoSize
}
