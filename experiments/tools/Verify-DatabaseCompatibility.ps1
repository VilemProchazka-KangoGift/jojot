#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $NativeExecutable,
    [Parameter(Mandatory)][string] $Dotnet,
    [string] $ReleaseTag = 'v2026.11.2',
    [switch] $PrepareOnly
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$native = (Resolve-Path -LiteralPath $NativeExecutable).Path
$root = Join-Path $repo ('.local/compat-roundtrip/' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$data = Join-Path $root 'data'
New-Item -ItemType Directory -Path $root, $data | Out-Null
$archive = Join-Path $root 'wpf.zip'
git -C $repo archive --format=zip -o $archive "$ReleaseTag" .editorconfig JoJot
if ($LASTEXITCODE -ne 0) { throw 'Could not export WPF source.' }
Expand-Archive -LiteralPath $archive -DestinationPath $source
$appSource = Join-Path $source 'JoJot'
Copy-Item -LiteralPath (Join-Path $repo 'experiments/testing/CompatibilityWpfProbe/CompatibilityProbe.cs.txt') -Destination (Join-Path $appSource 'CompatibilityProbe.cs')
function Replace-Once([string] $Path, [string] $From, [string] $To) {
    $s = [IO.File]::ReadAllText($Path)
    $i = $s.IndexOf($From, [StringComparison]::Ordinal)
    if ($i -lt 0 -or $s.IndexOf($From, $i + $From.Length, [StringComparison]::Ordinal) -ge 0) { throw "Instrumentation point changed: $From" }
    [IO.File]::WriteAllText($Path, $s.Remove($i, $From.Length).Insert($i, $To))
}
$envFile = Join-Path $appSource 'Services/AppEnvironment.cs'
Replace-Once $envFile 'Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),' 'Environment.GetEnvironmentVariable("JOJOT_COMPAT_DATA") ?? throw new InvalidOperationException("A disposable compatibility directory is required."),'
Replace-Once $envFile 'private const string FolderName = "JoJot";' 'private const string FolderName = "";'
Replace-Once $envFile 'private const string Suffix = "";' 'private static readonly string Suffix = $".CompatibilityProbe.{Environment.ProcessId}";'
$app = Join-Path $appSource 'App.xaml.cs'
Replace-Once $app 'window.Show();' 'CompatibilityProbe.Observe(window); window.Show();'
Replace-Once $app 'WindowActivationHelper.ActivateWindow(window);' '// Compatibility probe does not activate the window.'
Replace-Once $app 'LogPhase("tabs-loaded");' 'CompatibilityProbe.EditorReady(window); LogPhase("tabs-loaded");'
Replace-Once $app 'await HotkeyService.InitializeAsync(OnGlobalHotkeyPressed);' '// Compatibility probe does not register a global hotkey.'
Replace-Once $app 'LogService.Info("Startup complete in {ElapsedMs}ms", _startupStopwatch.ElapsedMilliseconds);' 'LogService.Info("Startup complete in {ElapsedMs}ms", _startupStopwatch.ElapsedMilliseconds); CompatibilityProbe.Finish(this);'
Replace-Once $app 'LogService.Error("Fatal error during application startup", ex);' 'LogService.Error("Fatal error during application startup", ex); CompatibilityProbe.Fail(this, ex);'
& $Dotnet build (Join-Path $appSource 'JoJot.csproj') -c Release -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Instrumented WPF build failed.' }
$wpf = Join-Path $appSource 'bin/Release/net11.0-windows/JoJot.exe'
if ($PrepareOnly) { Write-Host "Instrumented WPF prepared at $root"; return }
function Invoke-Probe([string] $File, [string[]] $Arguments, [hashtable] $Variables) {
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.UseShellExecute = $false
    foreach ($arg in $Arguments) { [void] $start.ArgumentList.Add($arg) }
    foreach ($key in $Variables.Keys) { $start.Environment[$key] = $Variables[$key] }
    $process = [Diagnostics.Process]::Start($start)
    if (-not $process.WaitForExit(30000)) { $process.Kill($true); $process.WaitForExit(); throw "Compatibility probe timed out (PID $($process.Id))." }
    if ($process.ExitCode -ne 0) { throw "Compatibility probe failed ($($process.ExitCode)): $File" }
}
$first = 'WPF compatibility seed č 🐇'
$second = 'Avalonia compatibility edit Ω 🐇'
Invoke-Probe $wpf @() @{ JOJOT_COMPAT_DATA = $data; JOJOT_COMPAT_REPLACEMENT = $first }
$result = Get-Content (Join-Path $data 'compat-wpf-result.json') -Raw | ConvertFrom-Json
Invoke-Probe $native @('--data-dir', $data, '--desktop-guid', $result.DesktopGuid, '--compat-expected', $first, '--compat-replacement', $second) @{}
Invoke-Probe $wpf @() @{ JOJOT_COMPAT_DATA = $data; JOJOT_COMPAT_EXPECTED = $second }
Write-Host "WPF editor → Avalonia editor → WPF editor passed; disposable artifacts: $root"
