# Builds Power Ring and installs it for the current user in %LOCALAPPDATA%\Programs\PowerRing, then starts it.
# Safe to re-run: a running Power Ring is closed first (--exit) and replaced. Your ring.json in %APPDATA%\PowerRing is kept.
# Uninstall: tray icon > Exit, untick "Start with Windows" first if you ticked it, then delete the folder above.
param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\PowerRing'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $Destination 'PowerRing.exe'
if (Test-Path $exe) {
    Start-Process $exe -ArgumentList '--exit' -Wait
    for ($i = 0; $i -lt 40 -and (Get-Process PowerRing -ErrorAction SilentlyContinue | Where-Object Path -eq $exe); $i++) { Start-Sleep -Milliseconds 100 }
}
& dotnet publish (Join-Path $repo 'src\PowerRing\PowerRing.csproj') -c Release -o $Destination --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
Start-Process $exe
"Power Ring installed in $Destination and started (tray icon)."
"Settings: $(Join-Path $env:APPDATA 'PowerRing\ring.json')  (tray icon > Edit ring.json)"
