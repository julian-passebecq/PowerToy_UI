# Observer for the manual MX Master acceptance (started by mx-acceptance-setup.ps1). Logs changes only:
# Quick Ring visibility and position vs pointer, which window is in front, active Power Ops tab, saved capture count.
# Other applications are logged by process name only - never their window titles.
param([Parameter(Mandatory)][string]$DataDir, [Parameter(Mandatory)][int]$PowerOpsPid, [Parameter(Mandatory)][string]$Log)
$ErrorActionPreference = 'Continue'
Add-Type -Namespace O -Name W -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
'@
"$(Get-Date -Format 'HH:mm:ss.fff') observer started for Power Ops pid $PowerOpsPid" | Set-Content $Log -Encoding utf8
$last = @{}
function Note($key, $value) { if ($last[$key] -ne $value) { $last[$key] = $value; "$(Get-Date -Format 'HH:mm:ss.fff') $key = $value" | Add-Content $Log -Encoding utf8 } }
$deadline = (Get-Date).AddHours(3)
while ((Get-Date) -lt $deadline) {
  if (-not (Get-Process -Id $PowerOpsPid -ErrorAction SilentlyContinue)) { Note 'powerops' 'exited'; break }
  $fg = [O.W]::GetForegroundWindow(); $fp = 0; [void][O.W]::GetWindowThreadProcessId($fg, [ref]$fp)
  if ($fp -eq $PowerOpsPid) { $sb = New-Object System.Text.StringBuilder 256; [void][O.W]::GetWindowText($fg, $sb, 256); Note 'foreground' ("Power Ops: " + $sb.ToString()) }
  else { Note 'foreground' ("other app: " + (Get-Process -Id $fp -ErrorAction SilentlyContinue).ProcessName) }
  $ring = [O.W]::FindWindow([NullString]::Value, 'Power Ops Quick Ring')
  $state = if ($ring -ne [IntPtr]::Zero -and [O.W]::IsWindowVisible($ring)) {
    $r = New-Object O.W+RECT; [void][O.W]::GetWindowRect($ring, [ref]$r); $c = New-Object O.W+POINT; [void][O.W]::GetCursorPos([ref]$c)
    "visible, centre ($([int](($r.L + $r.R) / 2)),$([int](($r.T + $r.B) / 2))), pointer ($($c.X),$($c.Y))" } else { 'hidden' }
  Note 'quick ring' $state
  try {
    $shell = Get-Content (Join-Path $DataDir 'shell-workspaces.json') -Raw -ErrorAction Stop | ConvertFrom-Json
    $ws = $shell.Workspaces | Where-Object Id -eq $shell.ActiveWorkspaceId
    $tab = $ws.Tabs | Where-Object Id -eq $ws.ActiveTabId
    Note 'active tab' ("$($ws.Name): tab $([array]::IndexOf(@($ws.Tabs.Id), $tab.Id) + 1)/$(@($ws.Tabs).Count) = $($tab.ModuleId)")
  } catch { }
  try { Note 'captures saved' (@((Get-Content (Join-Path $DataDir 'workspace.json') -Raw -ErrorAction Stop | ConvertFrom-Json).Notes).Count) } catch { }
  Start-Sleep -Milliseconds 150
}
"$(Get-Date -Format 'HH:mm:ss.fff') observer stopped" | Add-Content $Log -Encoding utf8
