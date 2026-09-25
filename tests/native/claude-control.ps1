# Native Windows check for the optional Claude Control link: status-bar health dot, live Launchpad card from
# GET /api/status with fallback to the Effort Board files, the "Claude Control" embedded tab and the start action.
# Uses a fresh isolated --data-dir, a throwaway localhost stand-in for the server (so it can be switched off) and a
# throwaway start script that only writes a marker file. Input is UI Automation Invoke only: no clicks or typing.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]
$exe = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\JUtility.App\bin\Release\net8.0-windows10.0.19041.0\JUtilityPalette.exe'
$root = Join-Path $env:TEMP ("powerops-control-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $root | Out-Null
$r = [ordered]@{}
function Rec($name, $ok, $detail = '') { $r[$name] = $(if ($ok) { 'PASS' } else { 'FAIL' }) + $(if ($detail) { "  ($detail)" } else { '' }) }

$port = Get-Random -Minimum 41000 -Maximum 48000
$requests = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
$status = '{"generated":"2026-09-25T18:36","project":null,"plan":{"five_hour":44,"week":33,"sampled":"2026-09-25T18:10"},' +
  '"sessions":{"open":12,"running":3,"needs_you":1},"todo":8,"open_prs":2,"checks_to_act":2,' +
  '"urgent":[{"time":"2026-09-25T16:12","level":"urgent","source":"session","project":"StandIn","text":"Stand-in item waiting on you","link":"https://example.invalid/x"}],' +
  '"latest_audit":{"name":"2026-09-25-standin","status":"ok"},"links":{}}'
function Start-Server {
  $l = [System.Net.HttpListener]::new(); $l.Prefixes.Add("http://localhost:$port/"); $l.Start()
  $j = Start-ThreadJob -ArgumentList $l, $requests, $status -ScriptBlock {
    param($l, $q, $status)
    while ($l.IsListening) {
      try { $ctx = $l.GetContext() } catch { break }
      $path = $ctx.Request.Url.AbsolutePath
      $q.Enqueue($ctx.Request.HttpMethod + ' ' + $path + ' | ' + $ctx.Request.UserAgent)
      $body, $type = switch ($path) {
        '/api/health' { '{"ok": true, "time": "now"}', 'application/json' }
        '/api/status' { $status, 'application/json' }
        default { '<!doctype html><title>Control stand-in</title><h1>Claude Control stand-in</h1>', 'text/html' }
      }
      $b = [Text.Encoding]::UTF8.GetBytes($body)
      $ctx.Response.ContentType = $type; $ctx.Response.OutputStream.Write($b, 0, $b.Length); $ctx.Response.Close()
    }
  }
  return @{ Listener = $l; Job = $j }
}
function Stop-Server($s) { $s.Listener.Stop(); $s.Listener.Close(); Stop-Job $s.Job -ErrorAction SilentlyContinue; Remove-Job $s.Job -Force -ErrorAction SilentlyContinue }

$marker = Join-Path $root 'started.txt'
Set-Content (Join-Path $root 'start-control.cmd') "@echo off`r`necho started> `"%~dp0started.txt`"`r`n" -Encoding ascii
$homeId = [guid]::NewGuid()
$settings = [ordered]@{
  Format = 'powerops-quick-actions'; SchemaVersion = 1; Mode = 'Off'; GlobalShortcutsEnabled = $false; GlobalShortcuts = @()
  Ring = @('capture.region'); Shelf = @('app.open', ('web:' + $homeId.ToString('N'))); ShelfOrientation = 'Horizontal'; ShelfAlwaysOnTop = $true; ShelfAutoHide = $false; WorkspaceOverrides = @()
  WebApps = @(@{ Id = $homeId; Name = 'Claude Home'; Url = "http://localhost:$port/never-opened"; OpenMode = 'AppWindow'; Browser = 'Auto' })
  ClaudeControl = @{ Url = "http://localhost:$port/home.html"; StartCommand = (Join-Path $root 'start-control.cmd') }
}
Set-Content (Join-Path $root 'quick-actions.json') ($settings | ConvertTo-Json -Depth 5) -Encoding utf8

function Main($p) { $A::FromHandle($p.MainWindowHandle) }
function Named($p, [string]$name) { (Main $p).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name))) }
function ById($p, [string]$id) { (Main $p).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id))) }
function Invoke-El($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function AllTexts($p) { try { @((Main $p).FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.ControlType -in [System.Windows.Automation.ControlType]::Text, [System.Windows.Automation.ControlType]::Hyperlink } | ForEach-Object { $_.Current.Name }) } catch { @() } }
function Wait-Text($p, [scriptblock]$match, [int]$seconds = 15) { for ($i = 0; $i -lt $seconds * 4; $i++) { $hit = AllTexts $p | Where-Object $match | Select-Object -First 1; if ($hit) { return $hit }; Start-Sleep -Milliseconds 250 }; return $null }
function Dot($p) { $el = ById $p 'ClaudeControlHealth'; if ($el) { $el.Current.Name } else { '' } }
function Wait-Dot($p, [string]$state, [int]$seconds) { for ($i = 0; $i -lt $seconds * 4; $i++) { if ((Dot $p) -eq "Claude Control $state") { return $true }; Start-Sleep -Milliseconds 250 }; return $false }
function MenuItems($p, [string]$menu) {
  $m = (Main $p).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $menu)),
    (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)))))
  $ec = $m.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern); $ec.Expand(); Start-Sleep -Milliseconds 400
  # WPF puts the open submenu in a popup, so read every menu item of this process from the desktop root.
  $names = @($A::RootElement.FindAll($TS::Descendants, (New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)),
    (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem))))) | ForEach-Object { $_.Current.Name })
  $ec.Collapse(); return $names
}
function Invoke-Menu($p, [string]$menu, [string]$item) {
  $m = (Main $p).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $menu)),
    (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)))))
  $m.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
  Invoke-El ($A::RootElement.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $item))))
}

$server = Start-Server
$p = Start-Process $exe -ArgumentList '--data-dir', "`"$root`"" -PassThru
for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
Start-Sleep -Seconds 3
try {
  # 1. Health dot and live Launchpad card.
  Rec 'dot: shows online from GET /api/health' (Wait-Dot $p 'online' 10) (Dot $p)
  $live = Wait-Text $p { $_ -like 'Live from Claude Control*' }
  Rec 'card: live data from GET /api/status' ([bool]$live) $live
  $sessions = Wait-Text $p { $_ -like 'Sessions: 12 open*' } 3
  Rec 'card: sessions line' ($sessions -eq 'Sessions: 12 open · 3 running · 1 need you') $sessions
  Rec 'card: counts line' ([bool](Wait-Text $p { $_ -eq 'To-do 8 · open PRs 2 · checks to act 2' } 3))
  Rec 'card: urgent item listed' ([bool](Wait-Text $p { $_ -eq 'Stand-in item waiting on you' } 3))
  Rec 'card: audit line' ([bool](Wait-Text $p { $_ -eq 'Latest audit: 2026-09-25-standin ok' } 3))
  Rec 'card: plan bars' ([bool](Named $p '5-hour limit 44% used') -and [bool](Named $p 'Week 33% used'))
  Rec 'card: Open Claude Home / Open Claude Control buttons' ([bool](Named $p 'Open Claude Home') -and [bool](Named $p 'Open Claude Control'))

  # 2. Actions menu entries.
  $items = MenuItems $p 'Actions'
  Rec 'menu: Claude Control tab, Start and settings entries' ($items -contains 'Claude Control' -and $items -contains 'Start Claude Control' -and $items -contains 'Claude Control...') ((@($items | Where-Object { $_ -like '*Claude*' } | Select-Object -Unique)) -join ', ')

  # 3. Clicking the online dot opens the embedded Claude Control tab.
  Invoke-El (ById $p 'ClaudeControlHealth')
  $tab = $false; for ($i = 0; $i -lt 60 -and -not $tab; $i++) { Start-Sleep -Milliseconds 250; $tab = @($requests | Where-Object { $_ -like 'GET /home.html | *Edg/*' }).Count -gt 0 }
  Rec 'tab: home.html loaded inside Power Ops (WebView2)' $tab
  Start-Sleep -Seconds 2
  Rec 'tab: saved in the workspace view' ((Get-Content (Join-Path $root 'shell-workspaces.json') -Raw) -match 'c1a0de00c0de4c0e9000000000007430')

  # 4. Server off: dot turns offline within one poll, card falls back to the files, Start runs the command.
  Stop-Server $server
  Rec 'dot: turns offline within one 30 s poll' (Wait-Dot $p 'offline' 40) (Dot $p)
  Invoke-El (ById $p 'ClaudeControlHealth')   # offline dot = start action
  $started = $false; for ($i = 0; $i -lt 40 -and -not $started; $i++) { Start-Sleep -Milliseconds 250; $started = Test-Path $marker }
  Rec 'start: offline dot runs the configured start command' $started
  $server = Start-Server
  Rec 'dot: back online from the post-start re-checks (< 30 s poll)' (Wait-Dot $p 'online' 14) (Dot $p)

  Stop-Server $server; $server = $null
  # Closing the Claude Control tab returns to the Launchpad tab, which re-renders the card.
  Invoke-Menu $p 'Tabs' 'Close tab (Ctrl+W)'
  $fallback = Wait-Text $p { $_ -like 'Claude Control *Showing the Effort Board files instead.' } 10
  $usageFile = Join-Path $env:USERPROFILE '.claude\effort-board\usage.json'
  if (Test-Path $usageFile) { Rec 'card: fallback rows read from usage.json' ([bool](Wait-Text $p { $_ -like '*limits as of*file written*' } 3)) }
  Rec 'card: server off -> falls back to the files' ([bool]$fallback) $(if ($fallback) { $fallback } else { (AllTexts $p | Where-Object { $_ -match 'Claude|Asking|running|usage' }) -join ' | ' })

  # 5. Power Ops only ever read from the server.
  $methods = @($requests | ForEach-Object { ($_ -split ' ')[0] } | Sort-Object -Unique)
  Rec 'contract: only GET requests reached the server' (($methods -join ',') -eq 'GET') ($methods -join ',')
  Rec 'contract: API paths used' ((@($requests | Where-Object { $_ -like 'GET /api/health*' }).Count -ge 2) -and (@($requests | Where-Object { $_ -like 'GET /api/status*' }).Count -ge 1))
  [void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000)
  Rec 'exit: clean' $p.HasExited
}
catch { Rec 'run aborted' $false $_.Exception.Message }
finally {
  if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  if ($server) { Stop-Server $server }
}
$r.GetEnumerator() | ForEach-Object { '{0,-62} {1}' -f $_.Key, $_.Value }
"data root: $root"
if (@($r.Values | Where-Object { $_ -like 'FAIL*' }).Count -gt 0) { 'Claude Control native checks: FAIL'; exit 1 }
'Claude Control native checks: PASS'
