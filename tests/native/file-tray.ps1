# Native Windows check for the V2.3 File tray, using fresh isolated --data-dir folders and a temporary watched
# folder (never the real Downloads: the test settings turn Downloads watching off).
# Run with PowerShell 7 (pwsh) from the repo root after .\scripts\build.ps1.
# WARNING: replaces the clipboard several times (copy actions are the feature under test). Plain text that was on the
# clipboard before the run is put back at the end; images or files that were on it are not.
# -Drag also drags one tray row onto a throwaway drop-target window (synthesized mouse; refuses to start unless the
# pointer is over the test Power Ops and the drop point is over the test window).
# Input otherwise: UI Automation patterns only, plus one global shortcut (Ctrl+Alt+Shift+Y) that Windows consumes. No synthesized
# clicks, typing or drags reach other applications. Leave the desktop idle for the CPU measurement.
param([switch]$Drag)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.IO.Compression.ZipFile
Add-Type @'
using System;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
public static class N {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool UnregisterHotKey(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  public static string TitleAt(int x, int y) { var pt = new POINT { X = x, Y = y }; var sb = new StringBuilder(256); GetWindowText(GetAncestor(WindowFromPoint(pt), 2), sb, 256); return sb.ToString(); }
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  public static uint PidAt(int x, int y) { var pt = new POINT { X = x, Y = y }; uint p; GetWindowThreadProcessId(GetAncestor(WindowFromPoint(pt), 2), out p); return p; }
  public static void Drag(int x, int y, int tx, int ty) {
    SetCursorPos(x, y); Thread.Sleep(200); mouse_event(2, 0, 0, 0, UIntPtr.Zero);
    for (int i = 1; i <= 25; i++) { Thread.Sleep(40); SetCursorPos(x + (tx - x) * i / 25, y + (ty - y) * i / 25); }
    Thread.Sleep(300); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
  }
  public static void Key(byte[] mods, byte key) {
    foreach (var m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
    keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero);
    for (int i = mods.Length - 1; i >= 0; i--) keybd_event(mods[i], 0, 2, UIntPtr.Zero);
  }
}
public static class Clip {
  [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr h);
  [DllImport("user32.dll")] static extern bool CloseClipboard();
  [DllImport("user32.dll")] static extern bool EmptyClipboard();
  [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint f);
  [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint f, IntPtr h);
  [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint f);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern uint RegisterClipboardFormat(string n);
  [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
  [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
  [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
  [DllImport("shell32.dll", CharSet=CharSet.Unicode)] static extern uint DragQueryFile(IntPtr h, uint i, StringBuilder s, uint n);
  static bool Open() { for (int i = 0; i < 40; i++) { if (OpenClipboard(IntPtr.Zero)) return true; Thread.Sleep(50); } return false; }
  public static string[] Files() {
    if (!Open()) return null;
    try {
      IntPtr h = GetClipboardData(15); if (h == IntPtr.Zero) return new string[0];
      uint n = DragQueryFile(h, 0xFFFFFFFF, null, 0); var result = new string[n];
      for (uint i = 0; i < n; i++) { var sb = new StringBuilder(1024); DragQueryFile(h, i, sb, 1024); result[i] = sb.ToString(); }
      return result;
    } finally { CloseClipboard(); }
  }
  public static string Text() {
    if (!Open()) return null;
    try { IntPtr h = GetClipboardData(13); if (h == IntPtr.Zero) return null; IntPtr p = GlobalLock(h); try { return Marshal.PtrToStringUni(p); } finally { GlobalUnlock(h); } }
    finally { CloseClipboard(); }
  }
  public static int DropEffect() {
    if (!Open()) return -1;
    try { IntPtr h = GetClipboardData(RegisterClipboardFormat("Preferred DropEffect")); if (h == IntPtr.Zero) return -1; IntPtr p = GlobalLock(h); try { return Marshal.ReadInt32(p); } finally { GlobalUnlock(h); } }
    finally { CloseClipboard(); }
  }
  public static bool Has(string name) { return IsClipboardFormatAvailable(RegisterClipboardFormat(name)); }
  public static bool HasId(uint id) { return IsClipboardFormatAvailable(id); }
  public static bool SetText(string s) {
    if (!Open()) return false;
    try {
      EmptyClipboard(); byte[] bytes = Encoding.Unicode.GetBytes(s + "\0");
      IntPtr h = GlobalAlloc(2, (UIntPtr)bytes.Length); IntPtr p = GlobalLock(h); Marshal.Copy(bytes, 0, p, bytes.Length); GlobalUnlock(h);
      return SetClipboardData(13, h) != IntPtr.Zero;
    } finally { CloseClipboard(); }
  }
}
'@
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]; $CT = [System.Windows.Automation.ControlType]
$exe = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\JUtility.App\bin\Release\net8.0-windows10.0.19041.0\JUtilityPalette.exe'
$root = Join-Path $env:TEMP ("powerops-tray-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$inbox = Join-Path $root 'inbox'; $projectDir = Join-Path $root 'project-folder'; $fixtures = Join-Path $root 'fixtures'
New-Item -ItemType Directory -Force $inbox, $projectDir, $fixtures | Out-Null
$r = [ordered]@{}; $m = [ordered]@{}
function Skip($name, $detail) { $r[$name] = "NOT RUN  ($detail)" }
function Rec($name, $ok, $detail = '') { $r[$name] = $(if ($ok) { 'PASS' } else { 'FAIL' }) + $(if ($detail) { "  ($detail)" } else { '' }) }
function Until([scriptblock]$check, [int]$ms = 8000) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms) { if (& $check) { return $true }; Start-Sleep -Milliseconds 150 }; return [bool](& $check) }
function Cond($prop, $value) { New-Object System.Windows.Automation.PropertyCondition($prop, $value) }
function ByName($parent, [string]$name, $type = $null, [int]$tries = 40) {
  $c = Cond $A::NameProperty $name
  if ($type) { $c = New-Object System.Windows.Automation.AndCondition($c, (Cond $A::ControlTypeProperty $type)) }
  for ($i = 0; $i -lt $tries; $i++) { $e = $parent.FindFirst($T::Descendants, $c); if ($e) { return $e }; Start-Sleep -Milliseconds 150 }
  throw "UI element not found: $name"
}
function Invoke($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Rows($parent) { @($parent.FindAll($T::Descendants, (Cond $A::ClassNameProperty 'FileTrayItem')) | ForEach-Object { ($_.Current.Name -split ', ')[0] }) }
function StatusText($parent) {
  $texts = @($parent.FindAll($T::Descendants, (Cond $A::ControlTypeProperty $CT::Text)) | ForEach-Object { $_.Current.Name })
  return ($texts | Where-Object { $_ -like 'Copied*' -or $_ -like 'Could not*' -or $_ -like 'No text*' -or $_ -like 'Removed*' -or $_ -like 'Moved*' -or $_ -like 'Reading*' -or $_ -like 'Rendering*' }) -join ' | '
}
function Loaded($p, [string]$module) { $p.Refresh(); return [bool]($p.Modules | Where-Object { $_.ModuleName -ieq $module }) }
function Get-IdleCost($p, [int]$seconds) {
  $p.Refresh(); $cpu0 = $p.TotalProcessorTime; $h0 = $p.HandleCount
  Start-Sleep -Seconds $seconds
  $p.Refresh()
  return [pscustomobject]@{ CpuMs = [math]::Round(($p.TotalProcessorTime - $cpu0).TotalMilliseconds); Handles = "$h0 -> $($p.HandleCount)"; WorkingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 1); PrivateMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1) }
}
# Keep the main window handle: Process.Refresh() can later resolve MainWindowHandle to the Quick Shelf.
function Start-App { $p = Start-Process $exe -ArgumentList '--data-dir', "`"$root`"" -PassThru; for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }; $script:mainHandle = $p.MainWindowHandle; Start-Sleep -Seconds 3; return $p }
function Stop-App($p) {
  [void][N]::PostMessage($script:mainHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero); [void]$p.WaitForExit(15000)
  if ($p.HasExited) { return $true }
  $script:exitDetail = (@($A::RootElement.FindAll($T::Children, (Cond $A::ProcessIdProperty $p.Id)) | ForEach-Object { "'$($_.Current.Name)'" }) -join ', ')
  Stop-Process -Id $p.Id -Force; return $false
}
function Nav($main, [string]$title) { Invoke (ByName $main $title $CT::Button); Start-Sleep -Milliseconds 800 }

# ---- Fixtures (synthetic, generated here) ----------------------------------------------------------------------
function Pdf([string]$path, [string[]]$objects) {
  $ms = New-Object IO.MemoryStream; $enc = [Text.Encoding]::Latin1
  function W([object]$x) { $b = if ($x -is [byte[]]) { $x } else { $enc.GetBytes([string]$x) }; $ms.Write($b, 0, $b.Length) }
  W "%PDF-1.4`n"; $offsets = @()
  for ($i = 0; $i -lt $objects.Count; $i++) { $offsets += $ms.Position; W "$($i + 1) 0 obj`n"; W $script:pdfBodies[$objects[$i]]; W "`nendobj`n" }
  $xref = $ms.Position; W "xref`n0 $($objects.Count + 1)`n0000000000 65535 f `n"
  foreach ($o in $offsets) { W ("{0:D10} 00000 n `n" -f $o) }
  W "trailer << /Size $($objects.Count + 1) /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n"
  [IO.File]::WriteAllBytes($path, $ms.ToArray())
}
function Stream([byte[]]$data, [string]$dict) { $enc = [Text.Encoding]::Latin1; return [byte[]]($enc.GetBytes("<< $dict /Length $($data.Length) >>`nstream`n") + $data + $enc.GetBytes("`nendstream")) }
function TextBitmap([string]$text, [int]$w, [int]$h) {
  $bmp = New-Object Drawing.Bitmap $w, $h; $g = [Drawing.Graphics]::FromImage($bmp); $g.Clear([Drawing.Color]::White)
  $g.TextRenderingHint = 'AntiAliasGridFit'; $g.DrawString($text, ([Drawing.Font]::new('Arial', [single]40, [Drawing.FontStyle]::Bold)), [Drawing.Brushes]::Black, 20, ($h / 2 - 30)); $g.Dispose(); return $bmp
}
$content = [Text.Encoding]::Latin1.GetBytes("BT /F1 22 Tf 72 700 Td (INVOICE 4821 total due 99 EUR) Tj 0 -30 Td (Payable to Power Ops test fixture) Tj ET")
$script:pdfBodies = @{
  cat = '<< /Type /Catalog /Pages 2 0 R >>'; pages = '<< /Type /Pages /Kids [3 0 R] /Count 1 >>'
  pageText = '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>'
  font = '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'
  textStream = [Text.Encoding]::Latin1.GetString((Stream $content ''))
}
Pdf (Join-Path $fixtures 'invoice.pdf') @('cat', 'pages', 'pageText', 'font', 'textStream')
# Scanned PDF: one grayscale image, no text layer.
$scanBmp = TextBitmap 'SCANNED CONTRACT 7731' 1200 300
$gray = New-Object byte[] (1200 * 300); for ($y = 0; $y -lt 300; $y++) { for ($x = 0; $x -lt 1200; $x++) { $gray[$y * 1200 + $x] = $scanBmp.GetPixel($x, $y).R } }; $scanBmp.Dispose()
$zipped = New-Object IO.MemoryStream; $z = New-Object IO.Compression.ZLibStream($zipped, [IO.Compression.CompressionLevel]::Optimal); $z.Write($gray, 0, $gray.Length); $z.Dispose()
$draw = [Text.Encoding]::Latin1.GetBytes('q 540 0 0 135 36 600 cm /Im1 Do Q')
$script:pdfBodies.pageImage = '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /XObject << /Im1 4 0 R >> >> /Contents 5 0 R >>'
$script:pdfBodies.image = [Text.Encoding]::Latin1.GetString((Stream $zipped.ToArray() '/Type /XObject /Subtype /Image /Width 1200 /Height 300 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode'))
$script:pdfBodies.drawStream = [Text.Encoding]::Latin1.GetString((Stream $draw ''))
Pdf (Join-Path $fixtures 'scan.pdf') @('cat', 'pages', 'pageImage', 'image', 'drawStream')
$png = TextBitmap 'RECEIPT NUMBER 5519' 1000 200; $png.Save((Join-Path $fixtures 'receipt.png'), [Drawing.Imaging.ImageFormat]::Png); $png.Dispose()
$docx = Join-Path $fixtures 'brief.docx'
$zip = [IO.Compression.ZipFile]::Open($docx, 'Create'); $sw = New-Object IO.StreamWriter($zip.CreateEntry('word/document.xml').Open())
$sw.Write('<?xml version="1.0"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Quarterly brief for Foil</w:t></w:r></w:p><w:p><w:r><w:t>Second paragraph</w:t></w:r></w:p></w:body></w:document>'); $sw.Dispose(); $zip.Dispose()
Set-Content (Join-Path $inbox 'old-note.txt') 'An older note'; foreach ($ts in 'CreationTime', 'LastWriteTime') { (Get-Item (Join-Path $inbox 'old-note.txt')).$ts = (Get-Date).AddDays(-1) }

$gesture = 'Ctrl+Alt+Shift+Y'
if (-not [N]::RegisterHotKey([IntPtr]::Zero, 0x7791, 0x4007, 0x59)) { "BLOCKED: $gesture is owned by another application."; exit 2 }
[void][N]::UnregisterHotKey([IntPtr]::Zero, 0x7791)
if (-not [N]::RegisterHotKey([IntPtr]::Zero, 0x7792, 0x4007, 0x55)) { 'BLOCKED: Ctrl+Alt+Shift+U is owned by another application.'; exit 2 }
[void][N]::UnregisterHotKey([IntPtr]::Zero, 0x7792)
[ordered]@{ Format = 'powerops-file-tray'; SchemaVersion = 1; Enabled = $true; WatchDownloads = $false; ExtraFolders = @(@{ Path = $inbox; Label = 'WhatsApp' }); MaxItems = 6; ProjectFolders = @() } |
  ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root 'file-tray.json') -Encoding utf8
[ordered]@{ Format = 'powerops-quick-actions'; SchemaVersion = 1; Mode = 'QuickShelf'; GlobalShortcutsEnabled = $true
  GlobalShortcuts = @(@{ Gesture = $gesture; ActionId = 'tray.copyLatest' }, @{ Gesture = 'Ctrl+Alt+Shift+U'; ActionId = 'app.open' }); Ring = @('capture.region'); Shelf = @('tray.show', 'app.open')
  ShelfOrientation = 'Horizontal'; ShelfAlwaysOnTop = $true; ShelfAutoHide = $false; ShelfLeft = $null; ShelfTop = $null; WorkspaceOverrides = @(); WebApps = @() } |
  ConvertTo-Json -Depth 5 | Set-Content (Join-Path $root 'quick-actions.json') -Encoding utf8
$savedClipboard = [Clip]::Text()
$p = $null
try {
  # ---- Run 1 ----------------------------------------------------------------------------------------------------
  $p = Start-App
  $main = $A::FromHandle($mainHandle)
  Rec 'lazy: no OCR/PDF/WinRT components at startup' (-not ((Loaded $p 'Windows.Media.Ocr.dll') -or (Loaded $p 'Windows.Data.Pdf.dll') -or (Loaded $p 'WinRT.Runtime.dll') -or (Loaded $p 'Microsoft.Windows.SDK.NET.dll')))
  Start-Sleep -Seconds 5
  $m['idle, tray never opened (30 s)'] = Get-IdleCost $p 30

  $sw = [Diagnostics.Stopwatch]::StartNew(); Nav $main 'File tray'
  Rec 'open: existing file listed by the first scan' (Until { (Rows $main) -contains 'old-note.txt' }) (((Rows $main) -join ', ') + " in $($sw.ElapsedMilliseconds) ms")
  foreach ($f in 'invoice.pdf', 'scan.pdf', 'receipt.png', 'brief.docx') { Copy-Item (Join-Path $fixtures $f) $inbox; Start-Sleep -Milliseconds 300 }
  Set-Content (Join-Path $inbox 'big.pdf.crdownload') 'downloading...'
  Rec 'drop: four new files appear, newest first' (Until { ((Rows $main) | Select-Object -First 4) -join ',' -eq 'brief.docx,receipt.png,scan.pdf,invoice.pdf' }) ((Rows $main) -join ', ')
  Start-Sleep -Milliseconds 600
  Rec 'partial download (.crdownload) not listed' (-not ((Rows $main) -match 'crdownload|^big'))
  Rename-Item (Join-Path $inbox 'big.pdf.crdownload') 'big.pdf'
  Rec 'partial download listed once renamed' (Until { (Rows $main)[0] -eq 'big.pdf' }) ((Rows $main) -join ', ')
  $writer = [IO.File]::Open((Join-Path $inbox 'voice.txt'), 'CreateNew', 'Write', 'None'); $writer.Write([byte[]](72, 105, 33), 0, 3); $writer.Flush()
  Start-Sleep -Seconds 2
  Rec 'file still being written is not listed' (-not ((Rows $main) -contains 'voice.txt'))
  $writer.Dispose()
  Rec 'listed once the writer closes it' (Until { (Rows $main)[0] -eq 'voice.txt' })
  Rec 'bounded to N=6: oldest dropped' ((Rows $main).Count -eq 6 -and -not ((Rows $main) -contains 'old-note.txt')) ((Rows $main) -join ', ')

  if ($Drag) {
    # Drag-out: a separate STA process shows a drop target that records the FileDrop it receives.
    $dropLog = Join-Path $root 'drop.txt'; $target = Join-Path $root 'drop-target.ps1'
    Set-Content $target @"
Add-Type -AssemblyName System.Windows.Forms
`$f = New-Object Windows.Forms.Form; `$f.Text = 'PowerOps tray drop target'; `$f.AllowDrop = `$true; `$f.TopMost = `$true
`$f.StartPosition = 'Manual'; `$f.Left = 40; `$f.Top = 40; `$f.Width = 320; `$f.Height = 220; `$f.ShowInTaskbar = `$false
`$f.Add_DragEnter({ if (`$_.Data.GetDataPresent('FileDrop')) { `$_.Effect = 'Copy' } })
`$f.Add_DragDrop({ Set-Content '$dropLog' ((`$_.Data.GetData('FileDrop')) -join ';'); `$f.Close() })
`$t = New-Object Windows.Forms.Timer; `$t.Interval = 30000; `$t.Add_Tick({ `$f.Close() }); `$t.Start()
[Windows.Forms.Application]::Run(`$f)
"@
    $dropper = Start-Process powershell.exe -ArgumentList '-NoProfile', '-STA', '-File', "`"$target`"" -PassThru -WindowStyle Hidden
    [void](Until { [N]::PidAt(200, 150) -eq $dropper.Id } 10000)
    [N]::Key(@(0x11, 0x12, 0x10), 0x55); Start-Sleep -Milliseconds 900 # app.open: Power Ops to the front (Windows consumes the hotkey)
    $hDrop = [N]::FindWindow([NullString]::Value, 'PowerOps tray drop target'); [void][N]::SetWindowPos($hDrop, [IntPtr](-1), 0, 0, 0, 0, 0x13); Start-Sleep -Milliseconds 300
    $row = @($main.FindAll($T::Descendants, (Cond $A::ClassNameProperty 'FileTrayItem')))[0]; $b = $row.Current.BoundingRectangle
    $sx = [int]($b.X + 30); $sy = [int]($b.Y + $b.Height / 2); $tx = 200; $ty = 150
    if ([N]::PidAt($sx, $sy) -ne $p.Id -or [N]::PidAt($tx, $ty) -ne $dropper.Id) {
      $styles = 'exstyle main 0x{0:X}, drop target 0x{1:X}' -f [N]::GetWindowLong($mainHandle, -20), [N]::GetWindowLong($hDrop, -20)
      Skip 'drag-out to another app' "desktop not in the expected state: start ($sx,$sy) belongs to pid $([N]::PidAt($sx, $sy)) (Power Ops $($p.Id)); drop point belongs to '$([N]::TitleAt($tx, $ty))'; $styles"
    }
    else {
      [N]::Drag($sx, $sy, $tx, $ty)
      $dropped = Until { Test-Path $dropLog } 5000
      Rec 'drag-out to another app: FileDrop with the file' ($dropped -and (Get-Content $dropLog -Raw).Trim() -eq (Join-Path $inbox ($row.Current.Name -split ', ')[0])) $(if ($dropped) { Get-Content $dropLog -Raw })
    }
    if (-not $dropper.HasExited) { Stop-Process -Id $dropper.Id -Force }
  }
  Start-Sleep -Seconds 5
  $m['idle, tray open and watching 6 files, no OCR yet (30 s)'] = Get-IdleCost $p 30
  [void][Clip]::SetText('sentinel-1')
  Invoke (ByName $main 'Copy as file: invoice.pdf' $CT::Button)
  $files = $null; [void](Until { $script:files = [Clip]::Files(); $files.Count -eq 1 } 3000)
  Rec 'Copy as file: CF_HDROP holds the file' ($files.Count -eq 1 -and $files[0] -eq (Join-Path $inbox 'invoice.pdf')) ($files -join ';')
  Rec 'Copy as file: Preferred DropEffect = Copy' ([Clip]::DropEffect() -eq 1) ([Clip]::DropEffect())

  $sw.Restart(); Invoke (ByName $main 'Copy text: invoice.pdf' $CT::Button)
  Rec 'Copy text (PDF text layer)' (Until { ([Clip]::Text()) -like '*INVOICE 4821*' } 15000) ("$($sw.ElapsedMilliseconds) ms; " + (StatusText $main))
  Rec 'PDF text did not need Windows OCR' (-not (Loaded $p 'Windows.Media.Ocr.dll'))
  $sw.Restart(); Invoke (ByName $main 'Copy text: brief.docx' $CT::Button)
  Rec 'Copy text (Word)' (Until { ([Clip]::Text()) -like 'Quarterly brief for Foil*Second paragraph*' } 10000) "$($sw.ElapsedMilliseconds) ms"
  $sw.Restart(); Invoke (ByName $main 'Copy text: receipt.png' $CT::Button)
  $ocr = Until { ([Clip]::Text()) -match '5519' } 30000
  Rec 'Copy text (image, Windows OCR)' $ocr ("$($sw.ElapsedMilliseconds) ms; clipboard: " + ([Clip]::Text() -replace "`n", ' / '))
  Rec 'Windows OCR loaded only after use' (Loaded $p 'Windows.Media.Ocr.dll')
  $sw.Restart(); Invoke (ByName $main 'Copy text: scan.pdf' $CT::Button)
  Rec 'Copy text (scanned PDF, OCR fallback)' (Until { ([Clip]::Text()) -match '7731' } 30000) ("$($sw.ElapsedMilliseconds) ms; " + (StatusText $main))
  [void][Clip]::SetText('sentinel-2')
  $sw.Restart(); Invoke (ByName $main 'Copy as image: invoice.pdf' $CT::Button)
  Rec 'Copy as image (PDF page 1): PNG + bitmap' (Until { [Clip]::Has('PNG') -and [Clip]::HasId(8) } 15000) ("$($sw.ElapsedMilliseconds) ms; " + (StatusText $main))

  [void][Clip]::SetText('sentinel-3')
  [N]::Key(@(0x11, 0x12, 0x10), 0x59)
  [void](Until { $script:files = [Clip]::Files(); $files.Count -eq 1 } 4000)
  Rec 'global shortcut: copy last received file' ($files.Count -eq 1 -and $files[0] -eq (Join-Path $inbox 'voice.txt')) ($files -join ';')

  Invoke (ByName $main 'Remove from tray: big.pdf' $CT::Button)
  Rec 'Remove: gone from the tray, file kept' ((Until { -not ((Rows $main) -contains 'big.pdf') }) -and (Test-Path (Join-Path $inbox 'big.pdf')))

  # Quick Shelf entry -> flyout (no-activate), copy from it.
  $shelf = $null; foreach ($w in $A::RootElement.FindAll($T::Children, (Cond $A::ProcessIdProperty $p.Id))) { if ($w.Current.Name -eq 'Power Ops Quick Shelf') { $shelf = $w } }
  Invoke (ByName $shelf 'File tray' $CT::Button); Start-Sleep -Milliseconds 900
  $hFly = [N]::FindWindow([NullString]::Value, 'Power Ops File tray')
  Rec 'Shelf: File tray flyout shown' ($hFly -ne [IntPtr]::Zero -and [N]::IsWindowVisible($hFly))
  Rec 'Shelf: flyout did not take focus' ([N]::GetForegroundWindow() -ne $hFly)
  $fly = $A::FromHandle($hFly)
  Rec 'flyout: same list' ((Rows $fly)[0] -eq 'voice.txt') ((Rows $fly) -join ', ')
  Invoke (ByName $fly 'Copy as file: brief.docx' $CT::Button)
  [void](Until { $script:files = [Clip]::Files(); $files.Count -eq 1 -and $files[0] -like '*brief.docx' } 3000)
  Rec 'flyout: Copy as file' ($files[0] -eq (Join-Path $inbox 'brief.docx'))
  Invoke (ByName $fly 'Close file tray' $CT::Button); Start-Sleep -Milliseconds 400
  Rec 'flyout: closes' (-not [N]::IsWindowVisible($hFly))

  # Prompt Builder: {{file}} and {{file_text}} from the chosen tray file.
  Invoke (ByName $main 'Use in Prompt Builder: brief.docx' $CT::Button); Start-Sleep -Milliseconds 900
  $body = ByName $main 'Prompt module body' $CT::Edit
  $body.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Summarize {{file}}: {{file_text}}')
  Invoke (ByName $main 'Preview' $CT::Button)
  $preview = ByName $main 'Prompt preview' $CT::Edit
  Rec 'Prompt Builder: {{file}} and {{file_text}} filled' (Until { $v = $preview.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value; $v -like '*Summarize brief.docx: Quarterly brief for Foil*' } 10000) `
    (($preview.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -split "`n")[0])

  # Sidebar layout shows the newest files at the top.
  Invoke (ByName $main 'Sidebar' $CT::Button); Start-Sleep -Milliseconds 1200
  $side = Rows $main
  Rec 'Sidebar: tray section with the newest files' ($side.Count -ge 1 -and $side.Count -le 5 -and $side[0] -eq 'voice.txt') ($side -join ', ')
  Invoke (ByName $main 'Compact' $CT::Button); Start-Sleep -Milliseconds 800
  Nav $main 'File tray'

  Start-Sleep -Seconds 8
  $m['idle, tray open and watching, after OCR (30 s)'] = Get-IdleCost $p 30
  $leaks = @(Get-ChildItem $root -File | Where-Object { (Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue) -match 'INVOICE 4821|RECEIPT NUMBER|SCANNED CONTRACT|Quarterly brief|invoice\.pdf|receipt\.png|voice\.txt' } | ForEach-Object Name)
  Rec 'no file names or contents in any data file' ($leaks.Count -eq 0) ($leaks -join ', ')
  $ok = Stop-App $p; Rec 'exit 1: clean' $ok $(if (-not $ok) { 'still open: ' + $exitDetail })

  # ---- Run 2: move to a project folder (remembered folder), settings persisted, still lazy ------------------------
  $ws = Get-Content (Join-Path $root 'workspace.json') -Raw | ConvertFrom-Json
  $project = $ws.Projects | Where-Object { -not $_.IsArchived } | Sort-Object Name | Select-Object -First 1
  $tray = Get-Content (Join-Path $root 'file-tray.json') -Raw | ConvertFrom-Json
  $tray.ProjectFolders = @(@{ ProjectId = $project.Id; Path = $projectDir }); $tray | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root 'file-tray.json') -Encoding utf8
  $p = Start-App; $main = $A::FromHandle($mainHandle)
  Rec 'restart: still lazy' (-not (Loaded $p 'Windows.Media.Ocr.dll'))
  Nav $main 'File tray'
  [void](Until { (Rows $main) -contains 'receipt.png' })
  Invoke (ByName $main 'Move to project folder: receipt.png' $CT::Button)
  $dialog = $null; [void](Until { $h = [N]::FindWindow([NullString]::Value, 'Move to project folder'); if ($h -ne [IntPtr]::Zero) { $script:dialog = $A::FromHandle($h) }; $null -ne $dialog })
  Rec 'move: dialog shows the remembered folder' ($null -ne $dialog -and (ByName $dialog $projectDir $CT::Text 10) -ne $null) $project.Name
  Invoke (ByName $dialog 'Move' $CT::Button)
  Rec 'move: file moved, nothing overwritten' ((Until { Test-Path (Join-Path $projectDir 'receipt.png') }) -and -not (Test-Path (Join-Path $inbox 'receipt.png')))
  Rec 'move: stays in the tray at its new place' (Until { @($main.FindAll($T::Descendants, (Cond $A::ClassNameProperty 'FileTrayItem')) | Where-Object { $_.Current.Name -like "receipt.png, Moved to $($project.Name),*" }).Count -eq 1 })
  $ok = Stop-App $p; Rec 'exit 2: clean' $ok $(if (-not $ok) { 'still open: ' + $exitDetail })
  Rec 'settings file holds settings only' ((Get-Content (Join-Path $root 'file-tray.json') -Raw) -notmatch 'receipt|invoice|voice|brief')
}
catch { Rec 'run aborted' $false $_.Exception.Message }
finally {
  if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  if ($null -ne $savedClipboard) { [void][Clip]::SetText($savedClipboard) }
}
$r.GetEnumerator() | ForEach-Object { '{0,-55} {1}' -f $_.Key, $_.Value }
$m.GetEnumerator() | ForEach-Object { '{0,-55} CPU {1} ms, handles {2}, working set {3} MB, private {4} MB' -f $_.Key, $_.Value.CpuMs, $_.Value.Handles, $_.Value.WorkingSetMB, $_.Value.PrivateMB }
"data root: $root"
if (@($r.Values | Where-Object { $_ -like 'FAIL*' }).Count -gt 0) { 'File tray native checks: FAIL'; exit 1 }
'File tray native checks: PASS'
