# Run only against the frozen source copy produced by Publish-Probe.ps1 -SnapshotOnly.
param([Parameter(Mandatory)] [string] $SnapshotDirectory)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $SnapshotDirectory).Path
if ($root -notlike '*\.local\avalonia-hybrid-profile\*\source') { throw 'Choose an isolated hybrid-profile source snapshot.' }
$directory = Join-Path $root 'experiments/JoJot.Avalonia'
if (Test-Path (Join-Path $directory 'HybridProbe.cs')) { throw 'Snapshot already has the probe.' }
$path = Join-Path $directory 'Program.cs'
$code = Get-Content -LiteralPath $path -Raw
$needle = '                result = StartupTrace.TraceSubsystems(builder)'
if (-not $code.Contains($needle)) { throw 'Unexpected Program.cs version.' }
$insert = @"
                if (HybridProbe.Enabled)
                {
                    var hybridOptions = new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] };
                    builder.With(hybridOptions);
                    HybridProbe.MutateOptions = () => hybridOptions.RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software];
                    HybridProbe.Reinitialize = builder.WindowingSubsystemInitializer!;
                }
"@
$code.Replace($needle, $insert + "`r`n" + $needle) | Set-Content -LiteralPath $path
$path = Join-Path $directory 'BenchmarkProbe.cs'
$code = Get-Content -LiteralPath $path -Raw
$needle = '        if (Environment.GetEnvironmentVariable("JOJOT_RENDER_WORKLOAD") == "1")'
if (-not $code.Contains($needle)) { throw 'Unexpected BenchmarkProbe.cs version.' }
$code = $code.Replace($needle, '        var originalWindow = window;' + "`r`n" + '        if (HybridProbe.Enabled) window = await HybridProbe.TransitionAsync(window, output);' + "`r`n" + $needle)
$code = $code.Replace('            await RecordRenderWorkloadAsync(window, output);', '            await RecordRenderWorkloadAsync(window, output);' + "`r`n" + '        if (!ReferenceEquals(window, originalWindow)) window.Close();')
$code.Replace('    private static async Task WaitForRenderedBatchAsync', '    internal static async Task WaitForRenderedBatchAsync') | Set-Content -LiteralPath $path
Copy-Item (Join-Path $PSScriptRoot 'HybridProbe.cs.txt') (Join-Path $directory 'HybridProbe.cs')
Get-ChildItem -LiteralPath $root -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
    "$((Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant())  $([IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/'))"
} | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $root) 'source.sha256')
