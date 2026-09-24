# Native Windows check for V2.1 web apps (app-window mode), using a fresh isolated --data-dir and a
# throwaway local HTTP page that stands in for Mongoku. Clicks the Quick Shelf and presses Ctrl+Alt+Shift+W.
# Closes only the test app window it opened (matched by its unique page title); other browser windows are untouched.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class N {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool UnregisterHotKey(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static IntPtr FindTitle(string needle) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { var s = new StringBuilder(512); GetWindowText(h, s, 512);
      if (IsWindowVisible(h) && s.ToString().Contains(needle)) { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
  public static string Class(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(150); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
  public static void Key(byte[] mods, byte key) {
    foreach (var m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
    keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero);
    for (int i = mods.Length - 1; i >= 0; i--) keybd_event(mods[i], 0, 2, UIntPtr.Zero);
  }
}
'@
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
$exe = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\JUtility.App\bin\Release\net8.0-windows\JUtilityPalette.exe'
$root = Join-Path $env:TEMP ("powerops-webapps-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $root | Out-Null
$r = [ordered]@{}
function Rec($name, $ok, $detail = '') { $r[$name] = $(if ($ok) { 'PASS' } else { 'FAIL' }) + $(if ($detail) { "  ($detail)" } else { '' }) }

if (-not [N]::RegisterHotKey([IntPtr]::Zero, 0x7779, 0x4007, 0x57)) { 'BLOCKED: Ctrl+Alt+Shift+W is already owned by another application.'; exit 2 }
[void][N]::UnregisterHotKey([IntPtr]::Zero, 0x7779)

# Throwaway page on a free localhost port.
$port = Get-Random -Minimum 41000 -Maximum 48000
$token = [guid]::NewGuid().ToString('N').Substring(0, 8)
$title = "PowerOps web app test $token"
$listener = [System.Net.HttpListener]::new(); $listener.Prefixes.Add("http://localhost:$port/"); $listener.Start()
$hits = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
$serve = {
  param($l, $q, $t)
  while ($l.IsListening) {
    try { $ctx = $l.GetContext() } catch { break }
    $q.Enqueue($ctx.Request.Url.AbsolutePath + ' ' + $ctx.Request.UserAgent)
    $bytes = [Text.Encoding]::UTF8.GetBytes("<!doctype html><title>$t</title><h1>$t</h1>")
    $ctx.Response.ContentType = 'text/html'; $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length); $ctx.Response.Close()
  }
}
$job = Start-ThreadJob -ScriptBlock $serve -ArgumentList $listener, $hits, $title

$appId = [guid]::NewGuid()
$actionId = 'web:' + $appId.ToString('N')
$settings = [ordered]@{
  Format = 'powerops-quick-actions'; SchemaVersion = 1; Mode = 'QuickShelf'; GlobalShortcutsEnabled = $true
  GlobalShortcuts = @(@{ Gesture = 'Ctrl+Alt+Shift+W'; ActionId = $actionId })
  Ring = @('capture.region'); Shelf = @($actionId, 'app.open'); ShelfOrientation = 'Horizontal'; ShelfAlwaysOnTop = $true; ShelfAutoHide = $false
  WorkspaceOverrides = @(); WebApps = @(@{ Id = $appId; Name = 'Test Mongoku'; Url = "http://localhost:$port/"; OpenMode = 'AppWindow'; Browser = 'Auto' })
}
Set-Content (Join-Path $root 'quick-actions.json') ($settings | ConvertTo-Json -Depth 5) -Encoding utf8

$p = Start-Process $exe -ArgumentList '--data-dir', "`"$root`"" -PassThru
for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
Start-Sleep -Seconds 3
$shelf = $null
foreach ($w in $A::RootElement.FindAll($T::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)))) { if ($w.Current.Name -eq 'Power Ops Quick Shelf') { $shelf = $w } }
$btnCond = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Test Mongoku')
$btn = if ($shelf) { $shelf.FindFirst($T::Descendants, $btnCond) } else { $null }
Rec 'shelf: web app button present with its name' ($null -ne $btn)
Rec 'nothing requested before the user acts' ($hits.Count -eq 0) "requests=$($hits.Count)"

function Wait-AppWindow { for ($i = 0; $i -lt 40; $i++) { $h = [N]::FindTitle($title); if ($h -ne [IntPtr]::Zero) { return $h }; Start-Sleep -Milliseconds 250 }; return [IntPtr]::Zero }
function Close-AppWindow($h) { if ($h -ne [IntPtr]::Zero) { [void][N]::SendMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 800 } }

if ($btn) {
  $b = $btn.Current.BoundingRectangle; [N]::Click([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2))
  $h = Wait-AppWindow
  Rec 'shelf click: app window opened on the page' ($h -ne [IntPtr]::Zero) $(if ($h -ne [IntPtr]::Zero) { "class=$([N]::Class($h))" } else { '' })
  Rec 'shelf click: page requested' ($hits.Count -ge 1) "requests=$($hits.Count)"
  Close-AppWindow $h
  Rec 'test app window closed' ([N]::FindTitle($title) -eq [IntPtr]::Zero)
}

$before = $hits.Count
[N]::Key(@(0x11, 0x12, 0x10), 0x57)
$h = Wait-AppWindow
Rec 'global shortcut: same web app opens' ($h -ne [IntPtr]::Zero -and $hits.Count -gt $before) "requests=$($hits.Count)"
Close-AppWindow $h

[void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000)
Rec 'exit: Power Ops closed' $p.HasExited
$listener.Stop(); $listener.Close(); Stop-Job $job -ErrorAction SilentlyContinue; Remove-Job $job -Force -ErrorAction SilentlyContinue
$r.GetEnumerator() | ForEach-Object { '{0,-48} {1}' -f $_.Key, $_.Value }
"data root: $root"
if (@($r.Values | Where-Object { $_ -like 'FAIL*' }).Count -gt 0) { 'Web app native checks: FAIL'; exit 1 }
'Web app native checks: PASS'
