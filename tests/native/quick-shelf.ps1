# Native Windows check for the V2.1 Quick Shelf, using fresh isolated --data-dir folders.
# Leave the desktop idle while it runs: real mouse/keyboard input overrides synthesized input and Windows then refuses focus changes.
# WARNING: moves/clicks the mouse, synthesizes Ctrl+Alt+Shift+L and Esc, and briefly takes focus.
# Run from the repo root after .\scripts\build.ps1. Never points at the personal workspace.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class S {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
  public static uint PidAt(int x, int y) { var pt = new POINT { X = x, Y = y }; uint p; GetWindowThreadProcessId(GetAncestor(WindowFromPoint(pt), 2), out p); return p; }
  [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool UnregisterHotKey(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static uint Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(150); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
  public static void Drag(int x, int y, int dx, int dy) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(150); mouse_event(2, 0, 0, 0, UIntPtr.Zero);
    for (int i = 1; i <= 10; i++) { System.Threading.Thread.Sleep(30); SetCursorPos(x + dx * i / 10, y + dy * i / 10); }
    System.Threading.Thread.Sleep(150); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
  }
  public static void Key(byte[] mods, byte key) {
    foreach (var m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
    keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero);
    for (int i = mods.Length - 1; i >= 0; i--) keybd_event(mods[i], 0, 2, UIntPtr.Zero);
  }
}
'@
$A = [System.Windows.Automation.AutomationElement]
$T = [System.Windows.Automation.TreeScope]
$exe = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\JUtility.App\bin\Release\net8.0-windows\JUtilityPalette.exe'
$root = Join-Path $env:TEMP ("powerops-shelf-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $root | Out-Null
$r = [ordered]@{}
function Rec($name, $ok, $detail = '') { $r[$name] = $(if ($ok) { 'PASS' } else { 'FAIL' }) + $(if ($detail) { "  ($detail)" } else { '' }) }

function Settings($extra) {
  $s = [ordered]@{
    Format = 'powerops-quick-actions'; SchemaVersion = 1; Mode = 'QuickShelf'; GlobalShortcutsEnabled = $true
    GlobalShortcuts = @(@{ Gesture = 'Ctrl+Alt+Shift+L'; ActionId = 'shelf.toggle' })
    Ring = @('capture.region'); Shelf = @('app.open', 'capture.region', 'capture.quick', 'clipboard.open', 'folder.downloads', 'folder.explorer', 'terminal.open', 'workspace.resume')
    ShelfOrientation = 'Horizontal'; ShelfAlwaysOnTop = $true; ShelfAutoHide = $false; ShelfLeft = $null; ShelfTop = $null; WorkspaceOverrides = @()
  }
  foreach ($k in $extra.Keys) { $s[$k] = $extra[$k] }
  Set-Content -Path (Join-Path $root 'quick-actions.json') -Value ($s | ConvertTo-Json -Depth 5) -Encoding utf8
}
function Start-App {
  $p = Start-Process $exe -ArgumentList '--data-dir', "`"$root`"" -PassThru
  for ($i = 0; $i -lt 60; $i++) { $p.Refresh(); if ($p.MainWindowHandle -ne 0) { break }; Start-Sleep -Milliseconds 250 }
  Start-Sleep -Seconds 3
  return $p
}
function Find-Shelf($p) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)
  foreach ($w in $A::RootElement.FindAll($T::Children, $cond)) { if ($w.Current.Name -eq 'Power Ops Quick Shelf') { return $w } }
  return $null
}
function Shelf-Buttons($shelf) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  return @($shelf.FindAll($T::Descendants, $cond) | Where-Object { $_.Current.Name -ne 'Quick Shelf options' })
}
function Rect($h) { $x = New-Object S+RECT; [void][S]::GetWindowRect($h, [ref]$x); return $x }
function Center($el) { $b = $el.Current.BoundingRectangle; return @([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) }
function Stop-App($p) { [void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000); if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; return $false }; return $true }

function Safe-Key([byte]$vk) {
  if ([S]::Pid([S]::GetForegroundWindow()) -ne $p.Id) { throw "Safety stop: foreground window is not the test Power Ops process; refusing to send key 0x$('{0:X}' -f $vk)." }
  [S]::Key(@(), $vk)
}
function Safe-Click([int]$x, [int]$y) {
  if ([S]::PidAt($x, $y) -ne $p.Id) { throw "Safety stop: ($x,$y) is not over the test Power Ops process; refusing to click." }
  [S]::Click($x, $y)
}
function Safe-Drag([int]$x, [int]$y, [int]$dx, [int]$dy) {
  if ([S]::PidAt($x, $y) -ne $p.Id) { throw "Safety stop: ($x,$y) is not over the test Power Ops process; refusing to drag." }
  [S]::Drag($x, $y, $dx, $dy)
}
trap { if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }; 'Quick Shelf native checks: ABORTED - ' + $_.Exception.Message; exit 1 }
$probe = [S]::RegisterHotKey([IntPtr]::Zero, 0x7778, 0x4007, 0x4C)
if (-not $probe) { 'BLOCKED: Ctrl+Alt+Shift+L is already owned by another application.'; exit 2 }
[void][S]::UnregisterHotKey([IntPtr]::Zero, 0x7778)

# --- Run 1: startup shelf, accessibility, no-activate, shortcut/Esc, drag -----------------------
Settings @{}
$fgBefore = [S]::GetForegroundWindow()
$p = Start-App
$shelf = Find-Shelf $p
Rec 'startup: shelf shown in Quick Shelf mode' ($null -ne $shelf)
if ($null -eq $shelf) { Stop-App $p | Out-Null; $r.GetEnumerator() | ForEach-Object { '{0,-55} {1}' -f $_.Key, $_.Value }; exit 1 }
$hs = [IntPtr]$shelf.Current.NativeWindowHandle
Rec 'startup: shelf did not take the foreground' ([S]::GetForegroundWindow() -ne $hs)
$ex = [S]::GetWindowLong($hs, -20)
Rec 'window: tool window + no-activate styles' ((($ex -band 0x80) -ne 0) -and (($ex -band 0x08000000) -ne 0)) ('0x{0:X}' -f $ex)
$names = @(Shelf-Buttons $shelf | ForEach-Object { $_.Current.Name })
$expected = @('Open Power Ops', 'Screenshot (region)', 'Quick Capture', 'Clipboard library', 'Downloads', 'Explorer folder', 'Terminal', 'Resume workspace')
Rec 'buttons: accessible names in configured order' (($names -join '|') -eq ($expected -join '|')) ($names -join ', ')
$rect = Rect $hs
Rec 'placement: near top of screen' ($rect.T -ge -50 -and $rect.T -lt 200) "top=$($rect.T) left=$($rect.L) width=$($rect.R - $rect.L)"

# Clicking "Open Power Ops": action runs, main window comes forward, shelf itself never activates.
$open = (Shelf-Buttons $shelf)[0]; $c = Center $open
Safe-Click $c[0] $c[1]; Start-Sleep -Milliseconds 1200
$fg = [S]::GetForegroundWindow()
Rec 'click: Open Power Ops brings main window forward' ($fg -eq $p.MainWindowHandle)
Rec 'click: shelf not activated by the click' ($fg -ne $hs)
# Again while Power Ops already owns the foreground (the case where WPF focus could activate the Shelf).
Safe-Click $c[0] $c[1]; Start-Sleep -Milliseconds 1200
$fg = [S]::GetForegroundWindow()
Rec 'click again: shelf still not activated' ($fg -ne $hs -and $fg -eq $p.MainWindowHandle)

# Shortcut toggles; keyboard show takes focus; Esc hides and returns focus.
[S]::Key(@(0x11, 0x12, 0x10), 0x4C); Start-Sleep -Milliseconds 900
Rec 'shortcut #1: visible shelf hides' (-not [S]::IsWindowVisible($hs))
$fgBeforeShow = [S]::GetForegroundWindow()
[S]::Key(@(0x11, 0x12, 0x10), 0x4C); Start-Sleep -Milliseconds 900
Rec 'shortcut #2: shelf shown with keyboard focus' ([S]::IsWindowVisible($hs) -and [S]::GetForegroundWindow() -eq $hs)
$focused = [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name
Rec 'keyboard: first button focused' ($focused -eq 'Open Power Ops') $focused
Safe-Key 0x09; Start-Sleep -Milliseconds 300
$focused = [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name
Rec 'keyboard: Tab moves to next button' ($focused -eq 'Screenshot (region)') $focused
Safe-Key 0x1B; Start-Sleep -Milliseconds 800
Rec 'Esc: shelf hidden' (-not [S]::IsWindowVisible($hs))
Rec 'Esc: focus returned to previous window' ([S]::GetForegroundWindow() -eq $fgBeforeShow)

# Drag the grip; position must persist.
[S]::Key(@(0x11, 0x12, 0x10), 0x4C); Start-Sleep -Milliseconds 900
$gripCond = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Quick Shelf options')
$grip = $shelf.FindFirst($T::Descendants, $gripCond); $g = Center $grip
$before = Rect $hs
Safe-Drag $g[0] $g[1] 120 90; Start-Sleep -Milliseconds 1000
$after = Rect $hs
Rec 'drag: shelf moved' (($after.L - $before.L) -gt 60 -and ($after.T - $before.T) -gt 40) "dx=$($after.L - $before.L) dy=$($after.T - $before.T)"
$saved = Get-Content (Join-Path $root 'quick-actions.json') -Raw | ConvertFrom-Json
Rec 'drag: position saved' ($null -ne $saved.ShelfLeft -and $null -ne $saved.ShelfTop) "ShelfLeft=$($saved.ShelfLeft) ShelfTop=$($saved.ShelfTop)"
Rec 'exit: closing Power Ops closes the shelf and the process' (Stop-App $p)

# --- Run 2: saved position + per-workspace override + vertical auto-hide ------------------------
$shell = Get-Content (Join-Path $root 'shell-workspaces.json') -Raw | ConvertFrom-Json
$active = $shell.ActiveWorkspaceId
Settings @{ ShelfLeft = $saved.ShelfLeft; ShelfTop = $saved.ShelfTop; ShelfOrientation = 'Vertical'; ShelfAutoHide = $true
  WorkspaceOverrides = @(@{ WorkspaceId = $active; Ring = $null; Shelf = @('terminal.open', 'folder.downloads') }) }
[S]::SetCursorPos(5, 5) | Out-Null
$p = Start-App
$shelf = Find-Shelf $p; $hs = [IntPtr]$shelf.Current.NativeWindowHandle
$pos = Rect $hs
Rec 'restart: saved position restored' ([Math]::Abs($pos.L - $after.L) -le 2 -and [Math]::Abs($pos.T - $after.T) -le 2) "now=($($pos.L),$($pos.T)) saved=($($after.L),$($after.T))"
Start-Sleep -Milliseconds 1500
$collapsed = Rect $hs
$ch = $collapsed.B - $collapsed.T
Rec 'auto-hide: collapsed to handle while pointer away' ($ch -lt 60) "height=$ch"
$gc = Center ($shelf.FindFirst($T::Descendants, $gripCond))
[S]::SetCursorPos($gc[0], $gc[1]) | Out-Null; Start-Sleep -Milliseconds 800
$expanded = Rect $hs
$eh = $expanded.B - $expanded.T; $ew = $expanded.R - $expanded.L
$names = @(Shelf-Buttons $shelf | ForEach-Object { $_.Current.Name })
Rec 'workspace: active workspace override used' (($names -join '|') -eq 'Terminal|Downloads') ($names -join ', ')
Rec 'auto-hide: hover expands vertically' ($eh -gt 100 -and $eh -gt $ew) "height=$eh width=$ew"
[S]::SetCursorPos(5, 5) | Out-Null; Start-Sleep -Milliseconds 1600
$again = Rect $hs
Rec 'auto-hide: collapses again after pointer leaves' (($again.B - $again.T) -lt 60) "height=$($again.B - $again.T)"
Rec 'exit: run 2 closed cleanly' (Stop-App $p)

$r.GetEnumerator() | ForEach-Object { '{0,-55} {1}' -f $_.Key, $_.Value }
"data root: $root"
if (@($r.Values | Where-Object { $_ -like 'FAIL*' }).Count -gt 0) { 'Quick Shelf native checks: FAIL'; exit 1 }
'Quick Shelf native checks: PASS'


