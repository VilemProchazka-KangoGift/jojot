param([switch] $SnapshotOnly, [switch] $PublishOnly)
$ErrorActionPreference = 'Stop'
$env:DOTNET_ROOT = (Resolve-Path .local/sqlite-migration/dotnet11).Path
$env:DOTNET_CLI_HOME = (Resolve-Path .local/sqlite-migration/cli).Path
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = (Resolve-Path .local/avalonia-nuget).Path
$msvc = 'C:/Program Files/Microsoft Visual Studio/18/Community/VC/Tools/MSVC/14.51.36231'
$windowsSdk = 'C:/Program Files (x86)/Windows Kits/10/Lib/10.0.19041.0'
$env:Path = "$msvc/bin/Hostx64/x64;$env:Path"
$env:LIB = "$msvc/lib/onecore/x64;$windowsSdk/ucrt/x64;$windowsSdk/um/x64"
$build = if ($PublishOnly) {
    (Resolve-Path .local/avalonia-hybrid-profile/reproduce).Path
} else {
    (New-Item -ItemType Directory .local/avalonia-hybrid-profile/reproduce).FullName
}
$source = Join-Path $build 'source'
if (-not $PublishOnly) {
New-Item -ItemType Directory $source | Out-Null
Copy-Item -LiteralPath .editorconfig -Destination $source
foreach ($relative in 'experiments/JoJot.Avalonia', 'experiments/JoJot.Storage') {
    robocopy (Resolve-Path $relative).Path (Join-Path $source $relative) /E /XD bin obj /XF '*.user' '*.log' '*.md' /NJH /NJS /NDL /NFL /NP
    if ($LASTEXITCODE -gt 7) { throw "Experiment source copy failed ($LASTEXITCODE)." }
}
$repo = (Get-Location).Path
$storageProject = Join-Path $repo 'experiments/JoJot.Storage/JoJot.Storage.csproj'
$storageDirectory = Split-Path -Parent $storageProject
$layoutProject = Join-Path $repo 'experiments/JoJot.Avalonia/JoJot.Avalonia.csproj'
$items = ([xml](Get-Content -LiteralPath $storageProject -Raw)).SelectNodes('//Compile[@Include] | //EmbeddedResource[@Include]')
$items = @($items) + @(([xml](Get-Content -LiteralPath $layoutProject -Raw)).SelectNodes('//AvaloniaResource[@Include]'))
foreach ($item in $items) {
    foreach ($include in $item.Include.Split(';')) {
        $absolute = (Resolve-Path -LiteralPath (Join-Path $storageDirectory $include)).Path
        $relative = [IO.Path]::GetRelativePath($repo, $absolute)
        if (-not $relative.StartsWith('JoJot' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected linked source outside JoJot: $relative"
        }
        $destination = Join-Path $source $relative
        New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $absolute -Destination $destination
    }
}
Copy-Item -LiteralPath 'JoJot/JoJot.csproj' -Destination (Join-Path $source 'JoJot/JoJot.csproj')
Get-ChildItem -LiteralPath $source -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($source, $_.FullName).Replace('\','/')
    "$((Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant())  $relative"
} | Set-Content -LiteralPath (Join-Path $build 'source.sha256')
}
if ($SnapshotOnly) { return }
$project = Join-Path $source 'experiments/JoJot.Avalonia/JoJot.Avalonia.csproj'
$dotnet = "$env:DOTNET_ROOT/dotnet.exe"
& $dotnet publish $project -c Release -r win-x64 --self-contained -p:PublishAot=true -p:PublishReadyToRun=false -p:IlcUseEnvironmentalTools=true -p:UseSharedCompilation=false -o "$build/native" | Tee-Object -FilePath "$build/publish-native.log"
if ($LASTEXITCODE -ne 0) { throw "NativeAOT publish exited $LASTEXITCODE." }
& $dotnet package list --project $project --include-transitive --vulnerable --no-restore --format json | Tee-Object -FilePath "$build/dependency-audit.json"
if ($LASTEXITCODE -ne 0) { throw "NuGet audit exited $LASTEXITCODE." }
