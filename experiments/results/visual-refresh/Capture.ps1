param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$Name)
$ErrorActionPreference='Stop'
$root=(New-Item -ItemType Directory ".local/avalonia-visual-refresh/$Name").FullName
Copy-Item .local/avalonia-visual-refresh/fixture/jojot.db $root
$env:DOTNET_ROOT=(Resolve-Path .local/sqlite-migration/dotnet11).Path
$p=Start-Process -FilePath (Resolve-Path $Executable).Path -ArgumentList @('--data-dir',$root,'--visual-output',"$root/images",'--desktop-guid','default','--benchmark-width','1000','--benchmark-height','680') -WindowStyle Hidden -PassThru -RedirectStandardError "$root/stderr.txt" -RedirectStandardOutput "$root/stdout.txt"
if(-not $p.WaitForExit(45000)){throw "Capture still running PID $($p.Id)"}
Get-Content "$root/stderr.txt"
if($p.ExitCode -ne 0){throw "Capture exit $($p.ExitCode)"}
"Captured $((Get-ChildItem "$root/images" -Filter *.png).Count) images to $root."
