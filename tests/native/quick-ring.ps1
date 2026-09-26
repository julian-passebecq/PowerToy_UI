# Native Windows check for the V2.1 Quick Ring, using a fresh isolated --data-dir and a throwaway localhost
# page as an observable slot action. Presses Ctrl+Alt+Shift+R, arrows, digits and Esc; moves/clicks the mouse.
# Leave the desktop idle while it runs: real mouse/keyboard input overrides synthesized input and Windows then refuses focus changes.
# Closes only the test app window it opened (unique page title). Never points at the personal workspace.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class R {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool UnregisterHotKey(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
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
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(150); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
  public static void Key(byte[] mods, byte key) {
    foreach (var m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
    keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero);
    for (int i = mods.Length - 1; i >= 0; i--) keybd_event(mods[i], 0, 2, UIntPtr.Zero);
  }
}
'@
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
$exe = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\JUtility.App\bin\Release\net8.0-windows10.0.19041.0\JUtilityPalette.exe'
$root = Join-Path $env:TEMP ("powerops-ring-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $root | Out-Null
$r = [ordered]@{}
function Rec($name, $ok, $detail = '') { $r[$name] = $(if ($ok) { 'PASS' } else { 'FAIL' }) + $(if ($detail) { "  ($detail)" } else { '' }) }
function Rect($h) { $x = New-Object R+RECT; [void][R]::GetWindowRect($h, [ref]$x); return $x }
$HOT = @(0x11, 0x12, 0x10); $VK_R = 0x52

foreach ($vk in 0x52, 0x4F) { if (-not [R]::RegisterHotKey([IntPtr]::Zero, 0x777A, 0x4007, $vk)) { 'BLOCKED: a test shortcut (Ctrl+Alt+Shift+R/O) is already owned by another application.'; exit 2 }; [void][R]::UnregisterHotKey([IntPtr]::Zero, 0x777A) }

# Throwaway page = observable, harmless slot action.
$port = Get-Random -Minimum 41000 -Maximum 48000
$title = "PowerOps ring test " + [guid]::NewGuid().ToString('N').Substring(0, 8)
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
$settings = [ordered]@{
  Format = 'powerops-quick-actions'; SchemaVersion = 1; Mode = 'Off'; GlobalShortcutsEnabled = $true
  GlobalShortcuts = @(@{ Gesture = 'Ctrl+Alt+Shift+R'; ActionId = 'ring.show' }, @{ Gesture = 'Ctrl+Alt+Shift+O'; ActionId = 'app.open' })
  Ring = @($web, 'workspace.resume', 'clipboard.open', 'app.toggle'); Shelf = @('app.open')
  ShelfOrientation = 'Horizontal'; ShelfAlwaysOnTop = $true; ShelfAutoHide = $false; WorkspaceOverrides = @()
  WebApps = @(@{ Id = $appId; Name = 'Ring test page'; Url = "http://localhost:$port/"; OpenMode = 'AppWindow'; Browser = 'Auto' })
}
Set-Content (Join-Path $root 'quick-actions.json') ($settings | ConvertTo-Json -Depth 5) -Encoding utf8

$p = Start-Process $exe -ArgumentList '--data-dir', "`"$root`"" -PassThru
for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
Start-Sleep -Seconds 3
$main = $p.MainWindowHandle; $mr = Rect $main
$titleBar = @([int]($mr.L + 120), [int]($mr.T + 14))
function Focus-Main { [R]::Key($HOT, 0x4F); Start-Sleep -Milliseconds 700 }   # app.open global shortcut: consumed by Windows, never typed into another app
function Safe-Key([byte]$vk) {
  $fg = [R]::GetForegroundWindow()
  if ([R]::Pid($fg) -ne $p.Id) { throw "Safety stop: foreground window is not the test Power Ops process; refusing to send key 0x$('{0:X}' -f $vk)." }
  [R]::Key(@(), $vk)
}
function Safe-Click([int]$x, [int]$y) {
  if ([R]::PidAt($x, $y) -ne $p.Id) { throw "Safety stop: ($x,$y) is not over the test Power Ops process; refusing to click." }
  [R]::Click($x, $y)
}
$cx = [int]([R]::GetSystemMetrics(0) / 2); $cy = [int]([R]::GetSystemMetrics(1) / 2)
Rec 'hidden until invoked: no ring window yet' ([R]::FindWindow([NullString]::Value, 'Power Ops Quick Ring') -eq [IntPtr]::Zero)

function Show-Ring($x, $y) {
  [void][R]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 200
  $sw = [Diagnostics.Stopwatch]::StartNew(); [R]::Key($HOT, $VK_R)
  $h = [IntPtr]::Zero
  while ($sw.ElapsedMilliseconds -lt 3000) { $h = [R]::FindWindow([NullString]::Value, 'Power Ops Quick Ring'); if ($h -ne [IntPtr]::Zero -and [R]::IsWindowVisible($h)) { break }; Start-Sleep -Milliseconds 5 }
  $script:lastLatency = $sw.ElapsedMilliseconds
  if ($h -eq [IntPtr]::Zero) { throw 'The Quick Ring did not appear within 3 s.' }
  Start-Sleep -Milliseconds 300
  return $h
}
function Ring-Buttons($h) {
  $el = $A::FromHandle($h)
  $cond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  return @($el.FindAll($T::Descendants, $cond))
}
function Focused { return $A::FocusedElement.Current.Name }
function Close-TestPage { for ($i = 0; $i -lt 40; $i++) { $h = [R]::FindTitle($title); if ($h -ne [IntPtr]::Zero) { [void][R]::SendMessage($h, 0x10, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 700; return $true }; Start-Sleep -Milliseconds 250 }; return $false }

try {
# 1. Shortcut shows the ring centred on the pointer, focused, with numbered slots clockwise from the top.
Focus-Main
$h = Show-Ring $cx $cy
$first = $script:lastLatency
Rec 'shortcut: ring visible' ($h -ne [IntPtr]::Zero -and [R]::IsWindowVisible($h)) "first show ${first} ms"
$rr = Rect $h; $rcx = ($rr.L + $rr.R) / 2; $rcy = ($rr.T + $rr.B) / 2
Rec 'placement: centred on pointer' ([Math]::Abs($rcx - $cx) -le 3 -and [Math]::Abs($rcy - $cy) -le 3) "centre=($rcx,$rcy) pointer=($cx,$cy)"
Rec 'focus: ring is foreground' ([R]::GetForegroundWindow() -eq $h)
$buttons = Ring-Buttons $h
$names = @($buttons | ForEach-Object { $_.Current.Name })
Rec 'slots: centre + names in order' (($names -join '|') -eq 'Open Power Ops|Ring test page|Resume workspace|Clipboard library|Show / hide Power Ops') ($names -join ', ')
$b0 = $buttons[1].Current.BoundingRectangle; $b1 = $buttons[2].Current.BoundingRectangle
Rec 'slots: 1 at top, 2 on the right' (($b0.Y + $b0.Height / 2) -lt $rcy - 50 -and ($b1.X + $b1.Width / 2) -gt $rcx + 50)
Rec 'keyboard: centre focused first' ((Focused) -eq 'Open Power Ops') (Focused)
Safe-Key 0x27; Start-Sleep -Milliseconds 200; $f1 = Focused
Safe-Key 0x27; Start-Sleep -Milliseconds 200; $f2 = Focused
Safe-Key 0x25; Start-Sleep -Milliseconds 200; $f3 = Focused
Safe-Key 0x25; Start-Sleep -Milliseconds 200; $f4 = Focused
Rec 'keyboard: arrows move clockwise and wrap' ($f1 -eq 'Ring test page' -and $f2 -eq 'Resume workspace' -and $f3 -eq 'Ring test page' -and $f4 -eq 'Show / hide Power Ops') "$f1 > $f2 > $f3 > $f4"
Safe-Key 0x1B; Start-Sleep -Milliseconds 500
Rec 'Esc: ring hidden' (-not [R]::IsWindowVisible($h))
Rec 'Esc: focus back on previous window' ([R]::GetForegroundWindow() -eq $main)

# 2. Digit runs the slot through the dispatcher.
$before = $hits.Count
$h = Show-Ring $cx $cy
Rec 'warm show latency' ($script:lastLatency -lt 500) "$($script:lastLatency) ms"
Safe-Key 0x31; Start-Sleep -Milliseconds 300
Rec 'digit 1: ring hidden' (-not [R]::IsWindowVisible($h))
Rec 'digit 1: slot action ran (page opened)' ((Close-TestPage) -and $hits.Count -gt $before) "requests=$($hits.Count)"

# 3. Click on a slot runs it.
Focus-Main; $before = $hits.Count
$h = Show-Ring $cx $cy
$slot = (Ring-Buttons $h)[1].Current.BoundingRectangle
Safe-Click ([int]($slot.X + $slot.Width / 2)) ([int]($slot.Y + $slot.Height / 2)); Start-Sleep -Milliseconds 300
Rec 'click slot: ring hidden and action ran' ((-not [R]::IsWindowVisible($h)) -and (Close-TestPage) -and $hits.Count -gt $before)

# 4. Outside click dismisses without running anything.
Focus-Main; $before = $hits.Count
$sw = [R]::GetSystemMetrics(0); $sh = [R]::GetSystemMetrics(1)
$h = Show-Ring ($sw - 10) ($sh - 60)
if ([R]::GetAncestor([R]::WindowFromPoint((New-Object R+POINT -Property @{ X = $titleBar[0]; Y = $titleBar[1] })), 2) -ne $main) { throw 'Title-bar point is covered; cannot test an outside click safely.' }
Safe-Click $titleBar[0] $titleBar[1]; Start-Sleep -Milliseconds 500
Rec 'outside click: ring dismissed, nothing ran' ((-not [R]::IsWindowVisible($h)) -and $hits.Count -eq $before)

# 5. Screen corner: ring clamped inside the screen.
$h = Show-Ring 2 2
$rr = Rect $h
Rec 'corner: clamped on screen' ($rr.L -ge 0 -and $rr.T -ge 0 -and $rr.L -le 2 -and $rr.T -le 2) "rect=($($rr.L),$($rr.T))"
Safe-Key 0x1B; Start-Sleep -Milliseconds 400

# 6. Centre opens Power Ops.
[void][R]::SendMessage($main, 0x0112, [IntPtr]0xF020, [IntPtr]::Zero); Start-Sleep -Milliseconds 800   # minimise only the test Power Ops window
$h = Show-Ring $cx $cy
$c = (Ring-Buttons $h)[0].Current.BoundingRectangle
Safe-Click ([int]($c.X + $c.Width / 2)) ([int]($c.Y + $c.Height / 2)); Start-Sleep -Milliseconds 900
Rec 'centre: Power Ops brought to front' ([R]::GetForegroundWindow() -eq $main)

# 7. Hidden ring costs nothing.
Start-Sleep -Seconds 3   # let the just-restored main window finish its layout save / redraw before measuring
$p.Refresh(); $cpu0 = $p.TotalProcessorTime; Start-Sleep -Seconds 10; $p.Refresh()
$cpu = ($p.TotalProcessorTime - $cpu0).TotalMilliseconds
Rec 'idle after use: no CPU while hidden' ($cpu -lt 50) "$([Math]::Round($cpu, 1)) ms CPU in 10 s"

[void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000)
Rec 'exit: Power Ops and ring closed' $p.HasExited
} catch { Rec 'run aborted' $false $_.Exception.Message }
finally {
  if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  $listener.Stop(); $listener.Close(); Stop-Job $job -ErrorAction SilentlyContinue; Remove-Job $job -Force -ErrorAction SilentlyContinue
}
$r.GetEnumerator() | ForEach-Object { '{0,-46} {1}' -f $_.Key, $_.Value }
"data root: $root"
if (@($r.Values | Where-Object { $_ -like 'FAIL*' }).Count -gt 0) { 'Quick Ring native checks: FAIL'; exit 1 }
'Quick Ring native checks: PASS'
