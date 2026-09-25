# Native Windows check for V2.1 Interaction settings + MX Master guide, using a fresh isolated --data-dir.
# Drives the dialog only through UI Automation patterns (Invoke/Select/ExpandCollapse): no synthesized clicks or
# typing reach other applications. The only synthesized input is global shortcuts, which Windows consumes.
# Does not press any "Copy" button (would overwrite the user's clipboard). Never points at the personal workspace.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class I {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool UnregisterHotKey(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static void Key(byte[] mods, byte key) {
    foreach (var m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
    keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero);
    for (int i = mods.Length - 1; i >= 0; i--) keybd_event(mods[i], 0, 2, UIntPtr.Zero);
  }
}
'@
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
$exe = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\JUtility.App\bin\Release\net8.0-windows10.0.19041.0\JUtilityPalette.exe'
$root = Join-Path $env:TEMP ("powerops-interaction-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $root | Out-Null
$r = [ordered]@{}
function Rec($name, $ok, $detail = '') { $r[$name] = $(if ($ok) { 'PASS' } else { 'FAIL' }) + $(if ($detail) { "  ($detail)" } else { '' }) }
function Probe([string]$gesture) {
  $mods = 0x4000; $key = $gesture.Split('+')[-1]
  foreach ($m in $gesture.Split('+')[0..($gesture.Split('+').Count - 2)]) { $mods = $mods -bor @{ Ctrl = 2; Alt = 1; Shift = 4 }[$m] }
  $vk = if ($key -eq 'Space') { 0x20 } elseif ($key -match '^F(\d+)$') { 0x6F + [int]$Matches[1] } else { [int][char]$key }
  $ok = [I]::RegisterHotKey([IntPtr]::Zero, 0x777B, $mods, $vk)
  if ($ok) { [void][I]::UnregisterHotKey([IntPtr]::Zero, 0x777B) }
  return $ok
}
function ByName($parent, [string]$name, $type = $null) {
  $c = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
  if ($type) { $c = New-Object System.Windows.Automation.AndCondition($c, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $type))) }
  for ($i = 0; $i -lt 40; $i++) { $e = $parent.FindFirst($T::Descendants, $c); if ($e) { return $e }; Start-Sleep -Milliseconds 150 }
  throw "UI element not found: $name"
}
function Invoke($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Wait-Window([string]$title) { for ($i = 0; $i -lt 40; $i++) { $h = [I]::FindWindow([NullString]::Value, $title); if ($h -ne [IntPtr]::Zero -and [I]::IsWindowVisible($h)) { return $A::FromHandle($h) }; Start-Sleep -Milliseconds 200 }; return $null }

# Expected outcome depends on what this machine already has registered (observed, not assumed).
$ctrlAltSpaceFree = Probe 'Ctrl+Alt+Space'
$expected = [ordered]@{}
$candidates = [ordered]@{ 'ring.show' = @('Ctrl+Alt+Shift+R', 'Ctrl+Alt+Shift+Q', 'Ctrl+Alt+Shift+F8'); 'app.toggle' = @('Ctrl+Alt+Shift+P', 'Ctrl+Alt+Shift+Space', 'Ctrl+Alt+Shift+F9')
  'capture.quick' = @('Ctrl+Alt+Shift+N', 'Ctrl+Alt+Shift+F10'); 'clipboard.open' = @('Ctrl+Alt+Shift+V', 'Ctrl+Alt+Shift+F11') }
foreach ($id in $candidates.Keys) {
  if ($id -eq 'app.toggle' -and $ctrlAltSpaceFree) { $expected[$id] = 'Ctrl+Alt+Space'; continue }
  $expected[$id] = $candidates[$id] | Where-Object { Probe $_ } | Select-Object -First 1
}
"machine: Ctrl+Alt+Space free=$ctrlAltSpaceFree; expected bindings: " + (($expected.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', ')

$p = Start-Process $exe -ArgumentList '--data-dir', "`"$root`"" -PassThru
try {
  for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
  Start-Sleep -Seconds 3
  $main = $A::FromHandle($p.MainWindowHandle)
  $actions = ByName $main 'Actions' ([System.Windows.Automation.ControlType]::MenuItem)
  $actions.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
  Invoke (ByName $A::RootElement 'Interaction settings...' ([System.Windows.Automation.ControlType]::MenuItem))
  $dialog = Wait-Window 'Interaction settings'
  Rec 'dialog: opens from Actions menu' ($null -ne $dialog)

  $radios = @('Off', 'Quick Shelf', 'Quick Ring', 'MX Master guide', 'Hybrid (recommended)') | ForEach-Object { ByName $dialog $_ ([System.Windows.Automation.ControlType]::RadioButton) }
  $selected = $radios | Where-Object { $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected }
  Rec 'modes: five choices, Off selected by default' ($radios.Count -eq 5 -and $selected.Current.Name -eq 'Off') $selected.Current.Name
  $radios[4].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 400
  $expander = ByName $dialog 'MX Master / Logi Options+ guide'
  $state = $expander.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Current.ExpandCollapseState
  Rec 'Hybrid: MX Master guide expanded' ($state -eq 'Expanded') $state
  Invoke (ByName $dialog 'Add recommended shortcuts' ([System.Windows.Automation.ControlType]::Button)); Start-Sleep -Milliseconds 800
  $texts = @($dialog.FindAll($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name })
  $report = $texts | Where-Object { $_ -like '*Saved when you press Save*' }
  Rec 'recommend: report shown before saving' ($null -ne $report) (($report -split "`n") -join ' | ')
  $ringCopy = "Copy $($expected['ring.show'])"
  Rec 'guide: Quick Ring step offers the chosen shortcut' ($dialog.FindFirst($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $ringCopy))) -ne $null) $ringCopy
  Rec 'guide: Back/Forward in-app steps' (($texts -match 'Back → previous Power Ops tab').Count -eq 1 -and ($texts -match 'Forward → next Power Ops tab').Count -eq 1)
  Rec 'nothing registered before Save' (Probe $expected['ring.show'])

  Invoke (ByName $dialog 'Save' ([System.Windows.Automation.ControlType]::Button)); Start-Sleep -Milliseconds 1200
  $warningBox = [I]::FindWindow('#32770', 'Power Ops global shortcuts')
  Rec 'save: no registration warning' ($warningBox -eq [IntPtr]::Zero)
  if ($warningBox -ne [IntPtr]::Zero) { Invoke (ByName $A::FromHandle($warningBox) 'OK') }

  $json = Get-Content (Join-Path $root 'quick-actions.json') -Raw | ConvertFrom-Json
  $bound = @{}; foreach ($b in $json.GlobalShortcuts) { $bound[$b.ActionId] = $b.Gesture }
  Rec 'saved: Hybrid mode, shortcuts enabled' ($json.Mode -eq 'Hybrid' -and $json.GlobalShortcutsEnabled)
  $mismatch = @($expected.Keys | Where-Object { $bound[$_] -ne $expected[$_] })
  Rec 'saved: bindings are the first free suggestions' ($mismatch.Count -eq 0) (($bound.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', ')
  Rec 'saved: unusable Ctrl+Alt+Space not kept' ($ctrlAltSpaceFree -or ($json.GlobalShortcuts.Gesture -notcontains 'Ctrl+Alt+Space'))
  $notOwned = @($expected.Values | Where-Object { Probe $_ })
  Rec 'registered: all four owned by Power Ops' ($notOwned.Count -eq 0) (($notOwned) -join ', ')

  # The ring shortcut now works (as Logi Options+ would send it): press twice = show then hide.
  $parts = $expected['ring.show'].Split('+'); $keyName = $parts[-1]
  $vk = if ($keyName -match '^F(\d+)$') { [byte](0x6F + [int]$Matches[1]) } else { [byte][char]$keyName }
  [I]::Key(@(0x11, 0x12, 0x10), $vk); Start-Sleep -Milliseconds 700
  $ring = [I]::FindWindow([NullString]::Value, 'Power Ops Quick Ring')
  Rec 'ring shortcut: shows the ring' ($ring -ne [IntPtr]::Zero -and [I]::IsWindowVisible($ring))
  [I]::Key(@(0x11, 0x12, 0x10), $vk); Start-Sleep -Milliseconds 500
  Rec 'ring shortcut again: hides it' (-not [I]::IsWindowVisible($ring))

  [void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000)
  Rec 'exit: clean' $p.HasExited
  Rec 'exit: shortcuts released' (@($expected.Values | Where-Object { -not (Probe $_) }).Count -eq 0)
}
catch { Rec 'run aborted' $false $_.Exception.Message }
finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
$r.GetEnumerator() | ForEach-Object { '{0,-50} {1}' -f $_.Key, $_.Value }
"data root: $root"
if (@($r.Values | Where-Object { $_ -like 'FAIL*' }).Count -gt 0) { 'Interaction native checks: FAIL'; exit 1 }
'Interaction native checks: PASS'
