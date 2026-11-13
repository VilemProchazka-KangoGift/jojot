$ErrorActionPreference = 'Stop'
$directory = (New-Item -ItemType Directory (Join-Path $PSScriptRoot ('sessions/' + [guid]::NewGuid().ToString('N')))).FullName
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixture/jojot.db') -Destination $directory
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSScriptRoot 'native/JoJot.Avalonia.exe'))
$start.ArgumentList.Add('--data-dir')
$start.ArgumentList.Add($directory)
$start.ArgumentList.Add('--desktop-guid')
$start.ArgumentList.Add('default')
$start.ArgumentList.Add('--benchmark-width')
$start.ArgumentList.Add('1000')
$start.ArgumentList.Add('--benchmark-height')
$start.ArgumentList.Add('680')
$process = [Diagnostics.Process]::Start($start)
Write-Host "Visual refresh preview started with disposable notes at $directory"
$process.Dispose()
