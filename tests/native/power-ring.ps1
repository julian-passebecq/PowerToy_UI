# Native Windows check for Power Ring (the separate light launcher). Isolated ring.json in a temp folder, hotkey
# Ctrl+Alt+Shift+F9 (checked free first), a throwaway localhost page as the observable "url" action.
# Leave the desktop idle while it runs. Keys are only sent while the Power Ring process owns the foreground.
# NOTE: the "text" check replaces the clipboard content.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, PresentationCore
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class PR {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool UnregisterHotKey(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static uint Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
  public static IntPtr FindTitle(string needle) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { var s = new StringBuilder(512); GetWindowText(h, s, 512);
      if (IsWindowVisible(h) && s.ToString().Contains(needle)) { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
  public static void Key(byte[] mods, byte key) {
    foreach (var m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
    keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero);
    for (int i = mods.Length - 1; i >= 0; i--) keybd_event(mods[i], 0, 2, UIntPtr.Zero);
  }
}
'@
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
$exe = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\PowerRing\bin\Release\net8.0-windows\PowerRing.exe'
$root = Join-Path $env:TEMP ("powerring-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $root | Out-Null
$cfg = Join-Path $root 'ring.json'
$r = [ordered]@{}
function Rec($name, $ok, $detail = '') { $r[$name] = $(if ($ok) { 'PASS' } else { 'FAIL' }) + $(if ($detail) { "  ($detail)" } else { '' }) }
$HOT = @(0x11, 0x12, 0x10); $F9 = 0x78
if (-not [PR]::RegisterHotKey([IntPtr]::Zero, 0x777C, 0x4007, $F9)) { 'BLOCKED: Ctrl+Alt+Shift+F9 is already owned by another application.'; exit 2 }
[void][PR]::UnregisterHotKey([IntPtr]::Zero, 0x777C)

$port = Get-Random -Minimum 41000 -Maximum 48000
$title = "PowerRing test " + [guid]::NewGuid().ToString('N').Substring(0, 8)
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
$secret = 'ring-text-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
function Config($label) { @"
{
  // test ring
  "version": 1, "hotkey": "Ctrl+Alt+Shift+F9", "startProfile": "t1",
  "appearance": { "animationMs": 0 },
  "profiles": [
    { "id": "t1", "name": "T1", "icon": "code", "items": [
      { "label": "$label", "action": "url", "target": "http://localhost:$port/" },
      { "label": "Sub", "items": [
        { "label": "Copy text", "action": "text", "target": "$secret" },
        { "label": "Deep", "items": [ { "label": "Leaf", "action": "text", "target": "leaf" } ] }
      ] },
      { "label": "Shot", "action": "screenshot" },
    ] },
    { "id": "t2", "name": "T2", "items": [ { "label": "Only", "action": "text", "target": "only" } ] },
    { "id": "t3", "name": "T3", "kind": "board", "tables": [
      { "title": "Copies", "kind": "clipboard", "columns": 2, "keep": 5 },
      { "title": "Notes", "kind": "notes", "columns": 2 }
    ] }
  ]
}
"@ }
Set-Content $cfg (Config 'Test page') -Encoding utf8

$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process $exe -ArgumentList '--config', "`"$cfg`"" -PassThru
function RingHandle { return [PR]::FindWindow([NullString]::Value, 'Power Ring') }
function Ring-Visible { $h = RingHandle; return $h -ne [IntPtr]::Zero -and [PR]::IsWindowVisible($h) }
function Name { return $A::FromHandle((RingHandle)).Current.Name }
function Buttons {
  $cond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  return (@($A::FromHandle((RingHandle)).FindAll($T::Descendants, $cond)) | ForEach-Object { $_.Current.Name }) -join '|'
}
function Safe-Key([byte]$vk, [byte[]]$mods = @()) {
  if ([PR]::Pid([PR]::GetForegroundWindow()) -ne $p.Id) { throw "Safety stop: the foreground window is not Power Ring; refusing to send key 0x$('{0:X}' -f $vk)." }
  [PR]::Key($mods, $vk); Start-Sleep -Milliseconds 350
}
function Show-Ring {
  [PR]::Key($HOT, $F9)
  for ($i = 0; $i -lt 60 -and -not (Ring-Visible); $i++) { Start-Sleep -Milliseconds 50 }
  Start-Sleep -Milliseconds 250
  if (-not (Ring-Visible)) { throw 'The ring did not appear.' }
}
function Clip { return (Get-Clipboard -Raw) }

try {
for ($i = 0; $i -lt 80 -and -not (Test-Path (Join-Path $root 'ring.schema.json')); $i++) { Start-Sleep -Milliseconds 50 }
Start-Sleep -Seconds 2; $p.Refresh()
Rec 'start: helper files written next to ring.json' ((Test-Path (Join-Path $root 'ring.schema.json')) -and (Test-Path (Join-Path $root 'RING_CONFIG.md')))
Rec 'start: no ring shown until asked' (-not (Ring-Visible))
$idle = [Math]::Round($p.PrivateMemorySize64 / 1MB, 1)
Rec 'idle: private memory under 60 MB' ($idle -lt 60) "$idle MB"
$cpu0 = $p.TotalProcessorTime; Start-Sleep -Seconds 5; $p.Refresh()
$cpu = ($p.TotalProcessorTime - $cpu0).TotalMilliseconds
Rec 'idle: no CPU' ($cpu -lt 30) "$([Math]::Round($cpu,1)) ms in 5 s"

# 1. Hotkey: ring at the pointer, focused, profile buttons + slots.
[void][PR]::SetCursorPos(800, 500)
$t0 = [Diagnostics.Stopwatch]::StartNew(); Show-Ring; $first = $t0.ElapsedMilliseconds
Rec 'hotkey: ring shows, focused' ([PR]::Pid([PR]::GetForegroundWindow()) -eq $p.Id) "$first ms incl. wait"
$names = (Buttons) -split '\|'
Rec 'first circle: buttons, children behind them, centre, workspace switchers' ((@('Test page', 'Sub ›', 'Shot', 'Copy text', 'Deep ›', 'Leaf', 'T1', 'Workspace: T3', 'Workspace: T2') | Where-Object { $names -notcontains $_ }).Count -eq 0) (Buttons)
$rect = New-Object PR+RECT; [void][PR]::GetWindowRect((RingHandle), [ref]$rect)
Rec 'placement: horizontally centred on the pointer' ([Math]::Abs(($rect.L + $rect.R) / 2 - 800) -le 3) "window $($rect.L)..$($rect.R)"

# 2. Levels 2 and 3, then back.
Safe-Key 0x32
Rec 'digit 2: second circle' ((Name) -eq 'Power Ring - T1 › Sub') (Name)
Safe-Key 0x32
Rec 'digit 2 again: third circle' ((Name) -eq 'Power Ring - T1 › Sub › Deep' -and (Buttons) -like '*Leaf*') (Name)
Safe-Key 0x1B
Rec 'Esc: back one level' ((Name) -eq 'Power Ring - T1 › Sub') (Name)
Safe-Key 0x08
Rec 'Backspace: back to the first circle' ((Name) -eq 'Power Ring - T1') (Name)

# 3. Profiles: Tab, Ctrl+1.
Safe-Key 0x09
Rec 'Tab: next workspace, all icons change' ((Name) -eq 'Power Ring - T2' -and (Buttons) -like '*Only*' -and (Buttons) -notlike '*Test page*') (Buttons)
Safe-Key 0x31 @(0x11)
Rec 'Ctrl+1: first profile' ((Name) -eq 'Power Ring - T1') (Name)
Safe-Key 0x1B
Rec 'Esc on the first circle: hidden' (-not (Ring-Visible))

# 4. Actions: url (page opens), text (clipboard).
$before = $hits.Count
Show-Ring; Safe-Key 0x31
$page = $false; for ($i = 0; $i -lt 40 -and -not $page; $i++) { Start-Sleep -Milliseconds 250; $h = [PR]::FindTitle($title); if ($h -ne [IntPtr]::Zero) { $page = $true; [void][PR]::SendMessage($h, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) } }
Rec 'url action: ring hidden, page opened' ((-not (Ring-Visible)) -and $page -and $hits.Count -gt $before) "requests=$($hits.Count)"
Start-Sleep -Milliseconds 800
Show-Ring; Safe-Key 0x32; Safe-Key 0x31; Start-Sleep -Milliseconds 400
Rec 'text action: copied to the clipboard' ((Clip) -eq $secret)

# 4b. A child shown behind its parent runs directly (UI Automation Invoke, no synthesized input).
Set-Clipboard 'placeholder'
Show-Ring
$cond = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Copy text')
$child = $A::FromHandle((RingHandle)).FindFirst($T::Descendants, $cond)
$child.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 600
Rec 'child behind its parent: runs from the first circle' ((-not (Ring-Visible)) -and (Clip) -eq $secret)

# 4c. Board: the last copies (memory only) and quick notes.
$copied = 'copied-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
Set-Clipboard $copied; Start-Sleep -Milliseconds 600
Show-Ring; Safe-Key 0x72
Rec 'board: F3 opens it, last copy listed' ((Name) -eq 'Power Ring - T3' -and (Buttons) -like "*Copied text: $copied*") (Buttons)
Safe-Key 0x27
Rec 'board: Right shows the next table' ((Buttons) -like '*Workspace: T3 (active)*' -and ($A::FromHandle((RingHandle)).FindFirst($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'New note'))) -ne $null))
foreach ($ch in 'NOTEABC'.ToCharArray()) { Safe-Key ([byte][char]$ch) }
Safe-Key 0x0D; Start-Sleep -Milliseconds 400
$notesFile = Join-Path $root 'notes.json'
Rec 'board: a typed note is saved in notes.json' ((Test-Path $notesFile) -and (Get-Content $notesFile -Raw) -match 'noteabc') ((Get-Content $notesFile -Raw -ErrorAction SilentlyContinue) -replace '\s+', ' ')
Rec 'board: no copied text written to disk' (-not (Get-ChildItem $root -Recurse -File | Where-Object { (Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue) -match $copied }))
Safe-Key 0x31 @(0x11)   # the ring reopens on the last workspace: go back to T1 for the next checks
Safe-Key 0x1B; Start-Sleep -Milliseconds 300

# 5. Hot reload: a saved change is applied; a broken file is reported and the last good ring stays.
Set-Content $cfg (Config 'Renamed page') -Encoding utf8; Start-Sleep -Milliseconds 1200
Show-Ring
Rec 'reload: saved change applied' ((Buttons) -like '*Renamed page*') (Buttons)
Safe-Key 0x1B
Set-Content $cfg '{ "version": 1, "profiles": [ oops ] }' -Encoding utf8
$notice = $false; for ($i = 0; $i -lt 30 -and -not $notice; $i++) { Start-Sleep -Milliseconds 100; $notice = [PR]::FindWindow([NullString]::Value, 'Power Ring notice') -ne [IntPtr]::Zero }
Rec 'reload: broken file reported' $notice
Start-Sleep -Milliseconds 500
Show-Ring
Rec 'reload: last good ring still active' ((Buttons) -like '*Renamed page*')
Safe-Key 0x1B

# 6. Second start: --profile and --show go to the running instance; --exit closes it.
Set-Content $cfg (Config 'Test page') -Encoding utf8; Start-Sleep -Milliseconds 1200
Start-Process $exe -ArgumentList '--config', "`"$cfg`"", '--profile', '2' -Wait
Start-Process $exe -ArgumentList '--config', "`"$cfg`"", '--show' -Wait
for ($i = 0; $i -lt 40 -and -not (Ring-Visible); $i++) { Start-Sleep -Milliseconds 50 }
Rec 'second start: --profile 2 --show reach the running ring' ((Ring-Visible) -and (Name) -eq 'Power Ring - T2') (Name)
$p.Refresh(); $used = [Math]::Round($p.PrivateMemorySize64 / 1MB, 1)
Rec 'after use: private memory under 90 MB' ($used -lt 90) "$used MB"
Start-Process $exe -ArgumentList '--config', "`"$cfg`"", '--exit' -Wait
[void]$p.WaitForExit(5000)
Rec 'exit: --exit closes it' $p.HasExited
$free = [PR]::RegisterHotKey([IntPtr]::Zero, 0x777C, 0x4007, $F9); if ($free) { [void][PR]::UnregisterHotKey([IntPtr]::Zero, 0x777C) }
Rec 'exit: hotkey released' $free
} catch { Rec 'run aborted' $false $_.Exception.Message }
finally {
  if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  $listener.Stop(); $listener.Close(); Stop-Job $job -ErrorAction SilentlyContinue; Remove-Job $job -Force -ErrorAction SilentlyContinue
}
$r.GetEnumerator() | ForEach-Object { '{0,-52} {1}' -f $_.Key, $_.Value }
"data root: $root"
if (@($r.Values | Where-Object { $_ -like 'FAIL*' }).Count -gt 0) { 'Power Ring native checks: FAIL'; exit 1 }
'Power Ring native checks: PASS'
