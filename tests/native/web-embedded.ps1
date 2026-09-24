# Native Windows check + memory measurement for V2.1 embedded web apps (WebView2 "Web" tab), using a fresh
# isolated --data-dir and a throwaway localhost page standing in for Mongoku (it counts loads in localStorage).
# Input: only a global shortcut (Ctrl+Alt+Shift+M, consumed by Windows) and UI Automation Invoke; no clicks/typing.
# Does not click links that would open the user's own browser. Never points at the personal workspace.
param([string]$RealUrl, [string]$AppName = 'Mongoku')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class E {
  [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool UnregisterHotKey(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static void Hot(byte key) {
    foreach (byte k in new byte[] { 0x11, 0x12, 0x10 }) keybd_event(k, 0, 0, UIntPtr.Zero);
    keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero);
    foreach (byte k in new byte[] { 0x10, 0x12, 0x11 }) keybd_event(k, 0, 2, UIntPtr.Zero);
  }
}
'@
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
$exe = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\JUtility.App\bin\Release\net8.0-windows\JUtilityPalette.exe'
$root = Join-Path $env:TEMP ("powerops-embedded-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $root | Out-Null
$r = [ordered]@{}; $mem = [ordered]@{}
function Rec($name, $ok, $detail = '') { $r[$name] = $(if ($ok) { 'PASS' } else { 'FAIL' }) + $(if ($detail) { "  ($detail)" } else { '' }) }
if (-not [E]::RegisterHotKey([IntPtr]::Zero, 0x777C, 0x4007, 0x4D)) { 'BLOCKED: Ctrl+Alt+Shift+M is already owned by another application.'; exit 2 }
[void][E]::UnregisterHotKey([IntPtr]::Zero, 0x777C)

$port = Get-Random -Minimum 41000 -Maximum 48000
$listener = [System.Net.HttpListener]::new(); $listener.Prefixes.Add("http://localhost:$port/"); $listener.Start()
$hits = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
$job = Start-ThreadJob -ArgumentList $listener, $hits -ScriptBlock {
  param($l, $q)
  $page = "<!doctype html><title>embed</title><script>let n=+(localStorage.getItem('n')||0)+1;localStorage.setItem('n',n);document.title='PowerOps embed test n='+n;</script><h1>Mongoku stand-in</h1>"
  while ($l.IsListening) {
    try { $ctx = $l.GetContext() } catch { break }
    $q.Enqueue($ctx.Request.Url.AbsolutePath + ' | ' + $ctx.Request.UserAgent)
    $b = [Text.Encoding]::UTF8.GetBytes($page)
    $ctx.Response.ContentType = 'text/html'; $ctx.Response.OutputStream.Write($b, 0, $b.Length); $ctx.Response.Close()
  }
}
$appId = [guid]::NewGuid(); $web = 'web:' + $appId.ToString('N')
$settings = [ordered]@{
  Format = 'powerops-quick-actions'; SchemaVersion = 1; Mode = 'Off'; GlobalShortcutsEnabled = $true
  GlobalShortcuts = @(@{ Gesture = 'Ctrl+Alt+Shift+M'; ActionId = $web })
  Ring = @('capture.region'); Shelf = @('app.open'); ShelfOrientation = 'Horizontal'; ShelfAlwaysOnTop = $true; ShelfAutoHide = $false; WorkspaceOverrides = @()
  WebApps = @(@{ Id = $appId; Name = $(if ($RealUrl) { $AppName } else { 'Test Mongoku' }); Url = $(if ($RealUrl) { $RealUrl } else { "http://localhost:$port/" }); OpenMode = 'Embedded'; Browser = 'Auto' })
}
Set-Content (Join-Path $root 'quick-actions.json') ($settings | ConvertTo-Json -Depth 5) -Encoding utf8

function Descendants([int]$rootPid) {
  $all = Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, Name, WorkingSetSize
  $ids = @($rootPid); $found = @()
  do { $next = @($all | Where-Object { $ids -contains $_.ParentProcessId -and $found.ProcessId -notcontains $_.ProcessId }); $found += $next; $ids = @($next.ProcessId) } while ($next.Count -gt 0)
  return $found
}
function Snapshot($label, $p) {
  $p.Refresh(); $kids = @(Descendants $p.Id | Where-Object Name -eq 'msedgewebview2.exe')
  $mb = [Math]::Round(($p.WorkingSet64 + ($kids | Measure-Object WorkingSetSize -Sum).Sum) / 1MB, 1)
  $mem[$label] = "Power Ops $([Math]::Round($p.WorkingSet64 / 1MB, 1)) MB + $($kids.Count) WebView2 processes $([Math]::Round((($kids | Measure-Object WorkingSetSize -Sum).Sum) / 1MB, 1)) MB = $mb MB"
  return $kids.Count
}
function WebView2Loaded($p) { $p.Refresh(); return @($p.Modules | Where-Object { $_.ModuleName -match '^(Microsoft\.Web\.WebView2|WebView2Loader)' }).Count -gt 0 }
function Title($p) {
  $main = $A::FromHandle($p.MainWindowHandle)
  $txt = $main.FindAll($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) | Where-Object { $_.Current.Name -like $(if ($RealUrl) { "$AppName · *" } else { 'Test Mongoku*n=*' }) } | Select-Object -First 1
  return $(if ($txt) { $txt.Current.Name } else { '' })
}
function Wait-Title($p, [string]$suffix) { for ($i = 0; $i -lt 60; $i++) { $seen = Title $p; if ($seen -like "*$suffix") { return $seen }; Start-Sleep -Milliseconds 250 }; return Title $p }
function Invoke-Named($p, [string]$name) {
  $el = $A::FromHandle($p.MainWindowHandle).FindFirst($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)))
  if (-not $el) { throw "UI element not found: $name" }
  $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Start-App { $p = Start-Process $exe -ArgumentList '--data-dir', "`"$root`"" -PassThru; for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }; Start-Sleep -Seconds 4; return $p }

function N([string]$title) { if ($title -match 'n=(\d+)$') { return [int]$Matches[1] }; return -1 }
function Wait-Gone($p) { for ($i = 0; $i -lt 40; $i++) { if (@(Descendants $p.Id | Where-Object Name -eq 'msedgewebview2.exe').Count -eq 0) { return $true }; Start-Sleep -Milliseconds 250 }; return $false }
function Invoke-Menu($p, [string]$menu, [string]$item) {
  $m = $A::FromHandle($p.MainWindowHandle).FindFirst($T::Descendants, (New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $menu)),
    (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)))))
  $m.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
  $A::RootElement.FindFirst($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $item))).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

$p = Start-App
try {
  # 1. Lazy: nothing browser-related exists before the first embedded open.
  $kids = Snapshot '1 idle, never opened' $p
  Rec 'lazy: no WebView2 processes before first use' ($kids -eq 0)
  Rec 'lazy: WebView2 libraries not even loaded' (-not (WebView2Loaded $p))
  Rec 'lazy: no browser profile folder created' (-not (Test-Path (Join-Path $root 'webview2')))

  # 2. Shortcut opens the app inside Power Ops.
  [E]::Hot(0x4D)
  if ($RealUrl) {
    $title = ''; for ($i = 0; $i -lt 80 -and $title -notlike "$AppName · ?*"; $i++) { Start-Sleep -Milliseconds 250; $title = Title $p }
    Rec 'real app: page loaded in a Power Ops Web tab' ($title -like "$AppName · ?*") $title
  } else {
    $title = Wait-Title $p 'n=1'; $n = N $title
    Rec 'open: embedded page loaded in a Power Ops Web tab' ($n -eq 1) $title
    Rec 'open: request came from the embedded WebView2' ((@($hits) | Where-Object { $_ -match 'Edg/' }).Count -ge 1)
  }
  Rec 'open: Power Ops in front' ([E]::GetForegroundWindow() -eq $p.MainWindowHandle)
  Start-Sleep -Seconds 2
  Rec 'open: tab saved in the workspace view' ((Get-Content (Join-Path $root 'shell-workspaces.json') -Raw) -match [regex]::Escape($web))
  Start-Sleep -Seconds $(if ($RealUrl) { 8 } else { 3 })
  $kids = Snapshot '2 one embedded page open' $p
  Rec 'memory: WebView2 processes started only now' ($kids -gt 0) "$kids processes"
  Rec 'profile: stored in the Power Ops data folder' (Test-Path (Join-Path $root 'webview2'))

  if (-not $RealUrl) {
    # 3. Reload keeps localStorage; toolbar works through UI Automation.
    Invoke-Named $p 'Reload'
    $title = Wait-Title $p "n=$($n + 1)"
    Rec 'reload: page state persists (localStorage)' ((N $title) -eq $n + 1) $title; $n = N $title

    # 3b. Another tab and back: the live page is kept, not reloaded.
    Invoke-Menu $p 'Tabs' 'New tab (Ctrl+T)'; Start-Sleep -Milliseconds 800
    $before = $hits.Count
    [E]::Hot(0x4D); Start-Sleep -Milliseconds 1500
    $title = Title $p
    Rec 'other tab and back: page kept, not reloaded' ((N $title) -eq $n -and $hits.Count -eq $before) "$title, requests +$($hits.Count - $before)"
  }

  # 4. Close web view frees the browser.
  Invoke-Named $p 'Close web view'
  $gone = Wait-Gone $p
  $kids = Snapshot '3 after Close web view' $p
  Rec 'close: all WebView2 processes exited' $gone "$kids left"

  if (-not $RealUrl) {
    # 5. Reopen from the shortcut: same profile, state continues.
    [E]::Hot(0x4D)
    $title = Wait-Title $p "n=$($n + 1)"
    Rec 'reopen: loads again with its saved state' ((N $title) -eq $n + 1) $title; $n = N $title

    # 6. Closing the tab (Tabs menu, via UI Automation) disposes the view.
    Invoke-Menu $p 'Tabs' 'Close tab (Ctrl+W)'
    Rec 'close tab: view disposed, processes exited' (Wait-Gone $p)
  }

  # 7. Restart: profile persisted on disk, still lazy.
  [void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000)
  Rec 'exit: clean, no WebView2 left behind' ($p.HasExited -and @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $_.CommandLine -match [regex]::Escape($root) }).Count -eq 0)
  if (-not $RealUrl) {
    $p = Start-App
    $kids = Snapshot '4 restarted, not reopened' $p
    Rec 'restart: still lazy until opened' ($kids -eq 0 -and -not (WebView2Loaded $p))
    [E]::Hot(0x4D)
    $title = Wait-Title $p "n=$($n + 1)"
    Rec 'restart: saved state survives restart' ((N $title) -eq $n + 1) $title
    [void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000)
  }
}
catch { Rec 'run aborted' $false $_.Exception.Message }
finally {
  if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  $listener.Stop(); $listener.Close(); Stop-Job $job -ErrorAction SilentlyContinue; Remove-Job $job -Force -ErrorAction SilentlyContinue
}
$r.GetEnumerator() | ForEach-Object { '{0,-52} {1}' -f $_.Key, $_.Value }
"--- memory (working set) ---"; $mem.GetEnumerator() | ForEach-Object { '{0,-30} {1}' -f $_.Key, $_.Value }
"data root: $root"
if (@($r.Values | Where-Object { $_ -like 'FAIL*' }).Count -gt 0) { 'Embedded web native checks: FAIL'; exit 1 }
'Embedded web native checks: PASS'
