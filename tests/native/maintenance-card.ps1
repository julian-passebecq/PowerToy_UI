# Native Windows check for the Mongoku MAINTENANCE report card against a running read-only Mongoku (default
# http://localhost:3100/), using a fresh isolated --data-dir. Only UI Automation Invoke is used (no synthesized
# clicks or typing). Links open inside Power Ops' embedded Mongoku tab, never the user's own browser.
param([string]$Mongoku = 'http://localhost:3100/')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]
$exe = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\JUtility.App\bin\Release\net8.0-windows10.0.19041.0\JUtilityPalette.exe'
$root = Join-Path $env:TEMP ("powerops-maintenance-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $root | Out-Null
$r = [ordered]@{}
function Rec($name, $ok, $detail = '') { $r[$name] = $(if ($ok) { 'PASS' } else { 'FAIL' }) + $(if ($detail) { "  ($detail)" } else { '' }) }

$base = $Mongoku.TrimEnd('/')
try { $health = Invoke-RestMethod "$base/api/health" -TimeoutSec 5 } catch { "BLOCKED: Mongoku is not reachable at $base ($($_.Exception.Message))."; exit 2 }
if ($health.writesEnabled -ne $false) { "BLOCKED: Mongoku at $base does not report writesEnabled=false; refusing to test against a writable instance."; exit 2 }
$truth = Invoke-RestMethod "$base/api/datapass/reports/MAINTENANCE" -TimeoutSec 30
$sum = ($truth.sections | Where-Object id -eq 'summary').rows[0]

# One ordinary card and one card on a stopped Mongoku; the Maintenance card is added through the "+ Maintenance card" button.
@{ Format = 'powerops-report-cards'; SchemaVersion = 1; Cards = @(
    @{ Id = [guid]::NewGuid(); Title = 'Portfolio'; SourceUrl = "$base/"; ReportId = 'GLOBAL_PROJECTS' },
    @{ Id = [guid]::NewGuid(); Title = 'Stopped maintenance'; SourceUrl = 'http://localhost:1/'; ReportId = 'MAINTENANCE' }) } |
  ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root 'report-cards.json') -Encoding utf8
@{ Format = 'powerops-quick-actions'; SchemaVersion = 1; Mode = 'Off'; GlobalShortcutsEnabled = $false; GlobalShortcuts = @()
   Ring = @('capture.region'); Shelf = @('app.open'); ShelfOrientation = 'Horizontal'; ShelfAlwaysOnTop = $true; ShelfAutoHide = $false; WorkspaceOverrides = @()
   WebApps = @(@{ Id = [guid]::NewGuid(); Name = 'Mongoku'; Url = "$base/"; OpenMode = 'Embedded'; Browser = 'Auto' }) } |
  ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root 'quick-actions.json') -Encoding utf8

function Main($p) { $A::FromHandle($p.MainWindowHandle) }
function Named($p, [string]$name) { (Main $p).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name))) }
function Invoke-Named($p, [string]$name) {
  $el = $null; for ($i = 0; $i -lt 20 -and -not $el; $i++) { try { $el = Named $p $name } catch { }; if (-not $el) { Start-Sleep -Milliseconds 250 } }
  if (-not $el) { throw "UI element not found: $name" }
  $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function CardTexts($p, [string]$card) {
  try {
    $c = Named $p "$card report card"; if (-not $c) { return @() }
    @($c.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.ControlType -in [System.Windows.Automation.ControlType]::Text, [System.Windows.Automation.ControlType]::Hyperlink } | ForEach-Object { $_.Current.Name })
  } catch { @() }
}
function Wait-CardText($p, [string]$card, [scriptblock]$match, [int]$seconds = 30) { for ($i = 0; $i -lt $seconds * 4; $i++) { $hit = CardTexts $p $card | Where-Object $match | Select-Object -First 1; if ($hit) { return $hit }; Start-Sleep -Milliseconds 250 }; return $null }
function AllTexts($p) { try { @((Main $p).FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name }) } catch { @() } }

$p = Start-Process $exe -ArgumentList '--data-dir', "`"$root`"" -PassThru
try {
  for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
  Start-Sleep -Seconds 4

  # A Maintenance card already exists (the stopped one), so the add button is hidden until it is gone; use a fresh file instead.
  Rec 'add button hidden when a MAINTENANCE card exists' (-not (Named $p '+ Maintenance card'))
  [void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000)
  $cards = Get-Content (Join-Path $root 'report-cards.json') -Raw | ConvertFrom-Json
  $cards.Cards = @($cards.Cards | Where-Object ReportId -ne 'MAINTENANCE')
  $cards | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root 'report-cards.json') -Encoding utf8
  $p = Start-Process $exe -ArgumentList '--data-dir', "`"$root`"" -PassThru
  for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
  Start-Sleep -Seconds 4

  Invoke-Named $p '+ Maintenance card'
  Start-Sleep -Milliseconds 800
  $saved = (Get-Content (Join-Path $root 'report-cards.json') -Raw | ConvertFrom-Json).Cards | Where-Object ReportId -eq 'MAINTENANCE'
  Rec 'add: "+ Maintenance card" saves one MAINTENANCE card' (@($saved).Count -eq 1 -and $saved.SourceUrl -like "$base*") "$($saved.SourceUrl)"
  Rec 'add: nothing fetched before Refresh' ([bool](Wait-CardText $p 'Maintenance' { $_ -eq 'Not loaded yet. Press Refresh.' } 5))
  Rec 'add: button disappears once added' (-not (Named $p '+ Maintenance card'))
  Rec 'no credential field on the card' (-not (CardTexts $p 'Maintenance' | Where-Object { $_ -match 'password|token|secret' }))

  Invoke-Named $p 'Refresh Maintenance'
  $line = Wait-CardText $p 'Maintenance' { $_ -eq $sum.summary } 40
  Rec 'summary line matches the API' ([bool]$line) $line
  $all = CardTexts $p 'Maintenance'
  Rec 'top next action shown' (@($all | Where-Object { $_ -eq "Next: $($sum.nextAction)" }).Count -eq 1) $sum.nextAction
  $counts = "Sources $($sum.sourcesReachable) · projects $($sum.projectsNeedingAction) · heads $($sum.headsToReconcile) · projections $($sum.projectionsNeedingAction) · audits $($sum.auditsToReview) · reconciliation $($sum.reconciliationFindings) · backups not recorded"
  Rec 'counts match the API' (@($all | Where-Object { $_ -eq $counts }).Count -eq 1) $counts
  $unresolved = @($truth.sections | Where-Object { $_.trace.resolved -eq $false })
  Rec 'unresolved sections match the API' (@($all | Where-Object { $_ -like '● *: unavailable*' }).Count -eq $unresolved.Count) "$($unresolved.Count) unresolved"
  $firstAction = @($truth.sections | Where-Object { $_.id -ne 'summary' -and $_.trace.resolved -ne $false } | ForEach-Object { $_.rows } | Where-Object { $_.actionKind -and $_.actionKind -ne 'none' })
  $firstLink = "▸ $($firstAction[0].title): $($firstAction[0].nextAction)"
  Rec 'rows to act on are links' (@($all | Where-Object { $_ -eq $firstLink }).Count -ge 1) $firstLink
  if ($firstAction.Count -gt 6) { Rec 'remaining rows summarised' (@($all | Where-Object { $_ -eq "+$($firstAction.Count - 6) more to act on in Mongoku" }).Count -ge 1) "$($firstAction.Count) rows" }
  Rec 'no row internals shown (repo, heads)' (-not ($all | Where-Object { $_ -match 'julian-passebecq/|[0-9a-f]{40}' }))

  # Mongoku down: that card only says unavailable; other cards still work.
  $cards = Get-Content (Join-Path $root 'report-cards.json') -Raw | ConvertFrom-Json
  Rec 'read-only: card config unchanged by Refresh' (@($cards.Cards).Count -eq 2)

  # Row link opens the project page inside the embedded Mongoku tab.
  if ($firstAction[0].openUri) {
    # The TextBlock and its Hyperlink share the name; only the Hyperlink supports Invoke.
    $el = (Main $p).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition(
      (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $firstLink.Replace('_', ' '))),
      (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Hyperlink)))))
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $want = "$base$($firstAction[0].openUri)"
    $addr = $null; for ($i = 0; $i -lt 160 -and -not $addr; $i++) { $addr = AllTexts $p | Where-Object { $_ -like "*$want*" } | Select-Object -First 1; if (-not $addr) { Start-Sleep -Milliseconds 250 } }
    Rec 'row link: opens its openUri in the Mongoku tab' ([bool]$addr) $want
  }

  [void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000)

  # Stopped Mongoku: card shows unavailable, the neighbouring card still refreshes. Fresh data folder, because the
  # previous run saved the Mongoku web tab as the active tab.
  $first = $root; $root = "$first-down"; New-Item -ItemType Directory $root | Out-Null
  Copy-Item (Join-Path $first 'quick-actions.json') $root
  @{ Format = 'powerops-report-cards'; SchemaVersion = 1; Cards = @(
      @{ Id = [guid]::NewGuid(); Title = 'Portfolio'; SourceUrl = "$base/"; ReportId = 'GLOBAL_PROJECTS' },
      @{ Id = [guid]::NewGuid(); Title = 'Stopped maintenance'; SourceUrl = 'http://localhost:1/'; ReportId = 'MAINTENANCE' }) } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root 'report-cards.json') -Encoding utf8
  $p = Start-Process $exe -ArgumentList '--data-dir', "`"$root`"" -PassThru
  for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
  Start-Sleep -Seconds 4
  Invoke-Named $p 'Refresh Stopped maintenance'
  $down = Wait-CardText $p 'Stopped maintenance' { $_ -like 'Maintenance unavailable: Mongoku is not reachable*' } 30
  Rec 'Mongoku down: card shows "Maintenance unavailable"' ([bool]$down) $down
  Invoke-Named $p 'Refresh Portfolio'
  Rec 'Mongoku down: other cards unaffected' ([bool](Wait-CardText $p 'Portfolio' { $_ -like '● *' } 40))
  Rec 'Mongoku down: Power Ops still responsive' ($p.Responding)
  [void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000)
  Rec 'exit: clean' $p.HasExited
  Rec 'Mongoku still read-only afterwards' ((Invoke-RestMethod "$base/api/health" -TimeoutSec 5).writesEnabled -eq $false)
}
catch { Rec 'run aborted' $false $_.Exception.Message }
finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
$r.GetEnumerator() | ForEach-Object { '{0,-52} {1}' -f $_.Key, $_.Value }
"data root: $root"
if (@($r.Values | Where-Object { $_ -like 'FAIL*' }).Count -gt 0) { 'Maintenance card native checks: FAIL'; exit 1 }
'Maintenance card native checks: PASS'
