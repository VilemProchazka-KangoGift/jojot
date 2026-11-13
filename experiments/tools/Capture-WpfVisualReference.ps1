#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Dotnet,
    [string] $ReleaseTag = 'v2026.11.2',
    [switch] $PrepareOnly
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$root = Join-Path $repo ('.local/visual-parity/wpf-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$data = Join-Path $root 'data'
$output = Join-Path $root 'images'
New-Item -ItemType Directory -Path $root, $data, $output | Out-Null
$archive = Join-Path $root 'wpf.zip'
git -C $repo archive --format=zip -o $archive "$ReleaseTag" .editorconfig JoJot
if ($LASTEXITCODE -ne 0) { throw 'Could not export WPF source.' }
Expand-Archive -LiteralPath $archive -DestinationPath $source
$appSource = Join-Path $source 'JoJot'
Copy-Item -LiteralPath (Join-Path $repo 'experiments/testing/VisualWpfProbe/VisualReference.cs.txt') -Destination (Join-Path $appSource 'Views/VisualReference.cs')
function Replace-Once([string] $Path, [string] $From, [string] $To) {
    $s = [IO.File]::ReadAllText($Path)
    $i = $s.IndexOf($From, [StringComparison]::Ordinal)
    if ($i -lt 0 -or $s.IndexOf($From, $i + $From.Length, [StringComparison]::Ordinal) -ge 0) { throw "Instrumentation point changed: $From" }
    [IO.File]::WriteAllText($Path, $s.Remove($i, $From.Length).Insert($i, $To))
}
$envFile = Join-Path $appSource 'Services/AppEnvironment.cs'
Replace-Once $envFile 'Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),' 'Environment.GetEnvironmentVariable("JOJOT_VISUAL_DATA") ?? throw new InvalidOperationException("A disposable visual directory is required."),'
Replace-Once $envFile 'private const string FolderName = "JoJot";' 'private const string FolderName = "";'
Replace-Once $envFile 'private const string Suffix = "";' 'private static readonly string Suffix = $".VisualProbe.{Environment.ProcessId}";'
$app = Join-Path $appSource 'App.xaml.cs'
Replace-Once $app 'window.Show();' 'window.Width = 850; window.Height = 560; window.ShowActivated = false; window.Show();'
Replace-Once $app 'WindowActivationHelper.ActivateWindow(window);' '// Visual probe does not activate the window.'
Replace-Once $app 'await HotkeyService.InitializeAsync(OnGlobalHotkeyPressed);' '// Visual probe does not register a global hotkey.'
Replace-Once $app 'LogService.Info("Startup complete in {ElapsedMs}ms", _startupStopwatch.ElapsedMilliseconds);' 'LogService.Info("Startup complete in {ElapsedMs}ms", _startupStopwatch.ElapsedMilliseconds); await mainWindow.CaptureVisualReferenceAsync();'
& $Dotnet build (Join-Path $appSource 'JoJot.csproj') -c Release -m:1 -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Instrumented WPF build failed.' }
$wpf = Join-Path $appSource 'bin/Release/net11.0-windows/JoJot.exe'
if ($PrepareOnly) { Write-Host "Instrumented WPF prepared at $root"; return }
$start = [Diagnostics.ProcessStartInfo]::new($wpf)
$start.UseShellExecute = $false
$start.Environment['JOJOT_VISUAL_DATA'] = $data
$start.Environment['JOJOT_VISUAL_OUTPUT'] = $output
$process = [Diagnostics.Process]::Start($start)
if (-not $process.WaitForExit(60000)) { $process.Kill($true); $process.WaitForExit(); throw "Visual probe timed out (PID $($process.Id))." }
if ($process.ExitCode -ne 0) { throw "Visual probe failed ($($process.ExitCode)); see $output" }
Write-Host "WPF client renders: $output"
