#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $ReleaseTag = 'v2026.11.2',
    [string] $Dotnet = 'dotnet',
    [string] $OutputRoot = (Join-Path $PSScriptRoot '..' '..' '.local' 'avalonia-aot' 'wpf')
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..' '..')).Path
$root = (New-Item -ItemType Directory -Path (Join-Path $OutputRoot ([Guid]::NewGuid().ToString('N')))).FullName
$archive = Join-Path $root 'source.zip'
$commit = git -C $repo rev-parse --verify "$ReleaseTag^{commit}"
if ($LASTEXITCODE -ne 0) {
    throw "Could not resolve release $ReleaseTag."
}
$commit | Set-Content -LiteralPath (Join-Path $root 'wpf-release-commit.txt')
git -C $repo archive --format=zip -o $archive $commit .editorconfig JoJot
if ($LASTEXITCODE -ne 0) {
    throw "Could not export release $ReleaseTag."
}
$source = Join-Path $root 'source'
Expand-Archive -LiteralPath $archive -DestinationPath $source

$tools = (New-Item -ItemType Directory -Path (Join-Path $root 'probe-tools')).FullName
$benchmark = Join-Path $repo 'JoJot.Benchmarks'
Copy-Item -Path (Join-Path $benchmark '*.cs'), (Join-Path $benchmark '*.csproj') -Destination $tools
Copy-Item -LiteralPath (Join-Path $benchmark 'Compare-Startup.ps1') -Destination $tools
$probeTemplate = Get-Content -LiteralPath (Join-Path $benchmark 'StartupProbe.cs.txt') -Raw
$probeTemplate = $probeTemplate.Replace('    internal static IpcService.SendResult RejectSecondInstance()', @"
    private static NativeRect _nativeFrame;
    private static NativeRect _clientFrame;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetWindowRect(IntPtr handle, out NativeRect rect);

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetClientRect(IntPtr handle, out NativeRect rect);

    internal static IpcService.SendResult RejectSecondInstance()
"@)
$probeTemplate = $probeTemplate.Replace('            FirstRender.TrySetResult(elapsed);', @"
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (GetWindowRect(handle, out _nativeFrame) == 0 || GetClientRect(handle, out _clientFrame) == 0)
            {
                throw new InvalidOperationException("Could not measure the first-render window geometry.");
            }
            FirstRender.TrySetResult(elapsed);
"@)
$probeTemplate = $probeTemplate.Replace('            Notes = _loadedTabs,', @"
            Notes = _loadedTabs,
            NativeLeft = _nativeFrame.Left,
            NativeTop = _nativeFrame.Top,
            NativeWidth = _nativeFrame.Right - _nativeFrame.Left,
            NativeHeight = _nativeFrame.Bottom - _nativeFrame.Top,
            ClientWidth = _clientFrame.Right - _clientFrame.Left,
            ClientHeight = _clientFrame.Bottom - _clientFrame.Top,
"@)
if (-not $probeTemplate.Contains('ClientHeight = _clientFrame.Bottom - _clientFrame.Top,') -or
    -not $probeTemplate.Contains('GetWindowRect(handle, out _nativeFrame)') -or
    -not $probeTemplate.Contains('private static extern int GetWindowRect')) {
    throw 'The WPF probe changed and its geometry hooks must be updated.'
}
Set-Content -LiteralPath (Join-Path $tools 'StartupProbe.cs.txt') -Value $probeTemplate
& (Join-Path $tools 'Compare-Startup.ps1') -Baseline $source -Candidate $source -Dotnet $Dotnet `
    -OutputRoot $root -PrepareOnly -Scenario Wpf -WindowProfile -ControlGeometry
