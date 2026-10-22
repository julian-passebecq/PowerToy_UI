# Native install -> reopen -> rollback -> uninstall check for the Power Ops package and scripts/install.ps1.
# Everything is isolated: a temporary -InstallRoot (never %LOCALAPPDATA%\Programs\PowerOps) and a temporary --data-dir
# (never the personal workspace). install.ps1 runs under Windows PowerShell 5.1, as the package README tells users.
# Proves: install/switch/rollback/uninstall never change one byte of user storage; data written by the new version is
# read by the rolled-back version; versions are immutable; a running instance blocks a switch.
param(
  [Parameter(Mandatory = $true)] [string] $NewPackage,
  [Parameter(Mandatory = $true)] [string] $OldPackage,
  [string] $JsonOut = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]; $CT = [System.Windows.Automation.ControlType]
$id = [guid]::NewGuid().ToString('N').Substring(0, 8)
$installRoot = Join-Path $env:TEMP "powerops-install-test-$id"
$dataDir = Join-Path $env:TEMP "powerops-lifecycle-data-$id"
New-Item -ItemType Directory $dataDir | Out-Null
$defaultDir = Join-Path $env:LOCALAPPDATA 'JUtilityPalette'
$scratch = Join-Path $env:TEMP "powerops-lifecycle-scratch-$id"; New-Item -ItemType Directory $scratch | Out-Null
$token = "lifecycle-$id"

$r = [ordered]@{}
function Rec([string]$name, $ok, [string]$detail = '') { $r[$name] = [ordered]@{ result = $(if ($ok) { 'PASS' } else { 'FAIL' }); detail = $detail } }
function DirHash([string]$d) {
  if (-not (Test-Path $d)) { return 'absent' }
  $lines = Get-ChildItem $d -Recurse -File | Sort-Object { $_.FullName.Substring($d.Length) } | ForEach-Object { $_.FullName.Substring($d.Length) + ' ' + (Get-FileHash $_.FullName).Hash }
  (Get-FileHash -InputStream ([IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))))).Hash
}
function Installer([string[]]$arguments) {
  $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scratch 'install.ps1') -InstallRoot $installRoot @arguments 2>&1
  return [pscustomobject]@{ Code = $LASTEXITCODE; Text = (($out | ForEach-Object { "$_" }) -join ' | ') }
}
function State { Get-Content (Join-Path $installRoot 'state.json') -Raw | ConvertFrom-Json }
function Cond($prop, $val) { New-Object System.Windows.Automation.PropertyCondition($prop, $val) }
function Find($parent, [string]$name, $type, [int]$tries = 40) {
  $c = New-Object System.Windows.Automation.AndCondition((Cond $A::NameProperty $name), (Cond $A::ControlTypeProperty $type))
  for ($i = 0; $i -lt $tries; $i++) { $e = $parent.FindFirst($TS::Descendants, $c); if ($e) { return $e }; Start-Sleep -Milliseconds 150 }
  throw "UI element not found: $name"
}
function Invoke-El($e) { $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 400 }
# Starts the CURRENT version through the installed PowerOps.cmd launcher and returns the process it started.
function Start-Installed {
  $known = @(Get-Process JUtilityPalette -ErrorAction SilentlyContinue | ForEach-Object Id)
  # cmd /c strips the outer quote pair when the line starts with a quote: wrap the whole command once more.
  Start-Process cmd.exe -ArgumentList "/c `"`"$(Join-Path $installRoot 'PowerOps.cmd')`" --data-dir `"$dataDir`"`"" -WindowStyle Hidden
  $p = $null
  for ($i = 0; $i -lt 80 -and -not $p; $i++) { Start-Sleep -Milliseconds 250; $p = Get-Process JUtilityPalette -ErrorAction SilentlyContinue | Where-Object { $known -notcontains $_.Id -and $_.Path -like "$installRoot*" } | Select-Object -First 1 }
  if (-not $p) { throw 'installed Power Ops did not start' }
  for ($i = 0; $i -lt 80 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
  Start-Sleep -Seconds 3; $p.Refresh(); return $p
}
function Stop-App($p) { [void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000); if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; return $false }; return $true }
function Has-Capture($p) {
  $main = $A::FromHandle($p.MainWindowHandle)
  Invoke-El (Find $main 'Capture' $CT::Button)
  for ($i = 0; $i -lt 30; $i++) { if (@($main.FindAll($TS::Descendants, (Cond $A::ControlTypeProperty $CT::Text)) | Where-Object { $_.Current.Name -like "*$token*" }).Count -gt 0) { return $true }; Start-Sleep -Milliseconds 200 }
  return $false
}

$defaultBefore = DirHash $defaultDir
$realInstall = Join-Path $env:LOCALAPPDATA 'Programs\PowerOps'; $realBefore = DirHash $realInstall
$p = $null
try {
  $oldDir = Join-Path $scratch 'old'; Expand-Archive -LiteralPath $OldPackage -DestinationPath $oldDir
  $newDir = Join-Path $scratch 'new'; Expand-Archive -LiteralPath $NewPackage -DestinationPath $newDir
  Copy-Item (Join-Path $newDir 'install.ps1') (Join-Path $scratch 'install.ps1')   # the installer shipped in the new package
  $oldVersion = ((Get-Item (Join-Path $oldDir 'JUtilityPalette.exe')).VersionInfo.ProductVersion -replace '\+.*$', '')
  $newVersion = ((Get-Item (Join-Path $newDir 'JUtilityPalette.exe')).VersionInfo.ProductVersion -replace '\+.*$', '')
  Rec 'packages: two distinct versions' ($oldVersion -and $newVersion -and $oldVersion -ne $newVersion) "old=$oldVersion new=$newVersion"

  # 1. Install the previous version, create user data with it.
  $x = Installer @('-Source', $OldPackage)
  Rec "install ${oldVersion}: current" ($x.Code -eq 0 -and (State).current -eq $oldVersion) $x.Text
  $p = Start-Installed
  Rec "run ${oldVersion}: started from the install folder" ($p.Path -like "$installRoot\versions\$oldVersion\*") $p.Path
  $main = $A::FromHandle($p.MainWindowHandle)
  Invoke-El (Find $main 'Capture' $CT::Button); Invoke-El (Find $main '+ Capture' $CT::Button)
  (Find $main 'Capture title' $CT::Edit).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("Synthetic $token"); Start-Sleep -Seconds 2
  Rec "run ${oldVersion}: closed cleanly" (Stop-App $p); $p = $null
  $h1 = DirHash $dataDir
  $oldTree = DirHash (Join-Path $installRoot "versions\$oldVersion")

  # 2. Install the new version: storage byte-identical, old version kept untouched.
  $x = Installer @('-Source', $NewPackage)
  $s = State
  Rec "install ${newVersion}: current, previous kept" ($x.Code -eq 0 -and $s.current -eq $newVersion -and $s.previous -eq $oldVersion) $x.Text
  Rec "install ${newVersion}: user storage byte-identical" ((DirHash $dataDir) -eq $h1)
  Rec "install ${newVersion}: previous version folder unchanged" ((DirHash (Join-Path $installRoot "versions\$oldVersion")) -eq $oldTree)
  Rec "install ${newVersion}: installed files equal the package" ((DirHash (Join-Path $installRoot "versions\$newVersion")) -eq (DirHash $newDir))

  # 3. Reopen with the new version: same data.
  $p = Start-Installed
  Rec "reopen ${newVersion}: started from its version folder" ($p.Path -like "$installRoot\versions\$newVersion\*") $p.Path
  Rec "reopen ${newVersion}: capture made by $oldVersion is there" (Has-Capture $p)
  # A running instance blocks switching.
  $x = Installer @('-Rollback')
  Rec 'rollback while running: refused, nothing changed' ($x.Code -ne 0 -and (State).current -eq $newVersion) $x.Text
  Rec "reopen ${newVersion}: closed cleanly" (Stop-App $p); $p = $null
  $h2 = DirHash $dataDir

  # 4. Immutability: same version, different bytes, is refused; identical re-install is a no-op.
  $tampered = Join-Path $scratch 'tampered'; Copy-Item $newDir $tampered -Recurse; Add-Content (Join-Path $tampered 'README.txt') 'changed'
  $x = Installer @('-Source', $tampered)
  Rec 'immutability: same version with other bytes refused' ($x.Code -ne 0 -and (DirHash (Join-Path $installRoot "versions\$newVersion")) -eq (DirHash $newDir)) $x.Text
  $x = Installer @('-Source', $NewPackage)
  Rec 'idempotent: identical re-install changes nothing' ($x.Code -eq 0 -and (State).current -eq $newVersion -and (State).previous -eq $oldVersion) $x.Text

  # 5. Roll back: storage byte-identical, old version reads the data written by the new one.
  $x = Installer @('-Rollback')
  $s = State
  Rec "rollback: $newVersion -> $oldVersion" ($x.Code -eq 0 -and $s.current -eq $oldVersion -and $s.previous -eq $newVersion) $x.Text
  Rec 'rollback: user storage byte-identical' ((DirHash $dataDir) -eq $h2)
  $p = Start-Installed
  Rec "reopen after rollback: $oldVersion running" ($p.Path -like "$installRoot\versions\$oldVersion\*") $p.Path
  Rec 'reopen after rollback: capture still there' (Has-Capture $p)
  Rec 'reopen after rollback: closed cleanly' (Stop-App $p); $p = $null

  # 6. Uninstall = delete the install root; storage untouched.
  $h3 = DirHash $dataDir
  Remove-Item -LiteralPath $installRoot -Recurse -Force
  Rec 'uninstall: user storage byte-identical' ((DirHash $dataDir) -eq $h3 -and (Test-Path (Join-Path $dataDir 'workspace.json')))
}
catch { Rec 'run aborted' $false ($_.Exception.Message + ' @ ' + $_.InvocationInfo.ScriptLineNumber) }
finally {
  if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  Get-Process JUtilityPalette -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$installRoot*" } | Stop-Process -Force
  if (Test-Path $installRoot) { Remove-Item -LiteralPath $installRoot -Recurse -Force }
  if (Test-Path $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
Rec 'isolation: default data folder untouched' ((DirHash $defaultDir) -eq $defaultBefore)
Rec 'isolation: real install folder untouched' ((DirHash $realInstall) -eq $realBefore) $realBefore
$r.GetEnumerator() | ForEach-Object { '{0,-62} {1}{2}' -f $_.Key, $_.Value.result, $(if ($_.Value.detail) { "  ($($_.Value.detail))" } else { '' }) }
"data dir: $dataDir"
if ($JsonOut) {
  [ordered]@{ newPackage = (Split-Path -Leaf $NewPackage); newSha256 = (Get-FileHash $NewPackage).Hash; oldPackage = (Split-Path -Leaf $OldPackage); oldSha256 = (Get-FileHash $OldPackage).Hash; checks = $r } |
    ConvertTo-Json -Depth 5 | Set-Content $JsonOut -Encoding utf8
}
if (@($r.Values | Where-Object { $_.result -eq 'FAIL' }).Count -gt 0) { 'Package lifecycle checks: FAIL'; exit 1 }
'Package lifecycle checks: PASS'
