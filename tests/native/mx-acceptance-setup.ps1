# Prepares the manual MX Master / Logi Options+ acceptance (docs/v2/MX_MANUAL_ACCEPTANCE.md):
# a fresh isolated Power Ops instance in Hybrid mode with the four recommended shortcuts, Mongoku as ring slot 8,
# three tabs (Launchpad | Capture | Clipboard) for Back/Forward, and a hidden observer that logs what really happens.
# Never touches the personal workspace. Stop with: close the test window (the observer stops by itself).
param([string]$Mongoku = 'http://localhost:3100/')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace MXS -Name K -MemberDefinition '[DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk); [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);'
$A = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exe = Join-Path $repo 'src\JUtility.App\bin\Release\net8.0-windows\JUtilityPalette.exe'
if (-not (Test-Path $exe)) { throw "Build first: .\scripts\build.ps1" }

$busy = @(foreach ($k in @(@('R', 0x52), @('P', 0x50), @('N', 0x4E), @('V', 0x56))) { if ([MXS.K]::RegisterHotKey([IntPtr]::Zero, 0x7102, 0x4007, $k[1])) { [void][MXS.K]::UnregisterHotKey([IntPtr]::Zero, 0x7102) } else { "Ctrl+Alt+Shift+$($k[0])" } })
if ($busy.Count -gt 0) { "BLOCKED: already owned by another program (or another Power Ops): $($busy -join ', '). Close it first."; exit 2 }

$root = Join-Path $env:TEMP ('powerops-mx-acceptance-' + (Get-Date -Format 'yyyyMMdd-HHmm'))
New-Item -ItemType Directory $root | Out-Null
$mongokuId = [guid]::NewGuid(); $web = 'web:' + $mongokuId.ToString('N')
@{ Format = 'powerops-quick-actions'; SchemaVersion = 1; Mode = 'Hybrid'; GlobalShortcutsEnabled = $true
   GlobalShortcuts = @(@{ Gesture = 'Ctrl+Alt+Shift+R'; ActionId = 'ring.show' }, @{ Gesture = 'Ctrl+Alt+Shift+P'; ActionId = 'app.toggle' },
                       @{ Gesture = 'Ctrl+Alt+Shift+N'; ActionId = 'capture.quick' }, @{ Gesture = 'Ctrl+Alt+Shift+V'; ActionId = 'clipboard.open' })
   Ring = @('capture.region', 'folder.downloads', 'capture.quick', 'clipboard.open', 'folder.explorer', 'terminal.open', 'workspace.resume', $web)
   Shelf = @('app.open', 'capture.region', 'capture.quick', 'clipboard.open', 'folder.downloads', 'folder.explorer', 'terminal.open', 'workspace.resume')
   ShelfOrientation = 'Horizontal'; ShelfAlwaysOnTop = $true; ShelfAutoHide = $false; WorkspaceOverrides = @()
   WebApps = @(@{ Id = $mongokuId; Name = 'Mongoku'; Url = $Mongoku; OpenMode = 'Embedded'; Browser = 'Auto' }) } |
  ConvertTo-Json -Depth 5 | Set-Content (Join-Path $root 'quick-actions.json') -Encoding utf8

$p = Start-Process $exe -ArgumentList '--data-dir', "`"$root`"" -PassThru
for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
Start-Sleep -Seconds 4
$main = $A::FromHandle($p.MainWindowHandle)
function Inv([string]$name) {
  $c = New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)), (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
  for ($i = 0; $i -lt 20; $i++) { $e = $main.FindFirst($TS::Descendants, $c); if ($e) { $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 700; return }; Start-Sleep -Milliseconds 200 }
  throw "not found: $name"
}
Inv 'Launchpad'; Inv '+ (Ctrl+T)'; Inv 'Capture'; Inv '+ (Ctrl+T)'; Inv 'Clipboard Library'

$log = Join-Path $root 'observer.log'
Start-Process pwsh -WindowStyle Hidden -ArgumentList '-NoProfile', '-File', "`"$(Join-Path $PSScriptRoot 'mx-observer.ps1')`"", '-DataDir', "`"$root`"", '-PowerOpsPid', $p.Id, '-Log', "`"$log`"" | Out-Null
"Test instance ready: window title ends with '— $(Split-Path $root -Leaf)'"
"Data folder (isolated): $root"
"Observer log: $log"
"Next: follow docs/v2/MX_MANUAL_ACCEPTANCE.md"
