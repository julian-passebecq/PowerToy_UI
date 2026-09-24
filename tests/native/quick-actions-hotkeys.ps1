# Native Windows check for V2.1 global shortcuts (RegisterHotKey), using fresh isolated --data-dir folders.
# Leave the desktop idle while it runs: real mouse/keyboard input overrides synthesized input and Windows then refuses focus changes.
# WARNING: synthesizes Ctrl+Alt+Shift+P / Ctrl+Alt+Shift+O keystrokes and briefly takes focus.
# Run from the repo root after .\scripts\build.ps1. Never points at the personal workspace.
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class W {
  [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool UnregisterHotKey(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static string Title(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
  public static uint Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
  public static void Chord(byte[] mods, byte key) {
    foreach (var m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
    keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero);
    for (int i = mods.Length - 1; i >= 0; i--) keybd_event(mods[i], 0, 2, UIntPtr.Zero);
  }
}
'@
$MOD_ALT = 1; $MOD_CTRL = 2; $MOD_SHIFT = 4; $MOD_NOREPEAT = 0x4000
$VK_CTRL = 0x11; $VK_ALT = 0x12; $VK_SHIFT = 0x10
function Probe([uint32]$mods, [uint32]$vk) {
  $ok = [W]::RegisterHotKey([IntPtr]::Zero, 0x7777, $mods -bor $MOD_NOREPEAT, $vk)
  $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
  if ($ok) { [void][W]::UnregisterHotKey([IntPtr]::Zero, 0x7777); return 'free' }
  return "taken(err $err)"
}
$exe = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\JUtility.App\bin\Release\net8.0-windows\JUtilityPalette.exe'
$root = Join-Path $env:TEMP ("powerops-hotkey-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$d1 = Join-Path $root 'default'; $d2 = Join-Path $root 'enabled'; $d3 = Join-Path $root 'collide'
New-Item -ItemType Directory -Force $d1, $d2, $d3 | Out-Null
$results = [ordered]@{}
function Wait-Window($proc) { for ($i = 0; $i -lt 60; $i++) { $proc.Refresh(); if ($proc.MainWindowHandle -ne 0) { return $proc.MainWindowHandle }; Start-Sleep -Milliseconds 250 }; throw 'window never appeared' }

$results['baseline Ctrl+Alt+Shift+P'] = Probe ($MOD_CTRL -bor $MOD_ALT -bor $MOD_SHIFT) 0x50
$results['baseline Ctrl+Alt+Shift+O'] = Probe ($MOD_CTRL -bor $MOD_ALT -bor $MOD_SHIFT) 0x4F

# 1. Fresh data dir: nothing registered, nothing written.
$a = Start-Process $exe -ArgumentList '--data-dir', "`"$d1`"" -PassThru
[void](Wait-Window $a); Start-Sleep -Seconds 3
$results['default: Ctrl+Alt+Shift+P'] = Probe ($MOD_CTRL -bor $MOD_ALT -bor $MOD_SHIFT) 0x50
$results['default: quick-actions.json created'] = Test-Path (Join-Path $d1 'quick-actions.json')
[void]$a.CloseMainWindow(); [void]$a.WaitForExit(15000); $results['default: exited'] = $a.HasExited

# 2. Enabled settings in an isolated dir.
$json = @{
  Format = 'powerops-quick-actions'; SchemaVersion = 1; Mode = 'Off'; GlobalShortcutsEnabled = $true
  GlobalShortcuts = @(@{ Gesture = 'Ctrl+Alt+Shift+P'; ActionId = 'app.toggle' }, @{ Gesture = 'Ctrl+Alt+Shift+O'; ActionId = 'app.open' })
  Ring = @('capture.region', 'folder.downloads'); Shelf = @('app.open'); ShelfOrientation = 'Horizontal'; ShelfAlwaysOnTop = $true; ShelfAutoHide = $false; WorkspaceOverrides = @()
} | ConvertTo-Json -Depth 5
Set-Content -Path (Join-Path $d2 'quick-actions.json') -Value $json -Encoding utf8
$b = Start-Process $exe -ArgumentList '--data-dir', "`"$d2`"" -PassThru
$hb = Wait-Window $b; Start-Sleep -Seconds 3
$results['enabled: Ctrl+Alt+Shift+P'] = Probe ($MOD_CTRL -bor $MOD_ALT -bor $MOD_SHIFT) 0x50
$results['enabled: Ctrl+Alt+Shift+O'] = Probe ($MOD_CTRL -bor $MOD_ALT -bor $MOD_SHIFT) 0x4F
$fg = [W]::GetForegroundWindow(); $results['enabled: initially foreground'] = ([W]::Pid($fg) -eq $b.Id)

function Toggle-Check($label) {
  $wasFront = ([W]::GetForegroundWindow() -eq $hb) -and -not [W]::IsIconic($hb)
  [W]::Chord(@($VK_CTRL, $VK_ALT, $VK_SHIFT), 0x50); Start-Sleep -Milliseconds 1200
  $nowFront = ([W]::GetForegroundWindow() -eq $hb) -and -not [W]::IsIconic($hb)
  $expected = if ($wasFront) { 'minimized' } else { 'brought to front' }
  $ok = if ($wasFront) { [W]::IsIconic($hb) } else { $nowFront }
  $results[$label] = "was front=$wasFront -> expected $expected : " + $(if ($ok) { 'PASS' } else { 'FAIL' })
}
Toggle-Check 'toggle #1'
Toggle-Check 'toggle #2'
Toggle-Check 'toggle #3'
# Minimize again then app.open hotkey restores.
[W]::Chord(@($VK_CTRL, $VK_ALT, $VK_SHIFT), 0x50); Start-Sleep -Milliseconds 1200
[W]::Chord(@($VK_CTRL, $VK_ALT, $VK_SHIFT), 0x4F); Start-Sleep -Milliseconds 1200
$fg = [W]::GetForegroundWindow()
$results['app.open: iconic'] = [W]::IsIconic($hb)
$results['app.open: foreground is Power Ops'] = ([W]::Pid($fg) -eq $b.Id)

# 3. Collision: second instance (different data dir) with the same bindings must report visibly.
Copy-Item (Join-Path $d2 'quick-actions.json') (Join-Path $d3 'quick-actions.json')
$c = Start-Process $exe -ArgumentList '--data-dir', "`"$d3`"" -PassThru
$box = [IntPtr]::Zero
for ($i = 0; $i -lt 40 -and $box -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 250; $box = [W]::FindWindow('#32770', 'Power Ops global shortcuts') }
$results['collision: warning dialog shown'] = ($box -ne [IntPtr]::Zero -and [W]::Pid($box) -eq $c.Id)
if ($box -ne [IntPtr]::Zero) { [void][W]::SendMessage($box, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
Start-Sleep -Seconds 1
$c.Refresh(); $results['collision: instance still running'] = -not $c.HasExited
[void]$c.CloseMainWindow(); [void]$c.WaitForExit(15000)
$results['collision: first instance keeps hotkey'] = Probe ($MOD_CTRL -bor $MOD_ALT -bor $MOD_SHIFT) 0x50

# 4. Exit releases the registrations.
[void]$b.CloseMainWindow(); [void]$b.WaitForExit(15000); $results['enabled: exited'] = $b.HasExited
Start-Sleep -Milliseconds 500
$results['after exit: Ctrl+Alt+Shift+P'] = Probe ($MOD_CTRL -bor $MOD_ALT -bor $MOD_SHIFT) 0x50
$results['after exit: Ctrl+Alt+Shift+O'] = Probe ($MOD_CTRL -bor $MOD_ALT -bor $MOD_SHIFT) 0x4F
$results['settings file unchanged by load'] = ((Get-Content (Join-Path $d2 'quick-actions.json') -Raw).Trim() -eq $json.Trim())

foreach ($p in @($a, $b, $c)) { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
$results.GetEnumerator() | ForEach-Object { '{0,-42} {1}' -f $_.Key, $_.Value }
"data root: $root"

$expected = @{
  'default: Ctrl+Alt+Shift+P' = 'free'; 'default: quick-actions.json created' = $false
  'enabled: Ctrl+Alt+Shift+P' = 'taken(err 1409)'; 'enabled: Ctrl+Alt+Shift+O' = 'taken(err 1409)'
  'app.open: iconic' = $false; 'app.open: foreground is Power Ops' = $true
  'collision: warning dialog shown' = $true; 'collision: instance still running' = $true
  'collision: first instance keeps hotkey' = 'taken(err 1409)'; 'enabled: exited' = $true
  'after exit: Ctrl+Alt+Shift+P' = 'free'; 'after exit: Ctrl+Alt+Shift+O' = 'free'; 'settings file unchanged by load' = $true
}
$bad = @($expected.Keys | Where-Object { "$($results[$_])" -ne "$($expected[$_])" }) + @($results.Keys | Where-Object { $_ -like 'toggle*' -and $results[$_] -notlike '*PASS' })
if ($results['baseline Ctrl+Alt+Shift+P'] -ne 'free' -or $results['baseline Ctrl+Alt+Shift+O'] -ne 'free') { 'BLOCKED: a test combination is already owned by another application.'; exit 2 }
if ($bad.Count -gt 0) { 'FAIL: ' + ($bad -join '; '); exit 1 }
'Native quick-action hotkey checks: PASS'


