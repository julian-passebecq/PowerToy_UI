# Native Windows release acceptance for Power Ops V2, using a fresh isolated --data-dir (synthetic profile).
# Covers: Repository Hub, Portal Launcher, Capture, Clipboard copy, Launchpad, saved workspaces/tabs/views across a
# restart, Quick actions ribbon, File tray page, Claude sessions page, focus behaviour, per-monitor DPI awareness on
# every connected monitor, and light/dark theme of the V2 chrome (JUTILITY_THEME override, never the Windows setting).
# Input is UI Automation patterns only, plus ONE synthesized Ctrl+K that is sent only while the test window owns the
# foreground. The clipboard is replaced once (Copy now); plain text that was on it before is put back at the end.
# Never points at the personal workspace; the default %LOCALAPPDATA%\JUtilityPalette folder is hashed before and after.
# Run with PowerShell 7 (pwsh) after .\scripts\build.ps1, or pass -Exe for an installed/extracted package.
param(
  [string] $Exe = '',
  [string] $JsonOut = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class RA {
  static readonly IntPtr PMv2 = new IntPtr(-4);
  [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr h);
  [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);
  [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr m, int t, out uint x, out uint y);
  [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonEnum cb, IntPtr d);
  delegate bool MonEnum(IntPtr m, IntPtr hdc, ref RECT r, IntPtr d);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO i);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MONITORINFO { public int Size; public RECT Mon; public RECT Work; public uint Flags; }
  public static void Aware() { SetThreadDpiAwarenessContext(PMv2); }
  public static string Awareness(IntPtr h) {
    Aware(); IntPtr c = GetWindowDpiAwarenessContext(h);
    if (AreDpiAwarenessContextsEqual(c, PMv2)) return "PerMonitorV2";
    int a = GetAwarenessFromDpiAwarenessContext(c);
    return a == 0 ? "Unaware" : a == 1 ? "SystemAware" : a == 2 ? "PerMonitor(V1)" : "Unknown(" + a + ")";
  }
  public static uint WindowDpi(IntPtr h) { Aware(); return GetDpiForWindow(h); }
  public static List<int[]> Monitors() {
    Aware(); var list = new List<int[]>();
    EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr hdc, ref RECT r, IntPtr d) => {
      var i = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) }; GetMonitorInfo(m, ref i);
      uint x, y; GetDpiForMonitor(m, 0, out x, out y);
      list.Add(new[] { i.Work.L, i.Work.T, i.Work.R, i.Work.B, (int)x, (i.Flags & 1) == 1 ? 1 : 0 }); return true; }, IntPtr.Zero);
    return list;
  }
  public static void Move(IntPtr h, int x, int y) { Aware(); SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010); }
  public static int[] Rect(IntPtr h) { Aware(); RECT r; GetWindowRect(h, out r); return new[] { r.L, r.T, r.R, r.B }; }
  public static uint Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
  [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
  [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
  // Renders the window itself (not the screen), so other applications are never captured. x/y are fractions of the window.
  public static int[] Sample(IntPtr h, double fx, double fy) {
    Aware(); RECT r; GetWindowRect(h, out r); int w = Math.Max(1, r.R - r.L), ht = Math.Max(1, r.B - r.T);
    IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, w, ht), old = SelectObject(mem, bmp);
    try { PrintWindow(h, mem, 2); uint c = GetPixel(mem, (int)(fx * w), (int)(fy * ht)); return new[] { (int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF) }; }
    finally { SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); }
  }
  public static void CtrlK() { keybd_event(0x11, 0, 0, UIntPtr.Zero); keybd_event(0x4B, 0, 0, UIntPtr.Zero); keybd_event(0x4B, 0, 2, UIntPtr.Zero); keybd_event(0x11, 0, 2, UIntPtr.Zero); }
}
'@
$A = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]; $CT = [System.Windows.Automation.ControlType]
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Exe) { $Exe = Join-Path $repo 'src\JUtility.App\bin\Release\net8.0-windows10.0.19041.0\JUtilityPalette.exe' }
$root = Join-Path $env:TEMP ('powerops-release-test-' + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $root | Out-Null
$defaultDir = Join-Path $env:LOCALAPPDATA 'JUtilityPalette'
# Synthetic, empty Claude home and audit folder: the Sessions tab and audit card never read the real profile in runs 1-2.
$claudeHome = Join-Path $root 'claude-home'; New-Item -ItemType Directory $claudeHome | Out-Null
$synthetic = @{ JUTILITY_CLAUDE_HOME = $claudeHome; JUTILITY_AUDITS_DIR = (Join-Path $claudeHome 'audits') }
function DirHash([string]$d) { if (-not (Test-Path $d)) { return 'absent' }; (@(Get-ChildItem $d -Recurse -File | Sort-Object FullName | ForEach-Object { $_.FullName.Substring($d.Length) + '=' + (Get-FileHash $_.FullName).Hash }) -join ';').GetHashCode() }
$defaultBefore = DirHash $defaultDir
$clipBefore = try { Get-Clipboard -Raw } catch { $null }

$r = [ordered]@{}
function Rec([string]$name, $ok, [string]$detail = '') { $r[$name] = [ordered]@{ result = $(if ($ok -is [string] -and $ok -eq 'BLOCKED') { 'BLOCKED' } elseif ($ok) { 'PASS' } else { 'FAIL' }); detail = $detail } }
function Cond($prop, $val) { New-Object System.Windows.Automation.PropertyCondition($prop, $val) }
function Find($parent, [string]$name, $type = $null, [int]$tries = 40) {
  $c = Cond $A::NameProperty $name
  if ($type) { $c = New-Object System.Windows.Automation.AndCondition($c, (Cond $A::ControlTypeProperty $type)) }
  for ($i = 0; $i -lt $tries; $i++) { $e = $parent.FindFirst($TS::Descendants, $c); if ($e) { return $e }; Start-Sleep -Milliseconds 150 }
  return $null
}
function Need($parent, [string]$name, $type = $null) { $e = Find $parent $name $type; if (-not $e) { throw "UI element not found: $name" }; $e }
function Invoke-El($e) { $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 350 }
function Set-Val($e, [string]$v) { $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($v); Start-Sleep -Milliseconds 250 }
function Nav($main, [string]$name) { Invoke-El (Need $main $name $CT::Button); Start-Sleep -Milliseconds 400 }
function Menu($p, $main, [string]$menu, [string]$item) {
  $m = Need $main $menu $CT::MenuItem
  $m.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
  $c = New-Object System.Windows.Automation.AndCondition((Cond $A::ProcessIdProperty $p.Id), (Cond $A::NameProperty $item), (Cond $A::ControlTypeProperty $CT::MenuItem))
  $e = $A::RootElement.FindFirst($TS::Descendants, $c); if (-not $e) { throw "Menu item not found: $menu > $item" }
  Invoke-El $e
}
function Rows($e) { @($e.FindAll($TS::Children, (New-Object System.Windows.Automation.OrCondition((Cond $A::ControlTypeProperty $CT::DataItem), (Cond $A::ControlTypeProperty $CT::ListItem))))).Count }
function Shell { Get-Content (Join-Path $root 'shell-workspaces.json') -Raw | ConvertFrom-Json }
function ActiveProfile { $s = Shell; $s.Workspaces | Where-Object Id -eq $s.ActiveWorkspaceId }
function Start-App([hashtable]$envs = @{}) {
  $saved = @{}; foreach ($k in $envs.Keys) { $saved[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $envs[$k]) }
  try { $p = Start-Process $Exe -ArgumentList '--data-dir', "`"$root`"" -PassThru }
  finally { foreach ($k in $saved.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) } }
  for ($i = 0; $i -lt 80 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
  Start-Sleep -Seconds 3; $p.Refresh(); return $p
}
function Stop-App($p) {
  if ($p.HasExited) { return $true }
  [void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000)
  if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; return $false }; return $true
}
# Background between the "+ Workspace" and "Customize" buttons of the V2 toolbar (chrome built in code).
function ChromeLuma($p, $main) {
  # (PowerShell names are case-insensitive: never call a local $a here, it would hide $A.)
  $left = (Need $main '+ Workspace' $CT::Button).Current.BoundingRectangle; $right = (Need $main 'Customize' $CT::Button).Current.BoundingRectangle
  $w = $main.Current.BoundingRectangle
  $c = [RA]::Sample($p.MainWindowHandle, ((($left.Right + $right.Left) / 2) - $w.Left) / $w.Width, ((($left.Top + $left.Bottom) / 2) - $w.Top) / $w.Height)
  [int](0.2126 * $c[0] + 0.7152 * $c[1] + 0.0722 * $c[2])
}

$token = 'powerops-release-' + [guid]::NewGuid().ToString('N').Substring(0, 10)
$p = $null
try {
  # ---- Run 1: light theme, modules, clipboard, focus, DPI ------------------------------------------------------
  $p = Start-App ($synthetic + @{ JUTILITY_THEME = 'light' })
  $main = $A::FromHandle($p.MainWindowHandle)
  Rec 'startup: main window shown for the isolated data dir' ($main.Current.Name -like '*Power Ops*') $main.Current.Name
  $fg = [RA]::GetForegroundWindow()
  # A process started from a background shell may not take the foreground (Windows foreground lock): not an app defect.
  Rec 'focus: new window owns the foreground at startup' $(if ($fg -eq $p.MainWindowHandle) { $true } else { 'BLOCKED' }) $(if ($fg -ne $p.MainWindowHandle) { "Windows foreground lock: foreground stayed with $((Get-Process -Id ([RA]::Pid($fg)) -ErrorAction SilentlyContinue).ProcessName)" })
  Rec 'storage: shell-workspaces.json written in the data dir' (Test-Path (Join-Path $root 'shell-workspaces.json'))

  # Repository Hub: add a synthetic project, search filters it, clearing restores.
  Nav $main 'Repository Hub'
  $grid = Need $main 'Repository projects' $CT::DataGrid
  $before = Rows $grid
  Invoke-El (Need $main '+ Project' $CT::Button); Start-Sleep -Milliseconds 500
  $added = Rows $grid
  $search = Need $main 'Repository search' $CT::Edit
  Set-Val $search 'zz-no-such-project-zz'; Start-Sleep -Milliseconds 500; $none = Rows $grid
  Set-Val $search ''; Start-Sleep -Milliseconds 500; $back = Rows $grid
  Rec 'Repository Hub: + Project adds a row' ($added -eq $before + 1) "rows $before -> $added"
  Rec 'Repository Hub: search filters and clears' ($none -eq 0 -and $back -eq $added) "no-match=$none cleared=$back"

  # Portal Launcher: add a portal and name it.
  Nav $main 'Portal Launcher'
  $portals = Need $main 'Portals' $CT::List
  $pb = Rows $portals
  Invoke-El (Need $main '+ Portal' $CT::Button); Start-Sleep -Milliseconds 400
  Set-Val (Need $main 'Portal name' $CT::Edit) "Synthetic portal $token"
  Set-Val (Need $main 'Main portal URL' $CT::Edit) 'https://example.invalid/portal'
  $selName = (Need $main 'Portal name' $CT::Edit).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
  Rec 'Portal Launcher: + Portal selects the new entry for editing' ($selName -eq "Synthetic portal $token") $selName

  # Capture: create a capture and give it a title.
  Nav $main 'Capture'
  $caps = Need $main 'Captures' $CT::List
  $cb = Rows $caps
  Invoke-El (Need $main '+ Capture' $CT::Button); Start-Sleep -Milliseconds 400
  Set-Val (Need $main 'Capture title' $CT::Edit) "Synthetic capture $token"
  Rec 'Capture: + Capture adds an entry' ((Rows $caps) -eq $cb + 1) "entries $cb -> $(Rows $caps)"

  # Clipboard: a snippet's Copy now puts exactly its text on the Windows clipboard.
  Nav $main 'Clipboard Library'
  Invoke-El (Need $main '+ Snippet' $CT::Button); Start-Sleep -Milliseconds 400
  Set-Val (Need $main 'Clipboard snippet title' $CT::Edit) 'Synthetic snippet'
  Set-Val (Need $main 'Clipboard snippet text' $CT::Edit) $token
  Invoke-El (Need $main 'Copy now' $CT::Button); Start-Sleep -Milliseconds 600
  $clip = try { Get-Clipboard -Raw } catch { "<$($_.Exception.Message)>" }
  Rec 'Clipboard: Copy now puts the snippet text on the clipboard' ($clip -eq $token) "clipboard=$clip"

  # Business data saved to workspace.json in the isolated folder.
  $saved = $false; for ($i = 0; $i -lt 30 -and -not $saved; $i++) { Start-Sleep -Milliseconds 300; $saved = (Test-Path (Join-Path $root 'workspace.json')) -and ((Get-Content (Join-Path $root 'workspace.json') -Raw) -match [regex]::Escape("Synthetic capture $token")) }
  $wj = if (Test-Path (Join-Path $root 'workspace.json')) { Get-Content (Join-Path $root 'workspace.json') -Raw } else { '' }
  Rec 'storage: capture, portal and snippet saved to workspace.json' ($saved -and $wj -match [regex]::Escape("Synthetic portal $token") -and $wj -match [regex]::Escape($token)) "file=$((Get-Item (Join-Path $root 'workspace.json') -ErrorAction SilentlyContinue).Length) bytes"

  # Launchpad shows the saved portal as a destination card.
  Nav $main 'Launchpad'
  Invoke-El (Need $main 'Refresh saved links' $CT::Button)
  $card = $null; for ($i = 0; $i -lt 20 -and -not $card; $i++) { $card = @($main.FindAll($TS::Descendants, (Cond $A::ControlTypeProperty $CT::Text)) | Where-Object { $_.Current.Name -like "*Synthetic portal $token*" })[0]; if (-not $card) { Start-Sleep -Milliseconds 200 } }
  Rec 'Launchpad: saved portal listed as a destination' ($null -ne $card)

  # Quick actions ribbon, File tray page and the Claude sessions page (merged from main).
  $ribbon = Find $main 'Quick actions'
  Rec 'Quick actions: ribbon present' ($null -ne $ribbon)
  Nav $main 'File tray'
  $traySummary = Find $main 'File tray summary' $null 20
  Rec 'File tray: page opens from navigation' ($null -ne $traySummary -and -not $traySummary.Current.IsOffscreen)
  $sessionsNav = Find $main 'Claude sessions' $CT::Button 10
  Rec 'Claude sessions: module reachable from V2 navigation' ($null -ne $sessionsNav)
  if ($sessionsNav) { Invoke-El $sessionsNav; Start-Sleep -Milliseconds 1600; Rec 'Claude sessions: page shown' ((ActiveProfile).Tabs.ModuleId -contains 'sessions') }

  # Tabs: new tab on Capture, a second on Portal Launcher, close one, reopen it.
  $tabs0 = @((ActiveProfile).Tabs).Count
  Menu $p $main 'Tabs' 'New tab (Ctrl+T)'; Nav $main 'Capture'
  Menu $p $main 'Tabs' 'New tab (Ctrl+T)'; Nav $main 'Portal Launcher'
  Start-Sleep -Milliseconds 1200
  $tabs1 = @((ActiveProfile).Tabs).Count
  Invoke-El (Need $main 'Close (Ctrl+W)' $CT::Button); Start-Sleep -Milliseconds 1200
  $tabs2 = @((ActiveProfile).Tabs).Count
  Menu $p $main 'Tabs' 'Reopen closed tab (Ctrl+Shift+T)'; Start-Sleep -Milliseconds 1200
  $prof = ActiveProfile; $tabs3 = @($prof.Tabs).Count
  Rec 'Tabs: new / close / reopen saved' ($tabs1 -eq $tabs0 + 2 -and $tabs2 -eq $tabs1 - 1 -and $tabs3 -eq $tabs1) "$tabs0 -> $tabs1 -> $tabs2 -> $tabs3"
  $modules = @($prof.Tabs | ForEach-Object ModuleId) -join ','
  Rec 'Tabs: modules remembered per tab' ($modules -match 'capture' -and $modules -match 'portals') $modules

  # Saved view (bookmark) through its dialog.
  Invoke-El (Need $main 'Save view' $CT::Button)
  $dlg = $null; for ($i = 0; $i -lt 30 -and -not $dlg; $i++) { Start-Sleep -Milliseconds 200; $h = [RA]::FindWindow([NullString]::Value, 'Save tabs, filters and layout (not a data snapshot)'); if ($h -ne [IntPtr]::Zero) { $dlg = $A::FromHandle($h) } }
  Rec 'Saved view: dialog opens' ($null -ne $dlg)
  if ($dlg) {
    $edit = $dlg.FindFirst($TS::Descendants, (Cond $A::ControlTypeProperty $CT::Edit)); Set-Val $edit 'Release view'
    $ok = $dlg.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((Cond $A::ControlTypeProperty $CT::Button), (New-Object System.Windows.Automation.OrCondition((Cond $A::NameProperty 'OK'), (Cond $A::NameProperty 'Save'))))))
    Invoke-El $ok; Start-Sleep -Milliseconds 1200
    $fgAfter = [RA]::GetForegroundWindow()
    Rec 'focus: closing a dialog returns the foreground to the main window' ($fgAfter -eq $p.MainWindowHandle) $(if ($fgAfter -ne $p.MainWindowHandle) { "foreground pid=$([RA]::Pid($fgAfter))" })
  }
  $bm = @((Shell).Bookmarks | Where-Object Name -eq 'Release view')
  Rec 'Saved view: bookmark stored' ($bm.Count -eq 1)

  # Switch workspace to the Knowledge preset.
  $combo = $main.FindFirst($TS::Descendants, (Cond $A::ControlTypeProperty $CT::ComboBox))
  $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
  $item = Find $combo 'Knowledge' $CT::ListItem 10
  $names = @($combo.FindAll($TS::Descendants, (Cond $A::ControlTypeProperty $CT::ListItem)) | ForEach-Object { $_.Current.Name })
  Rec 'Workspaces: selector items are announced by name' ($null -ne $item) ($names -join ', ')
  # Preset order: General, Datapass, Foil, Knowledge, Device & settings.
  if (-not $item) { $item = @($combo.FindAll($TS::Descendants, (Cond $A::ControlTypeProperty $CT::ListItem)))[3] }
  $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300
  $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse(); Start-Sleep -Milliseconds 1500
  Rec 'Workspaces: switching to Knowledge saved' ((ActiveProfile).Name -eq 'Knowledge') (ActiveProfile).Name

  # Focus: Ctrl+K focuses module search (synthesized only while this window owns the foreground).
  [void][RA]::SetForegroundWindow($p.MainWindowHandle); Start-Sleep -Milliseconds 500
  if ([RA]::GetForegroundWindow() -eq $p.MainWindowHandle) {
    Nav $main 'Capture'; [RA]::CtrlK(); Start-Sleep -Milliseconds 500
    $f = $A::FocusedElement.Current
    Rec 'focus: Ctrl+K moves keyboard focus to module search' ($f.Name -eq 'Search current module' -and $f.ProcessId -eq $p.Id) "$($f.Name)"
  } else { Rec 'focus: Ctrl+K moves keyboard focus to module search' 'BLOCKED' 'Windows refused the foreground; key not sent' }

  # DPI: process awareness, then the window on every monitor reports that monitor's DPI and keeps its layout inside.
  $aw = [RA]::Awareness($p.MainWindowHandle)
  Rec 'DPI: window is per-monitor aware (V2)' ($aw -eq 'PerMonitorV2') $aw
  $mons = [RA]::Monitors(); $orig = [RA]::Rect($p.MainWindowHandle); $dpiRows = @()
  foreach ($m in $mons) {
    [RA]::Move($p.MainWindowHandle, $m[0] + 40, $m[1] + 40); Start-Sleep -Milliseconds 900
    $wd = [RA]::WindowDpi($p.MainWindowHandle); $rc = [RA]::Rect($p.MainWindowHandle)
    $nav = (Need $main 'Launchpad' $CT::Button).Current.BoundingRectangle; $wb = $main.Current.BoundingRectangle
    $inside = $nav.Left -ge $wb.Left -and $nav.Right -le $wb.Right -and $nav.Top -ge $wb.Top -and $nav.Bottom -le $wb.Bottom -and $nav.Width -gt 0
    $dpiRows += [ordered]@{ monitorDpi = $m[4]; windowDpi = $wd; primary = [bool]$m[5]; widthPx = $rc[2] - $rc[0]; navInside = $inside }
  }
  [RA]::Move($p.MainWindowHandle, $orig[0], $orig[1]); Start-Sleep -Milliseconds 600
  $dpiOk = @($dpiRows | Where-Object { $_.windowDpi -ne $_.monitorDpi -or -not $_.navInside }).Count -eq 0
  Rec 'DPI: window DPI follows each monitor, layout stays inside' $dpiOk (($dpiRows | ForEach-Object { "mon $($_.monitorDpi)dpi -> win $($_.windowDpi)dpi, $($_.widthPx)px, nav inside=$($_.navInside)" }) -join '; ')
  Rec 'theme: light chrome is light' ((ChromeLuma $p $main) -gt 160) "luma=$(ChromeLuma $p $main)"

  Rec 'exit: run 1 closed cleanly' (Stop-App $p)

  # ---- Run 2: reopen the same data dir (dark theme override): everything restored -------------------------------
  $p = Start-App ($synthetic + @{ JUTILITY_THEME = 'dark' })
  $main = $A::FromHandle($p.MainWindowHandle)
  $sel = @($main.FindFirst($TS::Descendants, (Cond $A::ControlTypeProperty $CT::ComboBox)).GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection() | ForEach-Object { $_.Current.Name })
  Rec 'reopen: Knowledge workspace restored' ($sel -contains 'Knowledge') ($sel -join ', ')
  $combo = $main.FindFirst($TS::Descendants, (Cond $A::ControlTypeProperty $CT::ComboBox))
  $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
  (Need $combo 'General' $CT::ListItem).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300
  $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse(); Start-Sleep -Milliseconds 1200
  $prof = ActiveProfile
  Rec 'reopen: General tabs restored (count and modules)' (@($prof.Tabs).Count -eq $tabs3 -and $null -ne (Find $main 'Capture' $CT::Button 10)) "tabs=$(@($prof.Tabs).Count) modules=$((@($prof.Tabs | ForEach-Object ModuleId)) -join ',')"
  Nav $main 'Capture'
  Rec 'reopen: capture still there' ($null -ne (Find $main "Synthetic capture $token" $null 20))
  Rec 'theme: dark chrome is dark' ((ChromeLuma $p $main) -lt 100) "luma=$(ChromeLuma $p $main)"
  Rec 'exit: run 2 closed cleanly' (Stop-App $p)

  # ---- Run 3: real (read-only) Claude home, window minimized: background session scanning must stop -------------
  $p = Start-App @{}
  $main = $A::FromHandle($p.MainWindowHandle)
  $main.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Minimized)
  Start-Sleep -Seconds 5
  $p.Refresh(); $cpu0 = $p.TotalProcessorTime; Start-Sleep -Seconds 20; $p.Refresh()
  $cpu = ($p.TotalProcessorTime - $cpu0).TotalMilliseconds
  Rec 'idle: minimized with the real Claude home costs < 100 ms CPU in 20 s' ($cpu -lt 100) "$([Math]::Round($cpu)) ms"
  $main.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
  Start-Sleep -Seconds 3
  Rec 'idle: restored window rescans sessions (badge/list live again)' (-not $p.HasExited)
  Rec 'exit: run 3 closed cleanly' (Stop-App $p)
}
catch { Rec 'run aborted' $false ($_.Exception.Message + ' @ ' + $_.InvocationInfo.ScriptLineNumber) }
finally {
  if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  if ($null -ne $clipBefore) { try { Set-Clipboard -Value $clipBefore } catch {} }
}
Rec 'isolation: default data folder untouched' ((DirHash $defaultDir) -eq $defaultBefore)
$r.GetEnumerator() | ForEach-Object { '{0,-66} {1}{2}' -f $_.Key, $_.Value.result, $(if ($_.Value.detail) { "  ($($_.Value.detail))" } else { '' }) }
"data root: $root"
if ($JsonOut) { [ordered]@{ exe = $Exe; exeSha256 = (Get-FileHash $Exe).Hash; checks = $r } | ConvertTo-Json -Depth 5 | Set-Content $JsonOut -Encoding utf8 }
if (@($r.Values | Where-Object { $_.result -eq 'FAIL' }).Count -gt 0) { 'Release acceptance native checks: FAIL'; exit 1 }
'Release acceptance native checks: PASS'
