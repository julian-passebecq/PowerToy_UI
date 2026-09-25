# Native Windows check for V2.1 Mongoku report cards against a running Mongoku (default http://localhost:3100/),
# using a fresh isolated --data-dir. Only UI Automation Invoke is used (no synthesized clicks or typing).
# Mongoku must report writesEnabled=false; the script refuses to run otherwise. Only read-only GET endpoints are used.
param([string]$Mongoku = 'http://localhost:3100/')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]
$exe = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\JUtility.App\bin\Release\net8.0-windows\JUtilityPalette.exe'
$root = Join-Path $env:TEMP ("powerops-reports-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $root | Out-Null
$r = [ordered]@{}
function Rec($name, $ok, $detail = '') { $r[$name] = $(if ($ok) { 'PASS' } else { 'FAIL' }) + $(if ($detail) { "  ($detail)" } else { '' }) }

$base = $Mongoku.TrimEnd('/')
try { $health = Invoke-RestMethod "$base/api/health" -TimeoutSec 5 } catch { "BLOCKED: Mongoku is not reachable at $base ($($_.Exception.Message))."; exit 2 }
if ($health.writesEnabled -ne $false) { "BLOCKED: Mongoku at $base does not report writesEnabled=false; refusing to test against a writable instance."; exit 2 }
# Ground truth straight from the API (read-only GET) to compare with what the cards show.
$truth = @{}; foreach ($id in 'FOIL_STATUS_NOW', 'GLOBAL_PROJECTS') { $truth[$id] = Invoke-RestMethod "$base/api/datapass/reports/$id" -TimeoutSec 20 }

$mongokuApp = [guid]::NewGuid()
@{ Format = 'powerops-report-cards'; SchemaVersion = 1; Cards = @(
    @{ Id = [guid]::NewGuid(); Title = ''; SourceUrl = "$base/"; ReportId = 'FOIL_STATUS_NOW' },
    @{ Id = [guid]::NewGuid(); Title = 'Portfolio'; SourceUrl = "$base/"; ReportId = 'GLOBAL_PROJECTS' },
    @{ Id = [guid]::NewGuid(); Title = 'Unknown report'; SourceUrl = "$base/"; ReportId = 'NOPE_UNKNOWN_REPORT' },
    @{ Id = [guid]::NewGuid(); Title = 'Stopped Mongoku'; SourceUrl = 'http://localhost:1/'; ReportId = 'FOIL_NEXT' },
    @{ Id = [guid]::NewGuid(); Title = 'Source inventory'; SourceUrl = "$base/"; ReportId = 'SOURCE_INVENTORY' }) } |
  ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root 'report-cards.json') -Encoding utf8
@{ Format = 'powerops-quick-actions'; SchemaVersion = 1; Mode = 'Off'; GlobalShortcutsEnabled = $false; GlobalShortcuts = @()
   Ring = @('capture.region'); Shelf = @('app.open'); ShelfOrientation = 'Horizontal'; ShelfAlwaysOnTop = $true; ShelfAutoHide = $false; WorkspaceOverrides = @()
   WebApps = @(@{ Id = $mongokuApp; Name = 'Mongoku'; Url = "$base/"; OpenMode = 'Embedded'; Browser = 'Auto' }) } |
  ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root 'quick-actions.json') -Encoding utf8
$cardsBefore = Get-Content (Join-Path $root 'report-cards.json') -Raw

function Texts($p) { @($A::FromHandle($p.MainWindowHandle).FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name }) }
function Invoke-Named($p, [string]$name) {
  $el = $A::FromHandle($p.MainWindowHandle).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)))
  if (-not $el) { throw "UI element not found: $name" }
  $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Wait-Text($p, [scriptblock]$match, [int]$seconds = 25) { for ($i = 0; $i -lt $seconds * 4; $i++) { $hit = Texts $p | Where-Object $match | Select-Object -First 1; if ($hit) { return $hit }; Start-Sleep -Milliseconds 250 }; return $null }
function MongokuConnections($p) { @(Get-NetTCPConnection -OwningProcess $p.Id -ErrorAction SilentlyContinue | Where-Object { $_.RemotePort -eq ([Uri]$Mongoku).Port }).Count }

$p = Start-Process $exe -ArgumentList '--data-dir', "`"$root`"" -PassThru
try {
  for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
  Start-Sleep -Seconds 4
  $initial = Texts $p
  Rec 'launchpad: report section shown' ($initial -contains 'Mongoku reports')
  Rec 'on demand: five cards, all "Not loaded yet"' (@($initial | Where-Object { $_ -eq 'Not loaded yet. Press Refresh.' }).Count -eq 5)
  Rec 'on demand: no connection to Mongoku before Refresh' ((MongokuConnections $p) -eq 0)

  # FOIL status now vs API ground truth
  Invoke-Named $p 'Refresh FOIL STATUS NOW'
  $foil = $truth['FOIL_STATUS_NOW']
  $firstLabel = $foil.sections[0].label
  $line = Wait-Text $p { $_ -like "● $firstLabel*" }
  $all = Texts $p
  $expectedLines = @($foil.sections | ForEach-Object { "● $($_.label): $($_.meta.returnedRows) row" })
  $missing = @($expectedLines | Where-Object { $e = $_; -not ($all | Where-Object { $_ -like "$e*" }) })
  Rec 'FOIL status now: every section with its row count' ($line -and $missing.Count -eq 0) $(if ($missing) { 'missing: ' + ($missing -join '; ') } else { "$($foil.sections.Count) sections" })
  $unbound = @($foil.sections | Where-Object { $_.meta.state -ne 'OK' -and $_.meta.state -ne 'TRUNCATED' }).Count
  $overall = $all | Where-Object { $_ -like '*unavailable*complete' -or $_ -like 'All * sections complete' } | Select-Object -First 1
  Rec 'FOIL status now: overall state matches Mongoku' ($(if ($unbound -gt 0) { $overall -like "$unbound unavailable*" } else { $overall -like 'All*' })) $overall
  Rec 'FOIL status now: generated/fetched/read-only line' (@($all | Where-Object { $_ -like 'Generated * by Mongoku · fetched * · read-only' }).Count -ge 1)

  # Global projects (includes a truncated section in the observed data)
  Invoke-Named $p 'Refresh Portfolio'
  $gp = $truth['GLOBAL_PROJECTS']
  $truncated = @($gp.sections | Where-Object { $_.meta.truncated }).Count
  $gpLine = Wait-Text $p { $_ -like "● $($gp.sections[0].label)*" }
  $all = Texts $p
  Rec 'Global projects: sections shown' ([bool]$gpLine) "$($gp.sections.Count) sections, $truncated truncated"
  if ($truncated -gt 0) { Rec 'Global projects: truncation stated, not hidden' (@($all | Where-Object { $_ -like '*more rows exist than the report limit*' }).Count -ge $truncated) }
  Rec 'row contents never displayed' (-not ($all | Where-Object { $_ -match '"_id"|ObjectId\(' }))

  # Failures are explained
  Invoke-Named $p 'Refresh Unknown report'
  Rec 'unknown report: Mongoku error shown' ([bool](Wait-Text $p { $_ -like '*Unknown report: NOPE_UNKNOWN_REPORT*' }))
  Invoke-Named $p 'Refresh Stopped Mongoku'
  Rec 'stopped Mongoku: "not reachable" shown' ([bool](Wait-Text $p { $_ -like 'Mongoku is not reachable at http://localhost:1*' }))

  # Source inventory (federation check): resolved count and states match the API
  $inv = Invoke-RestMethod "$base/api/datapass/reports/SOURCE_INVENTORY" -TimeoutSec 30
  $traced = @($inv.sections | Where-Object { $null -ne $_.trace.resolved })
  $expectedResolved = "Sources resolved: $(@($traced | Where-Object { $_.trace.resolved }).Count)/$($traced.Count)"
  Invoke-Named $p 'Refresh Source inventory'
  $card = $null; for ($i = 0; $i -lt 40 -and -not $card; $i++) { $card = $A::FromHandle($p.MainWindowHandle).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Source inventory report card'))); if (-not $card) { Start-Sleep -Milliseconds 250 } }
  function CardTexts { @($card.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name }) }
  $got = $null; for ($i = 0; $i -lt 160 -and -not $got; $i++) { $card = $A::FromHandle($p.MainWindowHandle).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Source inventory report card'))); if ($card) { $got = CardTexts | Where-Object { $_ -like 'Sources resolved:*' } | Select-Object -First 1 }; if (-not $got) { Start-Sleep -Milliseconds 250 } }
  Rec 'source inventory: resolved count matches the API' ($got -eq $expectedResolved) "$got (API: $expectedResolved)"
  $bad = @($inv.sections | Where-Object { $_.meta.state -notin 'OK', 'EMPTY' }).Count
  $all = CardTexts
  Rec 'source inventory: overall state matches the API' ($(if ($bad -eq 0) { @($all | Where-Object { $_ -like "All $(@($inv.sections).Count) sections complete" }).Count -ge 1 } else { @($all | Where-Object { $_ -like "$bad unavailable*" }).Count -ge 1 })) "$bad section(s) not OK/EMPTY"

  # Deep links: every card's "Open in Mongoku" target must exist in this Mongoku (checked with read-only GETs)
  $cards = (Get-Content (Join-Path $root 'report-cards.json') -Raw | ConvertFrom-Json).Cards | Where-Object { $_.SourceUrl -like "$base*" -and $_.ReportId -ne 'NOPE_UNKNOWN_REPORT' }
  $broken = @(foreach ($c in $cards) { $path = if ($c.ReportId -eq 'GLOBAL_PROJECTS') { '/projects' } else { "/foil/report/$($c.ReportId)" }; try { [void](Invoke-WebRequest "$base$path" -TimeoutSec 30 -UseBasicParsing -Headers @{ Accept = 'text/html' }) } catch { $path } })
  Rec 'deep links: every card target exists in Mongoku' ($broken.Count -eq 0) $(if ($broken) { 'broken: ' + ($broken -join ', ') } else { "$(@($cards).Count) targets" })

  # Open in Mongoku lands on the report page inside the embedded Mongoku tab
  Invoke-Named $p 'Open FOIL status now in Mongoku'
  $addr = Wait-Text $p { $_ -like "*$base/foil/report/FOIL_STATUS_NOW*" -or $_ -like '*localhost:3100/foil/report/FOIL_STATUS_NOW*' } 40
  Rec 'open: embedded Mongoku shows the report page' ([bool]$addr) $addr

  Rec 'nothing persisted: report-cards.json unchanged' ((Get-Content (Join-Path $root 'report-cards.json') -Raw) -eq $cardsBefore)
  Rec 'nothing persisted: no report data files' (@(Get-ChildItem $root -File | Where-Object { $_.Name -match 'report' -and $_.Name -ne 'report-cards.json' }).Count -eq 0)
  [void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000)
  Rec 'exit: clean' $p.HasExited
  $after = Invoke-RestMethod "$base/api/health" -TimeoutSec 5
  Rec 'Mongoku still read-only afterwards' ($after.writesEnabled -eq $false) "mode=$($after.mode)"
}
catch { Rec 'run aborted' $false $_.Exception.Message }
finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
$r.GetEnumerator() | ForEach-Object { '{0,-52} {1}' -f $_.Key, $_.Value }
"data root: $root"
if (@($r.Values | Where-Object { $_ -like 'FAIL*' }).Count -gt 0) { 'Report card native checks: FAIL'; exit 1 }
'Report card native checks: PASS'
