# Native Windows check for V2.1 protected-Mongoku sign-in (basic auth), using a fresh isolated --data-dir and a
# throwaway localhost server that answers exactly like Mongoku's MONGOKU_AUTH_BASIC (401 + WWW-Authenticate: Basic).
# Drives the UI only through UI Automation patterns (no synthesized clicks or typing).
# Writes ONE clearly named test entry (PowerOps/Mongoku/http://localhost:<random port>, dummy password) to the
# current user's Windows Credential Manager and always deletes it at the end.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace T -Name U -MemberDefinition '[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);'
# Same Credential Manager API as Power Ops (the cmdkey tool did not reliably list these generic entries).
Add-Type -Namespace T -Name Vault -MemberDefinition @"
[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct CRED { public uint Flags; public int Type; public string TargetName; public string Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten; public uint BlobSize; public IntPtr Blob; public int Persist; public uint AttrCount; public IntPtr Attrs; public string Alias; public string UserName; }
[DllImport("advapi32.dll", EntryPoint="CredReadW", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool CredRead(string t, int type, int f, out IntPtr c);
[DllImport("advapi32.dll", EntryPoint="CredWriteW", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool CredWrite(ref CRED c, int f);
[DllImport("advapi32.dll", EntryPoint="CredDeleteW", CharSet=CharSet.Unicode, SetLastError=true)] public static extern bool CredDelete(string t, int type, int f);
[DllImport("advapi32.dll")] static extern void CredFree(IntPtr p);
public static string User(string t) { IntPtr p; if (!CredRead(t, 1, 0, out p)) return null; try { return ((CRED)Marshal.PtrToStructure(p, typeof(CRED))).UserName; } finally { CredFree(p); } }
public static bool Write(string t, string user, string pass) { byte[] b = System.Text.Encoding.Unicode.GetBytes(pass); IntPtr m = Marshal.AllocHGlobal(b.Length); try { Marshal.Copy(b, 0, m, b.Length); var c = new CRED { Type = 1, TargetName = t, UserName = user, Blob = m, BlobSize = (uint)b.Length, Persist = 2 }; return CredWrite(ref c, 0); } finally { Marshal.FreeHGlobal(m); } }
"@
$A = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]
$exe = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\JUtility.App\bin\Release\net8.0-windows10.0.19041.0\JUtilityPalette.exe'
$root = Join-Path $env:TEMP ("powerops-auth-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory $root | Out-Null
$r = [ordered]@{}
function Rec($name, $ok, $detail = '') { $r[$name] = $(if ($ok) { 'PASS' } else { 'FAIL' }) + $(if ($detail) { "  ($detail)" } else { '' }) }

$port = Get-Random -Minimum 41000 -Maximum 48000
$base = "http://localhost:$port"
$target = "PowerOps/Mongoku/$base"
$user = 'powerops-test'
$password = 'pw-' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$expected = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("${user}:$password"))
$listener = [System.Net.HttpListener]::new(); $listener.Prefixes.Add("$base/"); $listener.Start()
$seen = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
$job = Start-ThreadJob -ArgumentList $listener, $seen, $expected -ScriptBlock {
  param($l, $q, $expected)
  $report = '{"reportId":"FOIL_AUTH_TEST","title":"Protected report","generatedAt":"2026-09-25T00:00:00Z","readOnly":true,"sections":[{"label":"Protected section","meta":{"state":"OK","returnedRows":7}}]}'
  $workspace = '{"reports":[{"id":"FOIL_AUTH_TEST","title":"Protected report"}]}'
  $page = '<!doctype html><title>Protected Mongoku stand-in</title><h1>signed in</h1>'
  while ($l.IsListening) {
    try { $ctx = $l.GetContext() } catch { break }
    $auth = $ctx.Request.Headers['Authorization']
    $q.Enqueue($ctx.Request.Url.AbsolutePath + ' | ' + $(if ($auth -eq $expected) { 'good' } elseif ($auth) { 'bad' } else { 'none' }))
    if ($auth -ne $expected) {
      $ctx.Response.StatusCode = 401; $ctx.Response.AddHeader('WWW-Authenticate', 'Basic')
      $b = [Text.Encoding]::UTF8.GetBytes('Unauthorized')
    } else {
      $path = $ctx.Request.Url.AbsolutePath
      $body = if ($path -eq '/api/datapass/reports/FOIL_AUTH_TEST') { $report } elseif ($path -eq '/api/datapass/workspace') { $workspace } else { $page }
      $ctx.Response.ContentType = $(if ($path -like '/api/*') { 'application/json' } else { 'text/html' })
      $b = [Text.Encoding]::UTF8.GetBytes($body)
    }
    $ctx.Response.OutputStream.Write($b, 0, $b.Length); $ctx.Response.Close()
  }
}
$appId = [guid]::NewGuid()
@{ Format = 'powerops-report-cards'; SchemaVersion = 1; Cards = @(@{ Id = [guid]::NewGuid(); Title = 'Protected report'; SourceUrl = "$base/"; ReportId = 'FOIL_AUTH_TEST' }) } |
  ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root 'report-cards.json') -Encoding utf8
@{ Format = 'powerops-quick-actions'; SchemaVersion = 1; Mode = 'Off'; GlobalShortcutsEnabled = $false; GlobalShortcuts = @()
   Ring = @('capture.region'); Shelf = @('app.open'); ShelfOrientation = 'Horizontal'; ShelfAlwaysOnTop = $true; ShelfAutoHide = $false; WorkspaceOverrides = @()
   WebApps = @(@{ Id = $appId; Name = 'Mongoku'; Url = "$base/"; OpenMode = 'Embedded'; Browser = 'Auto' }) } |
  ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root 'quick-actions.json') -Encoding utf8

function Win([string]$title) { for ($i = 0; $i -lt 40; $i++) { $h = [T.U]::FindWindow([NullString]::Value, $title); if ($h -ne [IntPtr]::Zero) { return $A::FromHandle($h) }; Start-Sleep -Milliseconds 200 }; throw "window not found: $title" }
function Find($parent, [string]$name) { for ($i = 0; $i -lt 40; $i++) { $e = $parent.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name))); if ($e) { return $e }; Start-Sleep -Milliseconds 150 }; throw "UI element not found: $name" }
function Invoke($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Edit($parent, [string]$name) {
  # Visible labels share the field's name; only the Edit control has a ValuePattern.
  $cond = New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)), (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
  for ($i = 0; $i -lt 40; $i++) { $e = $parent.FindFirst($TS::Descendants, $cond); if ($e) { return $e }; Start-Sleep -Milliseconds 150 }; throw "edit field not found: $name"
}
function SetValue($el, [string]$v) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($v) }
function Texts($el) { @($el.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name }) }
function Wait-Text($el, [scriptblock]$match) { for ($i = 0; $i -lt 80; $i++) { $hit = Texts $el | Where-Object $match | Select-Object -First 1; if ($hit) { return $hit }; Start-Sleep -Milliseconds 250 }; return $null }
function VaultUser { return [T.Vault]::User($target) }

$p = Start-Process $exe -ArgumentList '--data-dir', "`"$root`"" -PassThru
try {
  for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
  Start-Sleep -Seconds 4
  $main = $A::FromHandle($p.MainWindowHandle)
  Rec 'vault: no test entry before' ($null -eq (VaultUser))

  # 1. Without a saved sign-in: explained, and no Authorization header is ever sent.
  Invoke (Find $main 'Refresh Protected report')
  $msg = Wait-Text $main { $_ -like '*asks for a user name and password*' }
  Rec 'no sign-in: card explains what to do' ([bool]$msg) $msg
  Rec 'no sign-in: no Authorization header sent' (@($seen | Where-Object { $_ -notlike '* | none' }).Count -eq 0)

  # 2. Save the sign-in through the dialogs (UI Automation ValuePattern on TextBox and PasswordBox).
  Invoke (Find $main 'Manage report cards...')
  $dialog = Win 'Mongoku report cards'
  SetValue (Edit $dialog 'Mongoku address') "$base/"
  Invoke (Find $dialog 'Save user and password...')
  $signIn = Win 'Mongoku sign-in'
  SetValue (Edit $signIn 'User name') $user
  SetValue (Edit $signIn 'Password') $password
  Invoke (Find $signIn 'Save'); Start-Sleep -Milliseconds 600
  Rec 'save: stored in Windows Credential Manager for this origin' ((VaultUser) -eq $user) "target=$target"
  $status = Wait-Text $dialog { $_ -like "Sign-in for $base*saved for user*" }
  Rec 'save: dialog shows the saved user (never the password)' ([bool]$status -and $status -notlike "*$password*") $status
  Invoke (Find $dialog 'Save'); Start-Sleep -Milliseconds 600

  # 3. Refresh signs in.
  Invoke (Find $main 'Refresh Protected report')
  $line = Wait-Text $main { $_ -like '● Protected section: 7 rows*' }
  Rec 'signed in: report sections shown' ([bool]$line) $line
  Rec 'signed in: server saw the correct Authorization' (@($seen | Where-Object { $_ -like '/api/datapass/reports/FOIL_AUTH_TEST | good' }).Count -ge 1)

  # 4. The embedded Mongoku tab reuses the saved sign-in silently.
  $actions = Find $main 'Actions'
  $actions.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
  Invoke ($A::RootElement.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Mongoku')),
    (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem))))))
  $title = Wait-Text $main { $_ -eq 'Mongoku · Protected Mongoku stand-in' }
  Rec 'embedded: protected page opened without a prompt' ([bool]$title) $title
  Rec 'embedded: page request carried the saved sign-in' (@($seen | Where-Object { $_ -eq '/ | good' }).Count -ge 1)

  # 5. A wrong saved password is reported as rejected (back on the Launchpad, where the card lives).
  Invoke ($main.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Launchpad')), (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))))); Start-Sleep -Milliseconds 800
  [void][T.Vault]::Write($target, $user, 'definitely-wrong')
  Invoke (Find $main 'Refresh Protected report')
  $msg = Wait-Text $main { $_ -like '*rejected the saved user name or password*' }
  Rec 'wrong password: reported as rejected' ([bool]$msg) $msg

  # 6. Nothing secret in Power Ops files.
  $files = Get-ChildItem $root -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notlike '*\webview2\*' }
  $leaks = @($files | Where-Object { (Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue) -match [regex]::Escape($password) })
  Rec 'no password in Power Ops data files' ($leaks.Count -eq 0) "$($files.Count) files checked"

  # 7. Forget removes it from the vault.
  Invoke (Find $main 'Manage report cards...')
  $dialog = Win 'Mongoku report cards'
  SetValue (Edit $dialog 'Mongoku address') "$base/"
  Invoke (Find $dialog 'Forget sign-in'); Start-Sleep -Milliseconds 600
  Rec 'forget: removed from Windows Credential Manager' ($null -eq (VaultUser))
  $dialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
  Start-Sleep -Milliseconds 400
  [void]$p.CloseMainWindow(); [void]$p.WaitForExit(15000)
  Rec 'exit: clean' $p.HasExited
}
catch { Rec 'run aborted' $false $_.Exception.Message }
finally {
  if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  [void][T.Vault]::CredDelete($target, 1, 0)
  $listener.Stop(); $listener.Close(); Stop-Job $job -ErrorAction SilentlyContinue; Remove-Job $job -Force -ErrorAction SilentlyContinue
}
Rec 'cleanup: test entry gone from Credential Manager' ($null -eq (VaultUser))
$r.GetEnumerator() | ForEach-Object { '{0,-58} {1}' -f $_.Key, $_.Value }
"data root: $root"
if (@($r.Values | Where-Object { $_ -like 'FAIL*' }).Count -gt 0) { 'Protected Mongoku sign-in native checks: FAIL'; exit 1 }
'Protected Mongoku sign-in native checks: PASS'
