# Native Windows check for the V2.4 Quick Ring sub-rings, whole-screen capture, Esc and mouse Back/Forward.
# Fresh isolated --data-dir; a throwaway localhost page is the observable action inside the "Apps" sub-ring.
# Leave the desktop idle while it runs (real input overrides synthesized input). Keys and clicks are only sent while the test
# Power Ops process owns the foreground / the point. NOTE: the whole-screen check replaces the clipboard content.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class R2 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool UnregisterHotKey(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool OpenClipboard(IntPtr owner);
  [DllImport("user32.dll")] public static extern bool EmptyClipboard();
  [DllImport("user32.dll")] public static extern bool CloseClipboard();
  [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint format);
  public static uint Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
  public static uint PidAt(int x, int y) { var pt = new POINT { X = x, Y = y }; return Pid(GetAncestor(WindowFromPoint(pt), 2)); }
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static IntPtr FindTitle(string needle) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { var s = new StringBuilder(512); GetWindowText(h, s, 512);
      if (IsWindowVisible(h) && s.ToString().Contains(needle)) { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
  public static void XButton(int x, int y, uint which) { SetCursorPos(x, y); System.Threading.Thread.Sleep(150); mouse_event(0x80, 0, 0, which, UIntPtr.Zero); mouse_event(0x100, 0, 0, which, UIntPtr.Zero); }
  public static void Key(byte[] mods, byte key) {
    foreach (var m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
    keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero);
    for (int i = mods.Length - 1; i >= 0; i--) keybd_event(mods[i], 0, 2, UIntPtr.Zero);
  }
}
'@
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
$exe = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\JUtility.App\bin\Release\net8.0-windows10.0.19041.0\JUtilityPalette.exe'
$root = Join-Path $env:TEMP ("powerops-ring2-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $root | Out-Null
$r = [ordered]@{}
function Rec($name, $ok, $detail = '') { $r[$name] = $(if ($ok) { 'PASS' } else { 'FAIL' }) + $(if ($detail) { "  ($detail)" } else { '' }) }
function Rect($h) { $x = New-Object R2+RECT; [void][R2]::GetWindowRect($h, [ref]$x); return $x }
$HOT = @(0x11, 0x12, 0x10)

foreach ($vk in 0x52, 0x4F, 0x47) { if (-not [R2]::RegisterHotKey([IntPtr]::Zero, 0x777B, 0x4007, $vk)) { 'BLOCKED: a test shortcut (Ctrl+Alt+Shift+R/O/G) is already owned by another application.'; exit 2 }; [void][R2]::UnregisterHotKey([IntPtr]::Zero, 0x777B) }

$port = Get-Random -Minimum 41000 -Maximum 48000
$title = "PowerOps sub-ring test " + [guid]::NewGuid().ToString('N').Substring(0, 8)
$listener = [System.Net.HttpListener]::new(); $listener.Prefixes.Add("http://localhost:$port/"); $listener.Start()
$hits = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
$job = Start-ThreadJob -ArgumentList $listener, $hits, $title -ScriptBlock {
  param($l, $q, $t)
  while ($l.IsListening) {
    try { $ctx = $l.GetContext() } catch { break }
    $q.Enqueue($ctx.Request.Url.AbsolutePath)
    $b = [Text.Encoding]::UTF8.GetBytes("<!doctype html><title>$t</title><h1>$t</h1>")
    $ctx.Response.ContentType = 'text/html'; $ctx.Response.OutputStream.Write($b, 0, $b.Length); $ctx.Response.Close()
  }
}
$appId = [guid]::NewGuid(); $web = 'web:' + $appId.ToString('N')
$groupId = [guid]::NewGuid(); $group = 'group:' + $groupId.ToString('N')
$settings = [ordered]@{
  Format = 'powerops-quick-actions'; SchemaVersion = 1; Mode = 'Off'; GlobalShortcutsEnabled = $true
  GlobalShortcuts = @(@{ Gesture = 'Ctrl+Alt+Shift+R'; ActionId = 'ring.show' }, @{ Gesture = 'Ctrl+Alt+Shift+O'; ActionId = 'app.open' }, @{ Gesture = 'Ctrl+Alt+Shift+G'; ActionId = $group })
  Ring = @('capture.screen', $group, 'workspace.resume'); Shelf = @('app.open')
  ShelfOrientation = 'Horizontal'; ShelfAlwaysOnTop = $true; ShelfAutoHide = $false; WorkspaceOverrides = @()
  WebApps = @(@{ Id = $appId; Name = 'Sub-ring test page'; Url = "http://localhost:$port/"; OpenMode = 'AppWindow'; Browser = 'Auto' })
  RingGroups = @(@{ Id = $groupId; Name = 'Test apps'; Glyph = 'ECAA'; Items = @($web, 'clipboard.open') })
}
Set-Content (Join-Path $root 'quick-actions.json') ($settings | ConvertTo-Json -Depth 5) -Encoding utf8

$p = Start-Process $exe -ArgumentList '--data-dir', "`"$root`"" -PassThru
for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
Start-Sleep -Seconds 3
$main = $p.MainWindowHandle
function Focus-Main { [R2]::Key($HOT, 0x4F); Start-Sleep -Milliseconds 700 }
function Safe-Key([byte]$vk) {
  if ([R2]::Pid([R2]::GetForegroundWindow()) -ne $p.Id) { throw "Safety stop: foreground window is not the test Power Ops process; refusing to send key 0x$('{0:X}' -f $vk)." }
  [R2]::Key(@(), $vk)
}
$cx = [int]([R2]::GetSystemMetrics(0) / 2); $cy = [int]([R2]::GetSystemMetrics(1) / 2)
function Wait-Ring {
  $sw = [Diagnostics.Stopwatch]::StartNew(); $h = [IntPtr]::Zero
  while ($sw.ElapsedMilliseconds -lt 3000) { $h = [R2]::FindWindow([NullString]::Value, 'Power Ops Quick Ring'); if ($h -ne [IntPtr]::Zero -and [R2]::IsWindowVisible($h)) { break }; Start-Sleep -Milliseconds 5 }
  if ($h -eq [IntPtr]::Zero) { throw 'The Quick Ring did not appear within 3 s.' }
  Start-Sleep -Milliseconds 300; return $h
}
function Show-Ring([byte]$vk = 0x52) { [void][R2]::SetCursorPos($cx, $cy); Start-Sleep -Milliseconds 200; [R2]::Key($HOT, $vk); return Wait-Ring }
function Names($h) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  return (@($A::FromHandle($h).FindAll($T::Descendants, $cond)) | ForEach-Object { $_.Current.Name }) -join '|'
}
function RingName($h) { return $A::FromHandle($h).Current.Name }
function Close-TestPage { for ($i = 0; $i -lt 40; $i++) { $h = [R2]::FindTitle($title); if ($h -ne [IntPtr]::Zero) { [void][R2]::SendMessage($h, 0x10, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 700; return $true }; Start-Sleep -Milliseconds 250 }; return $false }
function ActiveTab {
  for ($i = 0; $i -lt 3; $i++) {
    try {
      $shell = Get-Content (Join-Path $root 'shell-workspaces.json') -Raw | ConvertFrom-Json
      $ws = $shell.Workspaces | Where-Object Id -eq $shell.ActiveWorkspaceId
      return [array]::IndexOf(@($ws.Tabs.Id), $ws.ActiveTabId)
    } catch { Start-Sleep -Milliseconds 200 }
  }
  return -2
}
function Wait-Tab($want) { for ($i = 0; $i -lt 20; $i++) { $t = ActiveTab; if ($t -eq $want) { return $t }; Start-Sleep -Milliseconds 200 }; return (ActiveTab) }

try {
# 1. First ring shows the sub-ring slot; digit 2 opens the sub-ring in place, centre becomes Back.
Focus-Main
$h = Show-Ring
Rec 'ring: sub-ring slot is marked' ((Names $h) -eq 'Open Power Ops|Screenshot (whole screen)|Test apps ›|Resume workspace') (Names $h)
Safe-Key 0x32; Start-Sleep -Milliseconds 400
Rec 'digit 2: sub-ring opens in the same window' ([R2]::IsWindowVisible($h) -and (RingName $h) -eq 'Power Ops Quick Ring - Test apps ›') (RingName $h)
Rec 'sub-ring: its actions, centre = back' ((Names $h) -eq 'Back to the first ring|Sub-ring test page|Clipboard library') (Names $h)
Safe-Key 0x1B; Start-Sleep -Milliseconds 400
Rec 'Esc in sub-ring: back to the first ring' ([R2]::IsWindowVisible($h) -and (RingName $h) -eq 'Power Ops Quick Ring' -and (Names $h) -like 'Open Power Ops|*') (RingName $h)
Safe-Key 0x1B; Start-Sleep -Milliseconds 400
Rec 'Esc on first ring: hidden' (-not [R2]::IsWindowVisible($h))

# 2. A sub-ring action runs through the dispatcher.
$before = $hits.Count
$h = Show-Ring
Safe-Key 0x32; Start-Sleep -Milliseconds 300; Safe-Key 0x31; Start-Sleep -Milliseconds 300
Rec 'sub-ring digit 1: ring hidden, action ran' ((-not [R2]::IsWindowVisible($h)) -and (Close-TestPage) -and $hits.Count -gt $before) "requests=$($hits.Count)"

# 3. A shortcut bound to the group opens the ring directly on it.
Focus-Main
$h = Show-Ring 0x47
Rec 'group shortcut: ring opens on the sub-ring' ((RingName $h) -eq 'Power Ops Quick Ring - Test apps ›') (RingName $h)
Safe-Key 0x1B; Start-Sleep -Milliseconds 300; Safe-Key 0x1B; Start-Sleep -Milliseconds 400

# 4. Whole screen goes to the clipboard as an image, with a short confirmation.
Focus-Main
if ([R2]::OpenClipboard([IntPtr]::Zero)) { [void][R2]::EmptyClipboard(); [void][R2]::CloseClipboard() }
$h = Show-Ring
Safe-Key 0x31
$toast = $false; for ($i = 0; $i -lt 30 -and -not $toast; $i++) { Start-Sleep -Milliseconds 100; $toast = [R2]::FindWindow([NullString]::Value, 'Power Ops notice') -ne [IntPtr]::Zero }
Start-Sleep -Milliseconds 300
Rec 'whole screen: image on the clipboard' ([R2]::IsClipboardFormatAvailable(2) -or [R2]::IsClipboardFormatAvailable(8))
Rec 'whole screen: confirmation shown' $toast

# 5. Mouse Back/Forward switch Power Ops tabs without any Options+ setting.
Focus-Main
[R2]::Key(@(0x11), 0x54); Start-Sleep -Milliseconds 800   # Ctrl+T: second tab (active)
$mr = Rect $main; $mx = [int](($mr.L + $mr.R) / 2); $my = [int]($mr.T + 60)
if ([R2]::PidAt($mx, $my) -ne $p.Id) { throw 'The test window is covered; cannot send mouse buttons safely.' }
$t0 = Wait-Tab 1
[R2]::XButton($mx, $my, 1); $t1 = Wait-Tab 0
[R2]::XButton($mx, $my, 2); $t2 = Wait-Tab 1
Rec 'mouse Back/Forward: previous / next tab' ($t0 -eq 1 -and $t1 -eq 0 -and $t2 -eq 1) "tabs $t0 > $t1 > $t2"

# 6. Esc puts Power Ops away (minimized in the Normal window mode).
Focus-Main
Safe-Key 0x1B; Start-Sleep -Milliseconds 900
Rec 'Esc: Power Ops minimized' ([R2]::IsIconic($main))

Focus-Main
[void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000)
Rec 'exit: clean' $p.HasExited
} catch { Rec 'run aborted' $false $_.Exception.Message }
finally {
  if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  $listener.Stop(); $listener.Close(); Stop-Job $job -ErrorAction SilentlyContinue; Remove-Job $job -Force -ErrorAction SilentlyContinue
}
$r.GetEnumerator() | ForEach-Object { '{0,-46} {1}' -f $_.Key, $_.Value }
"data root: $root"
if (@($r.Values | Where-Object { $_ -like 'FAIL*' }).Count -gt 0) { 'Quick Ring v2 native checks: FAIL'; exit 1 }
'Quick Ring v2 native checks: PASS'
