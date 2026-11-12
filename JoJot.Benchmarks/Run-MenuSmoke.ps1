param([Parameter(Mandatory = $true)][string]$Dotnet)
$ErrorActionPreference = 'Stop'
function Replace-Once([string]$Text, [string]$Old, [string]$New) {
    $first = $Text.IndexOf($Old, [StringComparison]::Ordinal)
    if ($first -lt 0 -or $Text.IndexOf($Old, $first + $Old.Length, [StringComparison]::Ordinal) -ge 0) {
        throw "Expected exactly one smoke instrumentation anchor: $Old"
    }
    return $Text.Substring(0, $first) + $New + $Text.Substring($first + $Old.Length)
}
$repo = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repo ('.local/menu-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $target | Out-Null
Get-ChildItem (Join-Path $repo 'JoJot') -Force | Where-Object { $_.Name -notin @('bin', 'obj') } | Copy-Item -Destination $target -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'MenuSmoke.cs.txt') (Join-Path $target 'MenuSmoke.cs') -Force
$project = Join-Path $target 'JoJot.csproj'
$xml = Replace-Once ([IO.File]::ReadAllText($project)) '<StartupObject>JoJot.Program</StartupObject>' '<StartupObject>Program</StartupObject>'
[IO.File]::WriteAllText($project, $xml)
$appXaml = Join-Path $target 'App.xaml'
$xaml = Replace-Once ([IO.File]::ReadAllText($appXaml)) 'Startup="OnAppStartup"' ''
[IO.File]::WriteAllText($appXaml, $xaml)
$environment = Join-Path $target 'Services/AppEnvironment.cs'
$source = Replace-Once ([IO.File]::ReadAllText($environment)) 'Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),' 'Environment.GetEnvironmentVariable("JOJOT_MENU_SMOKE_DATA")!,'
[IO.File]::WriteAllText($environment, $source)
$env:JOJOT_MENU_SMOKE_DATA = Join-Path $target 'fixture-data'
& $Dotnet run --project $project
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
