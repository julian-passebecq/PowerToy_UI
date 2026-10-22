# Installs, switches or rolls back Power Ops for the current user. No administrator rights, no registry, no services.
#
#   .\install.ps1 -Source PowerOps-2.0.0-win-x64.zip        install that version and make it current
#   .\install.ps1                                           (inside an extracted package) install the package it ships in
#   .\install.ps1 -Rollback                                 make the previously current version current again
#   .\install.ps1 -Status                                   show installed versions
#
# Works in Windows PowerShell 5.1 and PowerShell 7.
# Layout under -InstallRoot (default %LOCALAPPDATA%\Programs\PowerOps):
#   versions\<version>\   one immutable folder per version (never overwritten; a different package under an existing
#                          version number is refused)
#   state.json            { current, previous } - the only file a switch or rollback changes
#   PowerOps.cmd          starts the current version (passes arguments through, e.g. --data-dir)
#
# User storage is NEVER read or written: workspace data lives outside the install folder, in
# %LOCALAPPDATA%\JUtilityPalette (default) or the folder given with --data-dir. Uninstall = delete -InstallRoot.
param(
  [string] $Source = '',
  [string] $InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\PowerOps'),
  [switch] $Rollback,
  [switch] $Status
)
$ErrorActionPreference = 'Stop'
$exeName = 'JUtilityPalette.exe'
$versions = Join-Path $InstallRoot 'versions'
$statePath = Join-Path $InstallRoot 'state.json'

function Read-State {
  if (-not (Test-Path $statePath)) { return [ordered]@{ format = 'powerops-install'; schemaVersion = 1; current = $null; previous = $null } }
  $s = Get-Content $statePath -Raw | ConvertFrom-Json
  if ($s.format -ne 'powerops-install' -or $s.schemaVersion -ne 1) { throw "Unknown install state in $statePath; nothing changed." }
  return [ordered]@{ format = $s.format; schemaVersion = 1; current = $s.current; previous = $s.previous }
}
function Write-State($state) {
  $tmp = "$statePath.tmp"
  $state | ConvertTo-Json | Set-Content $tmp -Encoding utf8
  Move-Item $tmp $statePath -Force
  $launcher = Join-Path $InstallRoot 'PowerOps.cmd'
  "@echo off`r`nstart `"`" `"%~dp0versions\$($state.current)\$exeName`" %*`r`n" | Set-Content $launcher -Encoding ascii -NoNewline
}
function Assert-NotRunning {
  $running = @(Get-Process JUtilityPalette -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($InstallRoot, [StringComparison]::OrdinalIgnoreCase) })
  if ($running.Count -gt 0) { throw "Power Ops from $InstallRoot is running (pid $($running.Id -join ', ')). Close it first; nothing changed." }
}
function Tree-Hash([string]$dir) {
  $lines = Get-ChildItem $dir -Recurse -File | Sort-Object { $_.FullName.Substring($dir.Length) } | ForEach-Object { $_.FullName.Substring($dir.Length).TrimStart('\') + ' ' + (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
  $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
  $sha = [Security.Cryptography.SHA256]::Create()
  try { return (($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join '') } finally { $sha.Dispose() }
}

if ($Status) {
  $state = Read-State
  "Install root: $InstallRoot"
  "Current:  $($state.current)"; "Previous: $($state.previous)"
  if (Test-Path $versions) { 'Installed: ' + ((Get-ChildItem $versions -Directory | ForEach-Object Name) -join ', ') }
  return
}

if ($Rollback) {
  $state = Read-State
  if (-not $state.previous) { throw 'No previous version to roll back to; nothing changed.' }
  if (-not (Test-Path (Join-Path $versions "$($state.previous)\$exeName"))) { throw "Previous version $($state.previous) is missing; nothing changed." }
  Assert-NotRunning
  $from = $state.current
  $state.current = $state.previous; $state.previous = $from
  Write-State $state
  "Rolled back: $from -> $($state.current) (user data untouched)"
  return
}

if (-not $Source) { $Source = $PSScriptRoot }
$staging = $null
try {
  if ((Test-Path $Source -PathType Leaf) -and $Source -like '*.zip') {
    $staging = Join-Path ([IO.Path]::GetTempPath()) ('powerops-install-' + [guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $Source -DestinationPath $staging
    $Source = $staging
  }
  $exe = Join-Path $Source $exeName
  if (-not (Test-Path $exe)) { throw "No $exeName in $Source; nothing changed." }
  $version = (Get-Item $exe).VersionInfo.ProductVersion -replace '\+.*$', ''
  if (-not $version) { throw 'The package has no product version; nothing changed.' }
  $target = Join-Path $versions $version
  $state = Read-State
  if (Test-Path $target) {
    if ((Tree-Hash $target) -ne (Tree-Hash $Source)) { throw "Version $version is already installed with different files. Versions are immutable: build a new version number. Nothing changed." }
    "Version $version already installed (identical files)."
  } else {
    New-Item -ItemType Directory -Force $versions | Out-Null
    $partial = "$target.partial"
    if (Test-Path $partial) { Remove-Item -LiteralPath $partial -Recurse -Force }
    Copy-Item -Path $Source -Destination $partial -Recurse
    Move-Item -LiteralPath $partial -Destination $target
    "Installed $version in $target"
  }
  Assert-NotRunning
  if ($state.current -ne $version) { $state.previous = $state.current; $state.current = $version; Write-State $state }
  "Current: $($state.current)   Previous: $($state.previous)"
  "Start: `"$(Join-Path $InstallRoot 'PowerOps.cmd')`"   (user data stays in %LOCALAPPDATA%\JUtilityPalette or your --data-dir)"
}
finally {
  if ($staging -and (Test-Path $staging)) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
