<#
.SYNOPSIS
  Makes a narrated demo video of Style Guide (MP4, 1920x1080), following
  tools\demo_video\Demo_Video_Standard.md.
  1. Opens the plugin's window in its test host (form mode) on a copy of PDT15 and drives it with
     posted messages (WinForms: tree-view selection, button clicks, combo-box choices, text), as
     tests\ui_smoke.ps1 does. The mouse and the keyboard are never used. The window is captured
     after each step. The Word export is rendered through Word (COM) for one more step.
  2. Draws each frame: a headline, the window with orange boxes round the controls used and a
     pointer, and the narration as subtitles.
  3. Speaks the narration (Azure AI Speech, see azure_tts.ps1) and joins the steps with ffmpeg.
  The text is in narration.json (UTF-8). This file stays ASCII.

.EXAMPLE
  .\make_demo.ps1 -Lang en -NoVoice
#>
param(
    [string]$Lang = 'en',
    [string]$Work = '',
    [string]$Project = 'C:\ClaudeCode Projects\TestProject\PDT15',
    # Where the demo copy of the project goes while the window is captured (see below).
    [string]$DemoRoot = 'C:\My Paratext 9 Projects',
    # The plugin's test host (it opens the same window as the plugin).
    [string]$Exe = '',
    # An Azure voice (a name ending in 'Neural', e.g. en-US-JennyNeural; key and region from
    # AZURE_SPEECH_KEY / AZURE_SPEECH_REGION, see azure_tts.ps1) or a Windows voice (System.Speech).
    [string]$Voice = '',
    [int]$Rate = -1,                 # Windows voices: -10..10
    [string]$AzureRate = '0%',       # Azure voices: e.g. '-5%'
    # ffmpeg.exe to use; by default a newer GPL build in C:\ffmpeg-*, else the one on PATH.
    [string]$FFmpeg = '',
    # Use the screenshots of the last capture in $Work (no window is driven): for a voiced build or
    # a narration change after a -NoVoice test.
    [switch]$Reuse,
    # Only capture the window (then build with -Reuse).
    [switch]$CaptureOnly,
    # Zoom into the boxed controls in steps without cues (written as ..._zoom.mp4).
    [switch]$Zoom,
    # No narration: each step stays up as long as its subtitle takes to read (for testing).
    [switch]$NoVoice,
    # Background music (looped, kept soft; lowered further while the voice speaks). '' = none.
    [string]$Music = 'C:\Users\Pleckrone\Dropbox\Camtasia work\Background Music Files\Aetheric - Coconut Kind of Love (freetouse.com).mp3',
    [double]$MusicVolume = 0.30,        # the music on its own, at the start (the standard)
    [double]$MusicUnderVoice = 0.025,    # from the narration on (the standard)
    [double]$CoverIntro = 3.5,          # seconds of music alone on the cover before the narration
    # The cover's second line, under the plugin's name (narration.json, step "title").
    [string]$Series = 'Plugins by Phil',
    # Dissolve between steps, and dip to black round the slides (seconds). This window has no
    # count line, so steps always dissolve (the standard, section 6); $Wipe is kept for that rule.
    [double]$Dissolve = 1.0,
    [double]$Wipe = 2.0,
    [double]$Dip = 1.4,
    # How many steps each pass of the video join takes (fewer = less memory).
    [int]$JoinSteps = 6
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path (Split-Path $here) 'tests\TestHost\bin\Release\StyleGuideTestHost.exe' }
if (-not $Work) { $Work = Join-Path $env:TEMP "styleguide_demo_$Lang" }
if (-not $Voice) { $Voice = @{ 'en' = 'en-US-JennyNeural'; 'es' = 'es-MX-JorgeNeural'; 'fr' = 'fr-FR-RemyMultilingualNeural' }[$Lang] }   # the standard's voices
$azure = $Voice -match 'Neural$'
$musicOn = $Music -and (Test-Path -LiteralPath $Music)
if ($Music -and -not $musicOn) { "music file not found, none used: $Music" }
$es = $Lang -like 'es*'; $isFr = $Lang -like 'fr*'
# Style Guide is English only so far (Strings.For is ready for ES/FR); T() is kept for the slides.
function T($en, $sp, $frText) { if ($es) { $sp } elseif ($isFr -and $null -ne $frText) { $frText } else { $en } }

$narr = Get-Content -LiteralPath (Join-Path $here 'narration.json') -Raw -Encoding UTF8 | ConvertFrom-Json
function Text($id) {
    $t = ($narr.steps | Where-Object { $_.id -eq $id } | Select-Object -First 1).$Lang
    if (-not $t) { throw "narration.json has no '$Lang' text for step $id" }
    $t
}
# Box cues in the narration (the standard, section 8). "{1}" before a phrase shows box 1 (the
# step's boxes, in order) from there on, "{0,2}" boxes 0 and 2, "{}" none; the pointer goes on the
# first box named. Before the first cue no box is shown. A text without cues shows all its boxes
# the whole time. Cues are left out of the subtitles and the voice.
$cueRx = '\{\s*(\d+(\s*,\s*\d+)*)?\s*\}'
function Clean($raw) { if ($raw) { [regex]::Replace($raw, $cueRx, '') } else { $raw } }
# The parts of a step: @{ Pos (in the clean text); Show (box numbers) }, or none without cues.
function Cues($raw) {
    $clean = ''; $marks = @(); $last = 0
    if (-not $raw) { return , @() }
    foreach ($m in [regex]::Matches($raw, $cueRx)) {
        $clean += $raw.Substring($last, $m.Index - $last)
        $show = @()
        if ($m.Groups[1].Success) { $show = @($m.Groups[1].Value -split '\s*,\s*' | ForEach-Object { [int]$_ }) }
        $marks += [pscustomobject]@{ Pos = $clean.Length; Show = $show }
        $last = $m.Index + $m.Length
    }
    if ($marks.Count -and $marks[0].Pos -gt 0) { $marks = @([pscustomobject]@{ Pos = 0; Show = @() }) + $marks }
    , $marks
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms, System.Speech
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DmWin {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
    /// <summary>Per-monitor aware, so window rectangles, DWM frames and screen copies all use
    /// physical pixels on monitors with different scales.</summary>
    public static void BeAware() { try { if (SetProcessDpiAwarenessContext((IntPtr)(-4))) return; } catch { } SetProcessDPIAware(); }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Rect r, int size);
    /// <summary>The visible frame (without the invisible resize borders), in screen pixels.</summary>
    public static Rect Frame(IntPtr h) {
        Rect r;
        if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(Rect))) != 0) GetWindowRect(h, out r);
        return r;
    }
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, int flags);
    /// <summary>Keeps a window above all others (no move, no size), so screen copies show it.</summary>
    public static void TopMost(IntPtr h) { SetWindowPos(h, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
    /// <summary>Moves a window (no size change, no activation).</summary>
    public static void MoveTo(IntPtr h, int x, int y) { SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010); }
}
'@
[DmWin]::BeAware()
$AE = [Windows.Automation.AutomationElement]
$Scope = [Windows.Automation.TreeScope]
$CT = [Windows.Automation.ControlType]
$root = $AE::RootElement

# ------------------------------------------------------------ the demo project
$shots = Join-Path $Work 'shots'
$frames = Join-Path $Work 'frames'
$stepsFile = Join-Path $Work 'steps.xml'
if ($Reuse) {
    if (-not (Test-Path -LiteralPath $stepsFile)) { throw "-Reuse: no capture in $Work (run once without -Reuse)" }
    $script:steps = @(Import-Clixml -LiteralPath $stepsFile)
    if (Test-Path $frames) { Remove-Item -Recurse -Force $frames }
    New-Item -ItemType Directory -Force $frames | Out-Null
    Get-ChildItem -LiteralPath $Work -File | Where-Object { $_.Name -ne 'steps.xml' } | Remove-Item -Force
    "reusing the capture of $((Get-Item -LiteralPath $stepsFile).LastWriteTime): $(($script:steps | ForEach-Object { $_.Id }) -join ' ')"
}
else {
if (Test-Path $Work) { Remove-Item -Recurse -Force $Work }
New-Item -ItemType Directory -Force $shots, $frames | Out-Null
# The status line shows where the answers are saved, <project>\local\StyleGuide, so the copy of
# PDT15 is put where a user's project would be, $DemoRoot\PDT15, but only when that folder does not
# exist yet (a real project is never touched); it is removed again after the capture. Otherwise it
# stays in $Work. The window is driven with posted messages (WinForms), so the keyboard is free,
# except while the Save As dialog of the export is open (it is never captured: it shows the
# user's Documents folder).
$script:madeRoot = $false; $script:madeProj = $false
$proj = Join-Path $DemoRoot 'PDT15'
if (-not (Test-Path -LiteralPath $proj)) {
    try {
        if (-not (Test-Path -LiteralPath $DemoRoot)) { New-Item -ItemType Directory $DemoRoot | Out-Null; $script:madeRoot = $true }
        New-Item -ItemType Directory $proj | Out-Null; $script:madeProj = $true
    }
    catch { "cannot make $proj ($($_.Exception.Message)): using the work folder"; $proj = Join-Path $Work 'PDT15' }
}
else { "$proj exists: using the work folder instead"; $proj = Join-Path $Work 'PDT15' }
New-Item -ItemType Directory -Force $proj | Out-Null
# The project's own files (books, Settings.xml, BookNames.xml, TermRenderings.xml,
# hyphenatedWords.txt...), without backups and reports; RegExBackup1.sfm also starts with \id GEN.
Get-ChildItem -LiteralPath $Project -File | Where-Object { $_.Extension -notin '.bak', '.html' -and $_.Name -ne 'RegExBackup1.sfm' } | Copy-Item -Destination $proj
$exportFile = Join-Path $Work 'export\PDT15 Punctuation Module.docx'
New-Item -ItemType Directory -Force (Split-Path $exportFile) | Out-Null
function RemoveDemoProject {
    if ($script:madeProj) { Remove-Item -LiteralPath $proj -Recurse -Force; $script:madeProj = $false }
    if ($script:madeRoot) { Remove-Item -LiteralPath $DemoRoot -Force; $script:madeRoot = $false }
}

# ------------------------------------------------------------ driving the window
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class MsWin {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, StringBuilder l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
    public static void PressButton(IntPtr h) { PostMessage(h, 0xF5, IntPtr.Zero, IntPtr.Zero); }
    public static void SetText(IntPtr h, string text) { SendMessage(h, 0x000C, IntPtr.Zero, text); }
    public static int ComboCount(IntPtr h) { return (int)SendMessage(h, 0x146, IntPtr.Zero, IntPtr.Zero); }
    public static string ComboItem(IntPtr h, int i) {
        int n = (int)SendMessage(h, 0x149, (IntPtr)i, IntPtr.Zero);
        var sb = new StringBuilder(n + 1);
        SendMessage(h, 0x148, (IntPtr)i, sb);
        return sb.ToString();
    }
    /// <summary>Chooses an item and tells the form (CBN_SELCHANGE), as a user's choice would.</summary>
    public static void ComboSelect(IntPtr h, int i) {
        SendMessage(h, 0x14E, (IntPtr)i, IntPtr.Zero);
        int id = GetDlgCtrlID(h);
        SendMessage(GetParent(h), 0x111, (IntPtr)((1 << 16) | (id & 0xFFFF)), h);
    }
    public static void ShowDropDown(IntPtr h, bool show) { SendMessage(h, 0x14F, (IntPtr)(show ? 1 : 0), IntPtr.Zero); }
    public static string GetText(IntPtr h) {
        int n = (int)SendMessage(h, 0x0E, IntPtr.Zero, IntPtr.Zero);
        var sb = new StringBuilder(n + 1);
        SendMessage(h, 0x0D, (IntPtr)(n + 1), sb);
        return sb.ToString();
    }
    /// <summary>TVM_GETNEXTITEM: 0 root, 1 next sibling, 4 first child.</summary>
    public static IntPtr TreeNext(IntPtr tree, int code, IntPtr item) { return SendMessage(tree, 0x110A, (IntPtr)code, item); }
    /// <summary>TVM_SELECTITEM with TVGN_CARET: WinForms raises BeforeSelect and AfterSelect.</summary>
    public static void TreeSelect(IntPtr tree, IntPtr item) { SendMessage(tree, 0x110B, (IntPtr)9, item); }
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref Pt p);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr a, IntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr p, IntPtr a, IntPtr size, uint type);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr p, IntPtr a, byte[] b, IntPtr n, out IntPtr done);
    [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr p, IntPtr a, byte[] b, IntPtr n, out IntPtr done);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    struct Pt { public int X, Y; }
    /// <summary>A tree row's text on the screen (TVM_GETITEMRECT, text only): x, y, width, height.
    /// The RECT must be in the tree's own process and starts with the item's handle (written as
    /// 8 bytes, which suits a 32- or 64-bit process).</summary>
    public static int[] TreeItemRect(IntPtr tree, IntPtr item) {
        uint pid; GetWindowThreadProcessId(tree, out pid);
        IntPtr p = OpenProcess(0x0008 | 0x0010 | 0x0020, false, pid);
        if (p == IntPtr.Zero) throw new Exception("OpenProcess failed");
        IntPtr mem = VirtualAllocEx(p, IntPtr.Zero, (IntPtr)16, 0x3000, 0x04);
        try {
            byte[] b = new byte[16]; IntPtr done;
            BitConverter.GetBytes(item.ToInt64()).CopyTo(b, 0);
            WriteProcessMemory(p, mem, b, (IntPtr)16, out done);
            if (SendMessage(tree, 0x1104, (IntPtr)1, mem) == IntPtr.Zero) throw new Exception("TVM_GETITEMRECT failed");
            ReadProcessMemory(p, mem, b, (IntPtr)16, out done);
            Pt a = new Pt(); a.X = BitConverter.ToInt32(b, 0); a.Y = BitConverter.ToInt32(b, 4);
            Pt z = new Pt(); z.X = BitConverter.ToInt32(b, 8); z.Y = BitConverter.ToInt32(b, 12);
            ClientToScreen(tree, ref a); ClientToScreen(tree, ref z);
            return new[] { a.X, a.Y, z.X - a.X, z.Y - a.Y };
        } finally { VirtualFreeEx(p, mem, IntPtr.Zero, 0x8000); CloseHandle(p); }
    }
}
'@
$script:proc = $null
$script:win = $null
trap {
    if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill(); $script:proc.WaitForExit(5000) | Out-Null }
    RemoveDemoProject
    throw $_
}
function All { @($script:win.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) }
# A control by its text (WinForms gives UI Automation no control names).
function ByText($pattern) { All | Where-Object { $_.Current.Name -match $pattern } | Select-Object -First 1 }
function Need($pattern) { $el = ByText $pattern; if (-not $el) { throw "no control matching $pattern" }; $el }
function ByClass($pattern) { $el = All | Where-Object { $_.Current.ClassName -match $pattern } | Select-Object -First 1; if (-not $el) { throw "no control of class $pattern" }; $el }
function Press($el) { [MsWin]::PressButton([IntPtr]$el.Current.NativeWindowHandle); Start-Sleep -Milliseconds 900 }
function Hwnd($el) { [IntPtr]$el.Current.NativeWindowHandle }
# The status line right of Rescan ("66 books read, ..." when a survey is done).
function StatusEl { All | Where-Object { $_.Current.ClassName -match 'STATIC' -and $_.Current.BoundingRectangle.Y -lt ($script:win.Current.BoundingRectangle.Y + 90) -and $_.Current.Name -notmatch '^Module:' } | Select-Object -First 1 }
function StatusText { $el = StatusEl; if ($el) { [MsWin]::GetText((Hwnd $el)) } else { '' } }
# Waits until the survey of the module shown is done: the status reads "... books read ..." and
# differs from $old (each module has its own counts).
function WaitSurvey($old) {
    for ($i = 0; $i -lt 360; $i++) {
        Start-Sleep -Milliseconds 500
        $s = StatusText
        if ($s -match 'books read' -and $s -ne $old) { Start-Sleep -Milliseconds 800; return $s }
    }
    throw "the survey did not finish (status: $(StatusText))"
}
function Launch {
    $psi = New-Object Diagnostics.ProcessStartInfo $Exe
    $psi.Arguments = "form `"$proj`""
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    foreach ($k in @('SG_MODULE', 'SG_BOOKS', 'SG_GOTOLOG', 'SG_LANG')) { $psi.EnvironmentVariables.Remove($k) }
    $script:proc = [Diagnostics.Process]::Start($psi)
    for ($i = 0; $i -lt 120 -and -not $script:win; $i++) {
        Start-Sleep -Milliseconds 250; $script:proc.Refresh()
        if ($script:proc.MainWindowHandle -ne 0 -and $script:proc.MainWindowTitle -match ' \d+\.\d+ - ') {
            $script:win = $AE::FromHandle($script:proc.MainWindowHandle)
        }
    }
    if (-not $script:win) { throw 'window did not appear' }
    $h = Hwnd $script:win
    # On a screen without the mouse cursor (hover effects would show in the pictures), fully on it.
    $cursor = [Windows.Forms.Cursor]::Position
    $screen = [Windows.Forms.Screen]::AllScreens | Where-Object { -not $_.Bounds.Contains($cursor) } | Select-Object -First 1
    if ($screen) { [DmWin]::MoveTo($h, $screen.WorkingArea.X + 20, $screen.WorkingArea.Y + 20) }
    else { 'only one screen: park the mouse cursor away from the window' | Out-Host }
    [DmWin]::TopMost($h)
}
function ComboEl { $el = All | Where-Object { $_.Current.ControlType -eq $CT::ComboBox } | Select-Object -First 1; if (-not $el) { throw 'module box not found' }; $el }
# Opens a combo box's list (the mouse is not used), so a step shows the choices; returns the open
# list and its chosen item (UI Automation).
function OpenList($comboEl) {
    [MsWin]::ShowDropDown((Hwnd $comboEl), $true)
    Start-Sleep -Milliseconds 800
    $lc = New-Object Windows.Automation.PropertyCondition ($AE::ControlTypeProperty, $CT::List)
    $list = $comboEl.FindFirst($Scope::Descendants, $lc)
    if (-not $list) {
        $byPid = New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $script:proc.Id)
        $list = @($root.FindAll($Scope::Children, $byPid)) | Where-Object { $_.Current.ClassName -eq 'ComboLBox' } | Select-Object -First 1
    }
    if (-not $list) { throw 'the open list was not found' }
    $ic = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.SelectionItemPattern]::IsSelectedProperty, $true)
    @{ List = $list; Selected = $list.FindFirst($Scope::Descendants, $ic) }
}
function CloseList($comboEl) { [MsWin]::ShowDropDown((Hwnd $comboEl), $false); Start-Sleep -Milliseconds 500 }
# A window of the test host other than the main one (a dialog or message box), by its title.
function DialogTitled($title) {
    $byPid = New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $script:proc.Id)
    for ($i = 0; $i -lt 60; $i++) {
        $all = @($root.FindAll($Scope::Children, $byPid)) + @($script:win.FindAll($Scope::Children, [Windows.Automation.Condition]::TrueCondition))
        $d = $all | Where-Object { $_.Current.ControlType -eq $CT::Window -and $_.Current.Name -eq $title } | Select-Object -First 1
        if ($d) { Start-Sleep -Milliseconds 900; return $d }
        Start-Sleep -Milliseconds 250
    }
    throw "the dialog '$title' did not appear"
}
function InDialog($dialog, $pattern) {
    $el = @($dialog.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) | Where-Object { $_.Current.Name -match $pattern } | Select-Object -First 1
    if (-not $el) { throw "no '$pattern' in the dialog" }
    $el
}
# A stand-in element for a box from screen pixels (Box reads only Current.BoundingRectangle).
function RectEl($r) { [pscustomobject]@{ Current = [pscustomobject]@{ BoundingRectangle = (New-Object Windows.Rect $r[0], $r[1], $r[2], $r[3]) } } }

# The tree of sections and questions. WinForms tree views reach UI Automation as a plain pane, so
# rows are found and selected with tree-view messages. $section and $row count from 0.
function TreeEl { ByClass 'SysTreeView32' }
function TreeRow($section, $row) {
    $tree = Hwnd (TreeEl)
    $s = [MsWin]::TreeNext($tree, 0, [IntPtr]::Zero)
    for ($i = 0; $i -lt $section; $i++) { $s = [MsWin]::TreeNext($tree, 1, $s) }
    $r = [MsWin]::TreeNext($tree, 4, $s)
    for ($i = 0; $i -lt $row; $i++) { $r = [MsWin]::TreeNext($tree, 1, $r) }
    if ($r -eq [IntPtr]::Zero) { throw "no row $row in section $section" }
    $r
}
# Selects a row and checks, by the guide box (topic + NIV), that it is the row meant.
function SelectRow($item, $expect) {
    [MsWin]::TreeSelect((Hwnd (TreeEl)), $item)
    Start-Sleep -Milliseconds 1000
    $g = [MsWin]::GetText((Hwnd (GuideEl)))
    if ($g -notmatch $expect) { throw "the row shown is not the one meant ('$expect'): $($g.Substring(0, [Math]::Min(120, $g.Length)))" }
}
# The first question row whose guide text (topic + NIV) matches $expect, selected: every row is
# tried in tree order (the tree's sections are not the module's tables).
function FindRow($expect) {
    $tree = Hwnd (TreeEl)
    $s = [MsWin]::TreeNext($tree, 0, [IntPtr]::Zero)
    while ($s -ne [IntPtr]::Zero) {
        $r = [MsWin]::TreeNext($tree, 4, $s)
        while ($r -ne [IntPtr]::Zero) {
            [MsWin]::TreeSelect($tree, $r)
            Start-Sleep -Milliseconds 150
            if ([MsWin]::GetText((Hwnd (GuideEl))) -match $expect) { Start-Sleep -Milliseconds 900; return $r }
            $r = [MsWin]::TreeNext($tree, 1, $r)
        }
        $s = [MsWin]::TreeNext($tree, 1, $s)
    }
    throw "no question row matches '$expect'"
}
function RowEl($item) { RectEl ([MsWin]::TreeItemRect((Hwnd (TreeEl)), $item)) }
function GuideEl { ByClass 'RICHEDIT50W' }
function AnswerEl { ByClass '\.EDIT\.' }
function FoundEl { $el = All | Where-Object { $_.Current.ControlType -eq $CT::Group } | Select-Object -First 1; if (-not $el) { throw 'no Found group' }; $el }
function ListEls { @(All | Where-Object { $_.Current.ClassName -match 'SysListView32' } | Sort-Object { $_.Current.BoundingRectangle.X }) }

# ------------------------------------------------------------ capturing a step
$script:steps = @()
# The window as it is on the screen (open lists and dialogs in front of it included), in physical pixels.
function Capture([IntPtr]$h) {
    $f = [DmWin]::Frame($h)
    $bmp = New-Object Drawing.Bitmap ($f.R - $f.L), ($f.B - $f.T)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($f.L, $f.T, 0, 0, $bmp.Size)
    $g.Dispose()
    @{ Bmp = $bmp; Rect = $f }
}
# A box round some elements, in the capture's pixels (UI Automation gives physical pixels here).
function Box($els, $f) {
    $x1 = 1e9; $y1 = 1e9; $x2 = -1e9; $y2 = -1e9
    foreach ($e in $els) {
        if (-not $e) { continue }
        $b = $e.Current.BoundingRectangle
        if ($b.IsEmpty) { continue }
        $x1 = [Math]::Min($x1, $b.X); $y1 = [Math]::Min($y1, $b.Y)
        $x2 = [Math]::Max($x2, $b.X + $b.Width); $y2 = [Math]::Max($y2, $b.Y + $b.Height)
    }
    if ($x2 -lt $x1) { return $null }
    , @([int]($x1 - $f.L), [int]($y1 - $f.T), [int]($x2 - $x1), [int]($y2 - $y1))
}
# $groups: a list of element lists, one orange box each. $pointer: index of the box to point at, or -1.
# $pointEl: an element to put the pointer on instead (e.g. the chosen item of an open list).
function Step($id, $groups, $pointer = -1, $pointEl = $null) {
    Start-Sleep -Milliseconds 400
    $main = Capture (Hwnd $script:win)
    $boxes = @()
    foreach ($grp in $groups) {
        $b = Box @($grp) $main.Rect
        if (-not $b) { throw "step $id : a box has no element on screen" }
        $boxes += , $b
    }
    $pointAt = $null
    if ($pointEl) { $pointAt = Box @($pointEl) $main.Rect }
    $file = Join-Path $shots "$id.png"
    $main.Bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png)
    $main.Bmp.Dispose()
    # Rows = -1: the joins between steps are always dissolves.
    $script:steps += [pscustomobject]@{ Id = $id; Shot = $file; Boxes = $boxes; Pointer = [int]$pointer; Dialog = $null; Rows = -1; PointAt = $pointAt }
    "captured $id   ($(StatusText))"
}

# A page of the exported .docx, rendered by Word (COM) at 220 dpi and cut round the table row that
# holds $needle; one box round that answer cell. Returns the step, or $null without Word.
function WordStep($id, $docx, $needle) {
    $word = $null; $doc = $null
    try {
        $word = New-Object -ComObject Word.Application
        $word.Visible = $false
        $doc = $word.Documents.Open($docx, $false, $true)
        $doc.ActiveWindow.View.Type = 3   # print layout, so pages exist
        $rng = $doc.Content
        $find = $rng.Find
        if (-not $find.Execute($needle)) { throw "'$needle' is not in the exported document" }
        $cell = $rng.Cells.Item(1)
        $page = [int]$rng.Information(3)            # wdActiveEndPageNumber
        # Positions of the cell's first character (points from the page's corner); a cell range's
        # own position is the row's left edge. The cell starts its padding before the text.
        $startRng = $cell.Range.Duplicate; $startRng.Collapse(1)
        $left = [double]$startRng.Information(5) - [double]$cell.LeftPadding   # wdHorizontalPositionRelativeToPage
        $top = [double]$startRng.Information(6) - 1                            # wdVerticalPositionRelativeToPage
        $endRng = $cell.Range.Duplicate; $endRng.MoveEnd(1, -1) | Out-Null; $endRng.Collapse(0)
        $bottom = [double]$endRng.Information(6) + 12   # the last line's top + its height
        $width = [double]$cell.Width
        $pageW = [double]$doc.PageSetup.PageWidth; $pageH = [double]$doc.PageSetup.PageHeight
        # The row's first cell (the topic), so the box covers the whole row.
        $first = $cell.Row.Cells.Item(1); $firstRng = $first.Range.Duplicate; $firstRng.Collapse(1)
        $rowLeft = [double]$firstRng.Information(5) - [double]$first.LeftPadding
        $bits = [byte[]]$doc.ActiveWindow.Panes.Item(1).Pages.Item($page).EnhMetaFileBits
        $ms = New-Object IO.MemoryStream (, $bits)
        $meta = New-Object Drawing.Imaging.Metafile $ms
        $scale = 220.0 / 72.0
        $pw = [int]($pageW * $scale); $ph = [int]($pageH * $scale)
        $pageBmp = New-Object Drawing.Bitmap $pw, $ph
        $g = [Drawing.Graphics]::FromImage($pageBmp)
        $g.Clear([Drawing.Color]::White); $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'AntiAlias'
        $g.DrawImage($meta, 0, 0, $pw, $ph)
        $g.Dispose(); $meta.Dispose()
        # Cut: the page's full width, about as tall as the step's picture area allows, centred on the row.
        $cutH = [int]($pw * 0.42)
        $mid = ($top + $bottom) / 2 * $scale
        $cy = [int][Math]::Max(0, [Math]::Min($ph - $cutH, $mid - $cutH / 2))
        $cut = $pageBmp.Clone((New-Object Drawing.Rectangle 0, $cy, $pw, $cutH), $pageBmp.PixelFormat)
        $pageBmp.Dispose()
        $file = Join-Path $shots "$id.png"
        $cut.Save($file, [Drawing.Imaging.ImageFormat]::Png); $cut.Dispose()
        # (Each value apart: in an array literal the commas bind before a minus.)
        $y = [int]($top * $scale) - $cy; $hgt = [int](($bottom - $top) * $scale)
        $rowBox = @([int]($rowLeft * $scale), $y, [int](($left + $width - $rowLeft) * $scale), $hgt)
        $cellBox = @([int]($left * $scale), $y, [int]($width * $scale), $hgt)
        "row at $rowLeft, cell at $left (+$width), $top..$bottom pt" | Out-Host
        "rendered $id from page $page of the export" | Out-Host
        [pscustomobject]@{ Id = $id; Shot = $file; Boxes = @(, $rowBox) + @(, $cellBox); Pointer = -1; Dialog = $null; Rows = -1; PointAt = $null }
    }
    catch { "no picture of the Word document ($($_.Exception.Message) $($_.InvocationInfo.PositionMessage)): step $id left out" | Out-Host; $null }
    finally {
        if ($doc) { $doc.Close($false) | Out-Null }
        if ($word) { $word.Quit() | Out-Null; [Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null }
    }
}

# ------------------------------------------------------------ the demo
Launch
# While the first module is surveyed: the module box and the status line (orange while it runs).
Start-Sleep -Milliseconds 1500
if ((StatusText) -match 'books read') { throw 'the survey finished before the first picture' }
Step 's01_biblica' @(@(ComboEl), @(StatusEl)) -1
$status = WaitSurvey ''
"survey: $status"

# The tree: sections and questions, marked empty / suggested / reviewed.
Step 's02_tree' @(@(TreeEl), @(StatusEl)) -1

# One question: the question-mark row (section 1, row 1), as in tests\ui_smoke.ps1.
$qRow = TreeRow 1 1
SelectRow $qRow 'QUESTION MARK|question mark'
$answerLabel = Need '^Our project$'
Step 's03_question' @(@(GuideEl), @($answerLabel, (AnswerEl)), @(FoundEl)) -1

# What was found: the patterns with their counts, and the places (double-click goes there).
$lists = ListEls
if ($lists.Count -lt 2) { throw 'the two lists of the Found box were not found' }
$summary = All | Where-Object { $_.Current.ClassName -match 'STATIC' -and $_.Current.Name -match 'Double-click' } | Select-Object -First 1
Step 's04_found' @(@($summary), @($lists[0]), @($lists[1])) -1

# The team's own answer: typing ticks Reviewed, and the row turns green. Then Save.
$answerH = Hwnd (AnswerEl)
$suggestion = [MsWin]::GetText($answerH)
if (-not $suggestion) { throw 'no suggestion in the answer box' }
$typed = 'We always open a question with an inverted question mark, as Spanish does.'
# A multiline edit box sends no change notice for WM_SETTEXT, so the last character is typed
# (WM_CHAR at the end of the text), as a user's typing would; that ticks Reviewed.
$full = $suggestion.TrimEnd() + "`r`n" + $typed
[MsWin]::SetText($answerH, $full.Substring(0, $full.Length - 1))
[MsWin]::SendMessage($answerH, 0xB1, [IntPtr]($full.Length - 1), [IntPtr]($full.Length - 1)) | Out-Null
[MsWin]::PostMessage($answerH, 0x102, [IntPtr][int][char]$full[$full.Length - 1], [IntPtr]1) | Out-Null
Start-Sleep -Milliseconds 800
if ([MsWin]::GetText($answerH) -ne $full) { throw 'the answer box does not hold the typed text' }
$reviewed = Need '^Reviewed$'
$save = Need '^Save$'
Press $save
Start-Sleep -Milliseconds 600
if ((StatusText) -notmatch '^Saved ') { throw "Save did not report: $(StatusText)" }
Step 's05_reviewed' @(@(AnswerEl), @($reviewed), @(RowEl $qRow), @($save), @(StatusEl)) 3
$answersFile = Join-Path $proj 'local\StyleGuide\Punctuation.xml'
[xml]$ax = Get-Content -LiteralPath $answersFile -Encoding UTF8
$saved = $ax.Answers.Answer | Where-Object { $_.id -eq 'sent-question' }
if (-not $saved -or $saved.state -ne 'reviewed' -or $saved.'#text' -notmatch [regex]::Escape($typed)) { throw "the answer was not saved as reviewed: $($saved.OuterXml)" }
"saved: $($saved.state)"

# Export to Word: the question about suggestions nobody has reviewed yet (Yes = include them).
$export = Need '^Export to Word'
Press $export
$ask = DialogTitled 'Style Guide'
$askText = InDialog $ask 'nobody has reviewed'
$yes = InDialog $ask '^&?Yes$'
Step 's09_export' @(@($export), @($askText), @($yes)) 2
Press $yes
# Windows' Save As dialog: never captured (it opens in the user's Documents folder). The file
# name box gets a full path in the work folder, then Save.
$saveAs = DialogTitled 'Save As'
$nameBox = @($saveAs.FindAll($Scope::Descendants, (New-Object Windows.Automation.PropertyCondition ($AE::AutomationIdProperty, '1001')))) | Where-Object { $_.Current.NativeWindowHandle -ne 0 } | Select-Object -First 1
if (-not $nameBox) { throw 'the file name box of Save As was not found' }
[MsWin]::SetText((Hwnd $nameBox), $exportFile)
$saveBtn = @($saveAs.FindAll($Scope::Descendants, (New-Object Windows.Automation.PropertyCondition ($AE::AutomationIdProperty, '1')))) | Where-Object { $_.Current.NativeWindowHandle -ne 0 } | Select-Object -First 1
Press $saveBtn
$done = DialogTitled 'Style Guide'
Press (InDialog $done '^&?No$')
if (-not (Test-Path -LiteralPath $exportFile)) { throw "the export was not written: $exportFile" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($exportFile)
try { $xmlText = (New-Object IO.StreamReader ($zip.GetEntry('word/document.xml').Open())).ReadToEnd() } finally { $zip.Dispose() }
if ($xmlText -notmatch [regex]::Escape($typed)) { throw 'the typed answer is not in the exported document' }
"exported: $exportFile"
$wordStep = WordStep 's10_word' $exportFile $typed
if ($wordStep) { $script:steps += $wordStep }

# Another module: the list open on Quotations; each module is surveyed the first time it is shown.
$combo = ComboEl
$before = StatusText
$qi = -1
for ($i = 0; $i -lt [MsWin]::ComboCount((Hwnd $combo)); $i++) { if ([MsWin]::ComboItem((Hwnd $combo), $i) -match '^Quotations') { $qi = $i } }
if ($qi -lt 0) { throw 'Quotations is not in the module list' }
[MsWin]::ComboSelect((Hwnd $combo), $qi)
Start-Sleep -Milliseconds 700
$open = OpenList $combo
Step 's06_module' @(@($combo, $open.List), @(StatusEl)) -1 $open.Selected
CloseList $combo
$status = WaitSurvey $before
"quotations: $status"

# The quotation-mark system (section 3, row 0: qu-level1).
$quRow = FindRow 'first-level quotes'
Step 's07_quotes' @(@(RowEl $quRow), @(GuideEl), @(AnswerEl)) -1

# The books surveyed: the chooser (New Testament pressed), then Cancel.
$booksBtn = Need '^Books: \d+ of \d+'
Press $booksBtn
$chooser = DialogTitled 'Books to survey'
$nt = InDialog $chooser '^New Testament$'
Press $nt
$okBtn = InDialog $chooser '^OK$'
Step 's08_books' @(@($booksBtn), @($nt), @($okBtn)) 1
Press (InDialog $chooser '^Cancel$')

$script:proc.Kill(); $script:proc.WaitForExit(5000) | Out-Null
RemoveDemoProject
# The order of the video: the Word page after the export question, then the other module.
$order = 's01_biblica', 's02_tree', 's03_question', 's04_found', 's05_reviewed', 's09_export', 's10_word', 's06_module', 's07_quotes', 's08_books'
$script:steps = @($order | ForEach-Object { $id = $_; $script:steps | Where-Object { $_.Id -eq $id } })
$script:steps | Export-Clixml -LiteralPath $stepsFile
}
if ($CaptureOnly) { "captured in ${Work}; build the video with -Reuse"; return }

# The Paratext Tools menu, before the window opens, and again at the end of the install section:
# the user's screenshot with no item highlighted (tools\demo_video\tools_menu_plain_en.png,
# 2026-11-10), with Style Guide... highlighted by MenuShot.ps1, which also gives the two boxes
# (the Tools heading and the item).
$demoTools = Join-Path (Split-Path (Split-Path $here)) 'tools\demo_video'
. (Join-Path $demoTools 'MenuShot.ps1')
$menu = New-ToolsMenuShot -Item 'Style Guide...' -OutFile (Join-Path $Work 'tools_menu.png') -Lang $Lang
$mb = @(); foreach ($b in $menu.Boxes) { $mb += , $b }
$script:steps = @([pscustomobject]@{ Id = 's00_menu'; Shot = $menu.Shot; Boxes = $mb; Pointer = 1; Dialog = $null; Rows = -1 }) + $script:steps

# Getting and installing the tool: the shared install section (Install_Phils_tools.exe, see
# tools\demo_video\install\InstallSection.ps1), then the Tools menu again.
. (Join-Path $demoTools 'install\InstallSection.ps1')
$script:steps += Get-InstallSteps -Plugin 'StyleGuide' -Lang $Lang -Name (Text 'title').title
$script:steps += [pscustomobject]@{ Id = 'i5_ready'; Shot = $menu.Shot; Boxes = $mb; Pointer = 1; Dialog = $null; Rows = -1 }

# ------------------------------------------------------------ frames
$W = 1920; $H = 1080; $top = 96; $bottomBand = 190
# The "Sky" design (the user's choice): deep blue cover, light-to-mid blue steps, gold accents.
function Hex($h) { [Drawing.ColorTranslator]::FromHtml($h) }
$navy = Hex '#1F3A5F'          # headline bar
$orange = [Drawing.Color]::FromArgb(255, 140, 0)   # boxes round the controls
$gold = Hex '#F2B544'          # accents
$band = Hex '#18222E'          # subtitle band
$bgTop = Hex '#EEF7FF'; $bgBottom = Hex '#94BEE4'          # steps: the gradient
$coverTop = Hex '#2C5A9A'; $coverBottom = Hex '#0B1830'    # cover and closing slide
function Gradient($g, $x, $y, $w, $h, $c1, $c2) {
    $br = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.RectangleF $x, ($y - 1), $w, ($h + 2)), $c1, $c2, 90.0
    $g.FillRectangle($br, [float]$x, [float]$y, [float]$w, [float]$h)
    $br.Dispose()
}
function RoundRect($x, $y, $w, $h, $r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc([float]$x, [float]$y, $d, $d, 180, 90); $p.AddArc([float]($x + $w - $d), [float]$y, $d, $d, 270, 90)
    $p.AddArc([float]($x + $w - $d), [float]($y + $h - $d), $d, $d, 0, 90); $p.AddArc([float]$x, [float]($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure(); $p
}
function NewFrame { $f = New-Object Drawing.Bitmap $W, $H; $g = [Drawing.Graphics]::FromImage($f); $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'; @{ F = $f; G = $g } }
function Centered($g, $text, $font, $brush, $rect) {
    $sf = New-Object Drawing.StringFormat
    $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString($text, $font, $brush, $rect, $sf)
}
# A font of the given size, or smaller until the text fits on one line in $maxWidth pixels
# (long names, e.g. "Edicion de notas en lote", were cut off on the cover).
function FitFont($g, $text, $family, $size, $maxWidth) {
    $f = New-Object Drawing.Font $family, $size
    while ($size -gt 36 -and $g.MeasureString($text, $f).Width -gt $maxWidth) {
        $f.Dispose(); $size -= 2; $f = New-Object Drawing.Font $family, $size
    }
    $f
}
function Card($id, $file) {
    $t = Text $id
    $fr = NewFrame; $g = $fr.G
    Gradient $g 0 0 $W $H $coverTop $coverBottom
    $light = New-Object Drawing.SolidBrush $gold
    $grey = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(190, 205, 225))
    if ($id -eq 'title') {
        # The cover: a framed panel (gold border) with a tag on its top edge; the plugin's name
        # large and white, a gold rule, then the series smaller (the user's order).
        $px = 360; $py = 290; $pw = $W - 720; $ph = 470
        $panel = RoundRect $px $py $pw $ph 28
        $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(40, 255, 255, 255))), $panel)
        $g.DrawPath((New-Object Drawing.Pen $gold, 4), $panel)
        $tagFont = New-Object Drawing.Font 'Segoe UI Semibold', 20
        $tagText = T 'PARATEXT PLUGIN' 'COMPLEMENTO DE PARATEXT' 'MODULE PARATEXT'
        $tw = $g.MeasureString($tagText, $tagFont).Width + 44; $th = 48; $tx = ($W - $tw) / 2; $ty = $py - $th / 2
        $g.FillPath((New-Object Drawing.SolidBrush $gold), (RoundRect $tx $ty $tw $th 24))
        Centered $g $tagText $tagFont (New-Object Drawing.SolidBrush $coverBottom) (New-Object Drawing.RectangleF $tx, $ty, $tw, $th)
        Centered $g $t.title (FitFont $g $t.title 'Segoe UI Semibold' 80 ($pw - 60)) ([Drawing.Brushes]::White) (New-Object Drawing.RectangleF $px, ($py + 60), $pw, 170)
        $g.FillRectangle($light, [float]($W / 2 - 160), [float]($py + 250), 320, 5)
        Centered $g $Series (New-Object Drawing.Font 'Segoe UI', 50) $light (New-Object Drawing.RectangleF $px, ($py + 280), $pw, 120)
    }
    elseif ($id -like 'sec_*') {
        # A section slide (the user's "subtitle" slide): the cover's panel, its title large and
        # white, a gold rule, then the plugin's name smaller.
        $px = 360; $py = 290; $pw = $W - 720; $ph = 470
        $panel = RoundRect $px $py $pw $ph 28
        $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(40, 255, 255, 255))), $panel)
        $g.DrawPath((New-Object Drawing.Pen $gold, 4), $panel)
        Centered $g $t.title (FitFont $g $t.title 'Segoe UI Semibold' 80 ($pw - 60)) ([Drawing.Brushes]::White) (New-Object Drawing.RectangleF $px, ($py + 60), $pw, 170)
        $g.FillRectangle($light, [float]($W / 2 - 160), [float]($py + 250), 320, 5)
        # The plugin's name, smaller when it would not fit on one line (the French name was cut).
        $name = (Text 'title').title
        Centered $g $name (FitFont $g $name 'Segoe UI' 50 ($pw - 60)) $light (New-Object Drawing.RectangleF $px, ($py + 280), $pw, 120)
    }
    else {
        $f1 = New-Object Drawing.Font 'Segoe UI Semibold', 72
        $f2 = New-Object Drawing.Font 'Segoe UI', 34
        # Smaller when a long name would not fit on one line (the French name of this tool).
        $f1 = FitFont $g $t.title 'Segoe UI Semibold' 72 ($W - 200)
        Centered $g $t.title $f1 ([Drawing.Brushes]::White) (New-Object Drawing.RectangleF 80, 300, ($W - 160), 160)
        $g.FillRectangle($light, [float]($W / 2 - 160), 462, 320, 5)
        # The key points on one line, or each on its own line when they don't fit (a wrap fell in
        # the middle of a point in Spanish and French).
        $sub = $t.sub
        $subH = 120
        if ($g.MeasureString($sub, $f2).Width -gt ($W - 160)) { $sub = ($sub -split ('\s+' + [char]0xB7 + '\s+')) -join "`n"; $subH = 200 }
        Centered $g $sub $f2 $light (New-Object Drawing.RectangleF 80, 480, ($W - 160), $subH)
        # Credits, in small print.
        $credits = @(T 'Created with Claude AI' 'Creado con Claude AI' "Cr$([char]0xE9)$([char]0xE9) avec Claude AI")
        if (-not $NoVoice -and $azure) { $credits += T 'Narration: AI-generated voice (Microsoft Azure AI Speech)' "Narraci$([char]0xF3)n: voz generada por IA (Microsoft Azure AI Speech)" "Narration : voix g$([char]0xE9)n$([char]0xE9)r$([char]0xE9)e par IA (Microsoft Azure AI Speech)" }
        if ($musicOn) { $credits += T 'Music: Coconut Kind of Love by Aetheric (freetouse.com)' "M$([char]0xFA)sica: Coconut Kind of Love de Aetheric (freetouse.com)" 'Musique : Coconut Kind of Love par Aetheric (freetouse.com)' }
        $f3 = New-Object Drawing.Font 'Segoe UI', 22
        $y = $H - 70 - 42 * $credits.Count
        foreach ($c in $credits) { Centered $g $c $f3 $grey (New-Object Drawing.RectangleF 80, $y, ($W - 160), 42); $y += 42 }
    }
    $g.Dispose(); $fr.F.Save($file, [Drawing.Imaging.ImageFormat]::Png); $fr.F.Dispose()
}
function Pointer($g, $x, $y) {
    # A mouse arrow with its tip at (x, y).
    $s = 1.6
    $pts = @(@(0, 0), @(0, 25), @(6, 19), @(11, 30), @(15, 28), @(10, 18), @(18, 18)) | ForEach-Object { New-Object Drawing.PointF ($x + $_[0] * $s), ($y + $_[1] * $s) }
    $g.FillPolygon([Drawing.Brushes]::White, [Drawing.PointF[]]$pts)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::Black), 2
    $g.DrawPolygon($pen, [Drawing.PointF[]]$pts)
}
# The window area of a frame: the band between the headline and the subtitles, less margins.
$areaX = 40; $areaY = $top + 22; $areaW = $W - 80; $areaH = $H - $top - $bottomBand - 44
# The view of the whole window: the shot widened (or heightened) to the area's shape.
function FullView($iw, $ih) {
    $ar = $areaW / $areaH
    if ($iw / $ih -lt $ar) { $w0 = $ih * $ar; , @((($iw - $w0) / 2), 0, $w0, $ih) }
    else { $h0 = $iw / $ar; , @(0, (($ih - $h0) / 2), $iw, $h0) }
}
# The close-up of a step: the boxes (and the dialog) with a margin, in the area's shape, at most
# twice the size of the full view; $null when that would hardly be closer than the full view.
function ZoomView($st, $iw, $ih) {
    $rects = @($st.Boxes)
    if ($st.Dialog) { $rects += , $st.Dialog }
    if ($rects.Count -eq 0) { return $null }
    $x1 = 1e9; $y1 = 1e9; $x2 = -1e9; $y2 = -1e9
    foreach ($r in $rects) {
        $x1 = [Math]::Min($x1, $r[0]); $y1 = [Math]::Min($y1, $r[1])
        $x2 = [Math]::Max($x2, $r[0] + $r[2]); $y2 = [Math]::Max($y2, $r[1] + $r[3])
    }
    $pad = 0.05 * $iw
    $x1 -= $pad; $y1 -= $pad; $x2 += $pad; $y2 += $pad
    $ar = $areaW / $areaH
    $w = $x2 - $x1; $h = $y2 - $y1
    if ($w / $h -lt $ar) { $w = $h * $ar } else { $h = $w / $ar }
    $full = FullView $iw $ih
    $maxZoom = 2.0
    if ($full[2] / $w -gt $maxZoom) { $w = $full[2] / $maxZoom; $h = $w / $ar }
    if ($full[2] / $w -lt 1.15) { return $null }
    $cx = ($x1 + $x2) / 2; $cy = ($y1 + $y2) / 2
    $x = $cx - $w / 2; $y = $cy - $h / 2
    if ($w -le $iw) { $x = [Math]::Max(0, [Math]::Min($x, $iw - $w)) } else { $x = ($iw - $w) / 2 }
    if ($h -le $ih) { $y = [Math]::Max(0, [Math]::Min($y, $ih - $h)) } else { $y = ($ih - $h) / 2 }
    , @($x, $y, $w, $h)
}
# Between two views, at s (0..1): the centre moves in a straight line, the size changes
# geometrically, so the zoom feels even.
function MixView($a, $b, $s) {
    $w = $a[2] * [Math]::Pow($b[2] / $a[2], $s); $h = $a[3] * [Math]::Pow($b[3] / $a[3], $s)
    $cx = ($a[0] + $a[2] / 2) * (1 - $s) + ($b[0] + $b[2] / 2) * $s
    $cy = ($a[1] + $a[3] / 2) * (1 - $s) + ($b[1] + $b[3] / 2) * $s
    , @(($cx - $w / 2), ($cy - $h / 2), $w, $h)
}
# $part: one part of a step with cues (its Show = the boxes shown); $null = all boxes.
function StepFrame($st, $num, $total, $file, $view = $null, $part = $null) {
    $t = Text $st.Id
    $fr = NewFrame; $g = $fr.G
    Gradient $g 0 $top $W ($H - $top - $bottomBand) $bgTop $bgBottom
    # headline, with a gold line under it
    $g.FillRectangle((New-Object Drawing.SolidBrush $navy), 0, 0, $W, $top)
    $g.FillRectangle((New-Object Drawing.SolidBrush $gold), 0, ($top - 5), $W, 5)
    $fh = New-Object Drawing.Font 'Segoe UI Semibold', 30
    $g.DrawString($t.title, $fh, [Drawing.Brushes]::White, 40, 22)
    $fn = New-Object Drawing.Font 'Segoe UI', 20
    $sf = New-Object Drawing.StringFormat; $sf.Alignment = 'Far'
    $g.DrawString("$num / $total", $fn, (New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(190, 205, 225))), (New-Object Drawing.RectangleF 0, 34, ($W - 40), 40), $sf)
    # the window: the view (a rectangle of the shot) fills the area; float coordinates, so a
    # zoom moves smoothly. Whatever falls outside the band between headline and subtitles is cut.
    $img = [Drawing.Image]::FromFile($st.Shot)
    if (-not $view) { $view = FullView $img.Width $img.Height }
    $sc = [Math]::Min($areaW / $view[2], $areaH / $view[3])
    $ox = $areaX + ($areaW - $view[2] * $sc) / 2 - $view[0] * $sc
    $oy = $areaY + ($areaH - $view[3] * $sc) / 2 - $view[1] * $sc
    $iw = $img.Width * $sc; $ih = $img.Height * $sc
    $g.SetClip((New-Object Drawing.RectangleF 0, $top, $W, ($H - $top - $bottomBand)))
    # a soft shadow (spread, faint layers) and a white frame, so the grey window floats on the page
    for ($k = 12; $k -ge 2; $k -= 2) {
        $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(12, 10, 30, 60))), (RoundRect ($ox - $k + 6) ($oy - $k + 10) ($iw + 2 * $k) ($ih + 2 * $k) (10 + $k)))
    }
    $g.FillPath([Drawing.Brushes]::White, (RoundRect ($ox - 6) ($oy - 6) ($iw + 12) ($ih + 12) 10))
    $g.DrawImage($img, [float]$ox, [float]$oy, [float]$iw, [float]$ih)
    $pen = New-Object Drawing.Pen $orange, 5
    if ($st.Dialog) {
        $d = $st.Dialog
        $g.DrawRectangle((New-Object Drawing.Pen ([Drawing.Color]::FromArgb(31, 58, 95)), 4), [float]($ox + $d[0] * $sc - 4), [float]($oy + $d[1] * $sc - 4), [float]($d[2] * $sc + 8), [float]($d[3] * $sc + 8))
    }
    $i = 0; $pointAtShown = -not $part
    foreach ($b in $st.Boxes) {
        $shown = (-not $part) -or ($part.Show -contains $i)
        if ($shown) {
            $bx = $ox + $b[0] * $sc - 6; $by = $oy + $b[1] * $sc - 6; $bwid = $b[2] * $sc + 12; $bhei = $b[3] * $sc + 12
            $g.DrawRectangle($pen, [float]$bx, [float]$by, [float]$bwid, [float]$bhei)
            # With cues, the pointer (if the step has one) is on the first box named.
            $pointHere = if ($part) { $st.Pointer -ge 0 -and $part.Show[0] -eq $i } else { $i -eq $st.Pointer }
            if ($pointHere) { Pointer $g ($bx + $bwid * 0.6) ($by + $bhei * 0.6) }
            if ($st.PointAt) {
                $pa = $st.PointAt; $cx = $pa[0] + $pa[2] / 2; $cy = $pa[1] + $pa[3] / 2
                if ($cx -ge $b[0] -and $cx -le $b[0] + $b[2] -and $cy -ge $b[1] -and $cy -le $b[1] + $b[3]) { $pointAtShown = $true }
            }
        }
        $i++
    }
    if ($st.PointAt -and $pointAtShown) {
        $pa = $st.PointAt
        # at the right end of the item, so its text stays readable
        Pointer $g ($ox + ($pa[0] + $pa[2] * 0.88) * $sc) ($oy + ($pa[1] + $pa[3] * 0.5) * $sc)
    }
    $g.ResetClip()
    $iwShot = $img.Width; $ihShot = $img.Height
    $img.Dispose()
    # subtitles
    $g.FillRectangle((New-Object Drawing.SolidBrush $band), 0, ($H - $bottomBand), $W, $bottomBand)
    $fs = New-Object Drawing.Font 'Segoe UI', 27
    Centered $g (Clean $t.text) $fs ([Drawing.Brushes]::White) (New-Object Drawing.RectangleF 70, ($H - $bottomBand + 8), ($W - 140), ($bottomBand - 16))
    $g.Dispose(); $fr.F.Save($file, [Drawing.Imaging.ImageFormat]::Png); $fr.F.Dispose()
}

$order = @('title')
foreach ($st in $script:steps) {
    if ($st.Id -eq 'd1_folder') { $order += 'sec_install' }
    $order += $st.Id
}
$order += 'end'
# Slides (cover, section slides, closing slide) have no step number.
function IsCard($id) { $id -eq 'title' -or $id -eq 'end' -or $id -like 'sec_*' }
$stepIds = @($order | Where-Object { -not (IsCard $_) })
# Zoom timing: it starts when the dissolve into the step is over, and takes $zoomTime seconds.
$zoomStart = 1.3; $zoomTime = 1.6; $zoomFrames = [int]($zoomTime * 30)
$zooms = @{}; $partFrames = @{}
$n = 0
foreach ($id in $order) {
    $file = Join-Path $frames ('{0:D2}_{1}.png' -f $n, $id)
    if (IsCard $id) { Card $id $file }
    else {
        $st = $script:steps | Where-Object { $_.Id -eq $id }
        $num = [array]::IndexOf($stepIds, $id) + 1
        StepFrame $st $num $stepIds.Count $file
        $cues = Cues (Text $id).text
        if ($cues.Count) {
            $pf = @()
            for ($k = 0; $k -lt $cues.Count; $k++) {
                $cfile = Join-Path $frames ('{0:D2}_{1}_c{2}.png' -f $n, $id, $k)
                StepFrame $st $num $stepIds.Count $cfile $null $cues[$k]
                $pf += $cfile
            }
            $partFrames[$n] = $pf
            "cues $id : $(($cues | ForEach-Object { '{' + ($_.Show -join ',') + '}' }) -join ' ')"
        }
        elseif ($Zoom) {
            $img = [Drawing.Image]::FromFile($st.Shot); $iw0 = $img.Width; $ih0 = $img.Height; $img.Dispose()
            $v1 = ZoomView $st $iw0 $ih0
            if ($v1) {
                $v0 = FullView $iw0 $ih0
                $zf = @()
                for ($k = 1; $k -le $zoomFrames; $k++) {
                    $pp = $k / $zoomFrames
                    $ss = $pp * $pp * (3 - 2 * $pp)     # ease in and out
                    $zfile = Join-Path $frames ('{0:D2}_z{1:D3}.png' -f $n, $k)
                    StepFrame $st $num $stepIds.Count $zfile (MixView $v0 $v1 $ss)
                    $zf += $zfile
                }
                $zooms[$n] = $zf
                "zoom $id"
            }
        }
    }
    $n++
}

# ------------------------------------------------------------ speech and video
# Spoken text is kept between runs ($Work is emptied each time), so a rebuild asks Azure only for
# what changed (the free tier allows few requests a minute).
$ttsCache = Join-Path $env:TEMP 'bulknotes_demo_tts'
New-Item -ItemType Directory -Force $ttsCache | Out-Null
function Say($text, $wav) {
    if (-not $azure) { $tts.SetOutputToWaveFile($wav); $tts.Speak($text); $tts.SetOutputToNull(); return }
    $keyText = "$Voice|$AzureRate|$text|" + ($script:SayAs | ConvertTo-Json -Compress -Depth 4)
    $sha = [Security.Cryptography.SHA1]::Create()
    $hash = ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($keyText)) | ForEach-Object { $_.ToString('x2') }) -join ''
    $cached = Join-Path $ttsCache "$hash.wav"
    if (-not (Test-Path -LiteralPath $cached)) { Speak-Azure -Text $text -Voice $Voice -OutFile $cached -Rate $AzureRate }
    Copy-Item -LiteralPath $cached $wav -Force
}
# Seconds from the start of a WAV (16-bit PCM, data at byte 44) to the end of the speech in it.
function SpeechEnd($wav) {
    $bytes = [IO.File]::ReadAllBytes($wav)
    $byteRate = [BitConverter]::ToInt32($bytes, 28)
    $i = $bytes.Length - 2
    while ($i -ge 44 -and [Math]::Abs([int][BitConverter]::ToInt16($bytes, $i)) -lt 700) { $i -= 2 }
    [Math]::Max(0.0, ($i - 44) / $byteRate)
}
if (-not $NoVoice) {
    if ($azure) { . (Join-Path $here 'azure_tts.ps1') }
    else {
        $tts = New-Object System.Speech.Synthesis.SpeechSynthesizer
        $tts.SelectVoice($Voice)
        $tts.Rate = $Rate
    }
    "voice: $Voice"
}
else { 'voice: none' }
$ffmpeg = $FFmpeg
if (-not $ffmpeg) {
    $ffmpeg = Get-ChildItem 'C:\ffmpeg-*' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -Recurse -Filter ffmpeg.exe -ErrorAction SilentlyContinue } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $ffmpeg) { $ffmpeg = (Get-Command ffmpeg).Source }
"ffmpeg: $ffmpeg"
if (-not ((& $ffmpeg -hide_banner -filters 2>&1 | Out-String) -match '\bxfade\b')) {
    throw "This ffmpeg has no xfade filter (it needs ffmpeg 4.3 or later): $ffmpeg"
}
$inv = [Globalization.CultureInfo]::InvariantCulture
function Num($x) { ([double]$x).ToString('0.###', $inv) }

# The joins: a dip to black after the cover and before the closing slide, dissolves elsewhere.
$count = $order.Count
$joinType = @{}; $joinLen = @{}
for ($k = 1; $k -lt $count; $k++) {
    if ((IsCard $order[$k - 1]) -or (IsCard $order[$k])) { $joinType[$k] = 'fadeblack'; $joinLen[$k] = $Dip }
    else {
        $ra = ($script:steps | Where-Object { $_.Id -eq $order[$k - 1] }).Rows
        $rb = ($script:steps | Where-Object { $_.Id -eq $order[$k] }).Rows
        if ($ra -ge 0 -and $ra -eq $rb) { $joinType[$k] = 'wipeleft'; $joinLen[$k] = $Wipe }
        else { $joinType[$k] = 'fade'; $joinLen[$k] = $Dissolve }
    }
    "join $($order[$k - 1]) -> $($order[$k]): $($joinType[$k]) $($joinLen[$k]) s"
}
$fadeInFirst = 0.8; $endHold = 1.5
# Each step: its join in, a breath, the voice (or the reading time), a pause, its join out.
$vIn = @{}; $aIn = @(); $voiceIdx = @{}
$durs = @(); $leads = @()
$idx = 0
for ($n = 0; $n -lt $count; $n++) {
    $id = $order[$n]
    $frame = Join-Path $frames ('{0:D2}_{1}.png' -f $n, $id)
    $in = if ($n -eq 0) { $fadeInFirst } else { $joinLen[$n] }
    $out = if ($n -eq $count - 1) { $endHold } else { $joinLen[$n + 1] }
    $lead = $in + 0.3; $tail = 0.8
    if ($n -eq 0) { $lead += $CoverIntro }
    $text = Clean (Text $id).text
    if ($NoVoice) {
        # About 2.6 words a second: time to read the subtitle.
        $words = @($text -split '\s+' | Where-Object { $_ }).Count
        $secs = [Math]::Max(2.5, $words / 2.6)
        # The cover and closing slides show no subtitle: a short, fixed time.
        if ($id -eq 'title') { $secs = 1.5 } elseif ($id -eq 'end') { $secs = 4.5 } elseif ($id -like 'sec_*') { $secs = 2.5 }
        $wav = $null
    }
    else {
        $wav = Join-Path $Work ('{0:D2}.wav' -f $n)
        Say $text $wav
        $fs = [IO.File]::OpenRead($wav); $br = New-Object IO.BinaryReader $fs; $fs.Position = 28; $rate = $br.ReadInt32(); $br.Close()
        $secs = ((Get-Item $wav).Length - 44) / $rate
    }
    $dur = [Math]::Round([Math]::Max($lead + $secs + $tail + $out, 4.5), 2)
    $durs += $dur; $leads += $lead
    if ($partFrames.ContainsKey($n)) {
        # When each part starts: where its cue is spoken (the speech of the text before the cue,
        # spoken on its own), or without a voice, its share of the words.
        $cues = Cues (Text $id).text
        $starts = @()
        for ($k = 0; $k -lt $cues.Count; $k++) {
            $pos = $cues[$k].Pos
            if ($pos -eq 0) { $starts += 0.0; continue }
            $before = $text.Substring(0, $pos).Trim()
            if ($wav) {
                $pw = Join-Path $Work ('{0:D2}_cue{1}.wav' -f $n, $k)
                Say $before $pw
                $t = SpeechEnd $pw
            }
            else {
                $all = @($text -split '\s+' | Where-Object { $_ }).Count
                $t = $secs * @($before -split '\s+' | Where-Object { $_ }).Count / [Math]::Max(1, $all)
            }
            $starts += [Math]::Min($dur - 0.5, $lead + $t)
        }
        "  cues at $(($starts | ForEach-Object { Num $_ }) -join ', ') s"
        $q = { param($x) "file '" + ($x -replace '\\', '/') + "'" }
        $lines = @('ffconcat version 1.0')
        $pf = $partFrames[$n]
        for ($k = 0; $k -lt $pf.Count; $k++) {
            $end = if ($k -lt $pf.Count - 1) { $starts[$k + 1] } else { $dur + 1 }
            $lines += (& $q $pf[$k]); $lines += "duration $(Num ([Math]::Max(0.034, $end - $starts[$k])))"
        }
        $lines += (& $q $pf[-1])     # the concat reader ignores the last duration
        $listFile = Join-Path $Work ('{0:D2}_cues.txt' -f $n)
        [IO.File]::WriteAllLines($listFile, $lines)
        # Made into a clip of its own first: many image-list inputs in the one big join ran ffmpeg
        # out of memory.
        $clip = Join-Path $Work ('{0:D2}_cues.mp4' -f $n)
        & $ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i $listFile -vf 'fps=30,format=yuv420p' -t (Num $dur) -c:v libx264 -preset veryfast -crf 12 -tune stillimage -threads 4 $clip
        if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed on the cues of $id" }
        # One decoding thread each: with frame threads, every clip's decoder holds many 1080p
        # frames, and the big join ran out of memory.
        $vIn[$n] = @('-threads', '1', '-i', $clip)
    }
    elseif ($zooms.ContainsKey($n)) {
        $zf = $zooms[$n]
        $q = { param($x) "file '" + ($x -replace '\\', '/') + "'" }
        $zs = $in + 0.3
        $lines = @('ffconcat version 1.0', (& $q $frame), "duration $(Num $zs)")
        foreach ($z in $zf) { $lines += (& $q $z); $lines += 'duration 0.0333333' }
        $lines += (& $q $zf[-1]); $lines += "duration $(Num ($dur - $zs - $zoomTime + 1))"
        $lines += (& $q $zf[-1])     # the concat reader ignores the last duration
        $listFile = Join-Path $Work ('{0:D2}_zoom.txt' -f $n)
        [IO.File]::WriteAllLines($listFile, $lines)
        $vIn[$n] = @('-f', 'concat', '-safe', '0', '-i', $listFile)
    }
    else { $vIn[$n] = @('-threads', '1', '-loop', '1', '-framerate', '30', '-t', (Num $dur), '-i', $frame) }
    if ($wav) { $aIn += @('-i', $wav); $voiceIdx[$n] = $idx; $idx++ }
    "step $id $(Num $dur)s"
}
if (-not $NoVoice -and -not $azure) { $tts.Dispose() }
if ($musicOn) { $aIn += @('-stream_loop', '-1', '-i', $Music); $musicIdx = $idx; $idx++ }

# The length of the video: each join starts $joinLen before the end of what is joined so far.
$total = $durs[0]
for ($k = 1; $k -lt $count; $k++) { $total += $durs[$k] - $joinLen[$k] }
# Newer ffmpeg reads an option's value from a file with -/option; older ones had -filter_complex_script.
$fromFile = if ((& $ffmpeg -hide_banner -h long 2>&1 | Out-String) -match 'filter_complex_script') { '-filter_complex_script' } else { '-/filter_complex' }

# Video: xfade joins, in passes of $JoinSteps steps. One join of every step (and every voice) at
# once ran ffmpeg out of memory when little was free; each pass starts from the video so far.
$part = $null; $partLen = 0.0; $n = 0; $pass = 0
$videoFile = Join-Path $Work 'video.mp4'
while ($n -lt $count) {
    $a = @('-hide_banner', '-loglevel', 'error', '-y'); $f = @(); $i = 0; $prev = $null; $len = 0.0
    if ($part) { $a += @('-threads', '1', '-i', $part); $f += '[0:v]settb=AVTB,setpts=PTS-STARTPTS[p]'; $prev = 'p'; $len = $partLen; $i = 1 }
    $end = [Math]::Min($count, $n + $JoinSteps)
    for ($m = $n; $m -lt $end; $m++) {
        $a += $vIn[$m]
        $f += "[${i}:v]fps=30,format=yuv420p,setsar=1,trim=0:$(Num $durs[$m]),settb=AVTB,setpts=PTS-STARTPTS[v$m]"
        if (-not $prev) { $prev = "v$m"; $len = $durs[$m] }
        else {
            $f += "[$prev][v$m]xfade=transition=$($joinType[$m]):duration=$(Num $joinLen[$m]):offset=$(Num ($len - $joinLen[$m]))[x$m]"
            $prev = "x$m"; $len += $durs[$m] - $joinLen[$m]
        }
        $i++
    }
    $last = $end -eq $count
    if ($last) { $f += "[$prev]fade=t=in:st=0:d=$(Num $fadeInFirst),fade=t=out:st=$(Num ($total - 1.2)):d=1.2[vout]"; $out = $videoFile }
    else { $f += "[$prev]null[vout]"; $out = Join-Path $Work ('part{0}.mp4' -f $pass) }
    $script = Join-Path $Work ('filter_v{0}.txt' -f $pass)
    [IO.File]::WriteAllText($script, ($f -join ";`n"))
    # The passes between keep a high quality (crf 12); the last is the standard's crf 18.
    $q = if ($last) { @('-preset', 'medium', '-crf', '18') } else { @('-preset', 'veryfast', '-crf', '12') }
    $a += @($fromFile, $script, '-map', '[vout]', '-an', '-t', (Num $len), '-c:v', 'libx264') + $q + @('-tune', 'stillimage', '-r', '30', '-threads', '4', $out)
    & $ffmpeg @a
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed (video, pass $pass)" }
    if ($part) { Remove-Item -LiteralPath $part -Force }
    $part = $out; $partLen = $len; $n = $end; $pass++
}

# Audio: the voices, each delayed to its place, joined with the same overlaps; the music under it.
$f = @()
$mix = $null
if (-not $NoVoice) {
    for ($n = 0; $n -lt $count; $n++) {
        $ms = [int]($leads[$n] * 1000)
        $f += "[$($voiceIdx[$n]):a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,adelay=$ms|$ms,apad,atrim=0:$(Num $durs[$n]),asetpts=PTS-STARTPTS[a$n]"
    }
    $aPrev = 'a0'
    for ($k = 1; $k -lt $count; $k++) {
        $f += "[$aPrev][a$k]acrossfade=d=$(Num $joinLen[$k]):c1=tri:c2=tri[xa$k]"
        $aPrev = "xa$k"
    }
    $mix = $aPrev
}
if ($musicOn) {
    # The music alone at $MusicVolume, then down to $MusicUnderVoice over the second before the
    # narration starts (where it would start, without a voice), and there for the rest.
    $t1 = $leads[0]; $t0 = [Math]::Max(0.0, $t1 - 1.0)
    $hi = Num $MusicVolume; $lo = Num $MusicUnderVoice
    $level = "if(lt(t,$(Num $t0)),$hi,if(lt(t,$(Num $t1)),$hi-($hi-$lo)*(t-$(Num $t0))/$(Num ($t1 - $t0)),$lo))"
    $f += "[$($musicIdx):a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,atrim=0:$(Num $total),asetpts=PTS-STARTPTS,volume='$level':eval=frame,afade=t=in:st=0:d=2,afade=t=out:st=$(Num ($total - 3)):d=3[music]"
    if ($mix) {
        $f += "[$mix][music]amix=inputs=2:duration=first:normalize=0[amixed]"
        $mix = 'amixed'
    }
    else { $mix = 'music' }
}
if (-not $mix) { $f += "anullsrc=r=48000:cl=stereo,atrim=0:$(Num $total)[silence]"; $mix = 'silence' }
$f += "[$mix]afade=t=out:st=$(Num ($total - 1.2)):d=1.2[aout]"
$script = Join-Path $Work 'filter_a.txt'
[IO.File]::WriteAllText($script, ($f -join ";`n"))
$audioFile = Join-Path $Work 'audio.m4a'
$a = @('-hide_banner', '-loglevel', 'error', '-y') + $aIn + @($fromFile, $script, '-map', '[aout]', '-t', (Num $total), '-c:a', 'aac', '-b:a', '160k', $audioFile)
& $ffmpeg @a
if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed (audio)' }

# The two together.
$suffix = $(if ($Zoom) { '_zoom' } else { '' }) + $(if ($NoVoice) { '_novoice' } else { '' })
$outFile = Join-Path $here ("StyleGuide_Demo_{0}{1}.mp4" -f $Lang.ToUpper(), $suffix)
& $ffmpeg -hide_banner -loglevel error -y -i $videoFile -i $audioFile -map 0:v -map 1:a -c copy -t (Num $total) -movflags +faststart $outFile
if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed (joining picture and sound)' }
"written $outFile ($(Num $total)s)"

# The YouTube description (the standard, section 12), with each step's question ('q' in
# narration.json) at the time its step is fully on screen in this video: the end of the join into
# it, rounded up. Only for the voiced video, whose times differ from the test's. English is
# youtubetext.txt (the user's name for it), other languages youtubetext_<lang>.txt.
if (-not $NoVoice) {
    $yt = $narr.youtube.$Lang
    if (-not $yt) { "narration.json has no youtube text for '$Lang': no youtubetext written" }
    else {
        $lines = @($yt.about, '', $yt.download, $yt.link, '')
        $at = $durs[0]
        for ($k = 1; $k -lt $count; $k++) {
            $q = (Text $order[$k]).q
            if ($q) { $sec = [int][Math]::Ceiling($at); $lines += ('{0}:{1:D2} {2}' -f [int][Math]::Floor($sec / 60), ($sec % 60), $q) }
            $at += $durs[$k] - $joinLen[$k]
        }
        $lines += ''
        $lines += @($yt.credits)
        $ytFile = Join-Path $here $(if ($Lang -eq 'en') { 'youtubetext.txt' } else { "youtubetext_$Lang.txt" })
        [IO.File]::WriteAllText($ytFile, (($lines -join "`r`n") + "`r`n"), (New-Object Text.UTF8Encoding $true))
        "written $ytFile"
    }
}
