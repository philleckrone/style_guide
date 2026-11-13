<<<<<<< HEAD
﻿<#
.SYNOPSIS
  UI Automation smoke test of the Style Guide window, run through the test host on a copy of a
  project folder. It waits for the survey and takes a screenshot. It selects the "question" row
  and types an answer, ticks Reviewed and saves. Then it checks the answers file and closes
  the window.

  The mouse is never moved and nothing is typed: tree selection goes through UI Automation,
  button presses and text are posted as window messages to the test window only.

.EXAMPLE
  .\ui_smoke.ps1 -Project ..\..\TestProject\PDT15 -Out C:\temp\sg
#>
param(
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$ExePath = ''
)
$ErrorActionPreference = 'Stop'
if (-not $ExePath) { $ExePath = Join-Path $PSScriptRoot 'TestHost\bin\Release\StyleGuideTestHost.exe' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SgWin {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, int flags);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    /// <summary>TVM_GETNEXTITEM: 0 root, 1 next sibling, 4 first child.</summary>
    public static IntPtr TreeNext(IntPtr tree, int code, IntPtr item) { return SendMessage(tree, 0x110A, (IntPtr)code, item); }
    /// <summary>TVM_SELECTITEM with TVGN_CARET: WinForms raises BeforeSelect and AfterSelect.</summary>
    public static void TreeSelect(IntPtr tree, IntPtr item) { SendMessage(tree, 0x110B, (IntPtr)9, item); }
    public static void PressButton(IntPtr h) { PostMessage(h, 0xF5, IntPtr.Zero, IntPtr.Zero); }
    public static void SetText(IntPtr h, string text) { SendMessage(h, 0x000C, IntPtr.Zero, text); }
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
    /// <summary>CB_SETCURSEL, then CBN_SELCHANGE to the parent, as a user's choice would send.</summary>
    public static void SelectIndex(IntPtr combo, int i) {
        SendMessage(combo, 0x014E, (IntPtr)i, IntPtr.Zero);
        int id = GetDlgCtrlID(combo);
        SendMessage(GetParent(combo), 0x0111, (IntPtr)((1 << 16) | (id & 0xFFFF)), combo);
    }
}
'@
[SgWin]::SetProcessDPIAware() | Out-Null

New-Item -ItemType Directory -Force $Out | Out-Null
$copy = Join-Path $Out 'project'
if (Test-Path $copy) { Remove-Item -Recurse -Force $copy }
New-Item -ItemType Directory -Force $copy | Out-Null
Get-ChildItem -LiteralPath $Project -File | Where-Object { $_.Extension -notin '.bak', '.html' } | Copy-Item -Destination $copy

$psi = New-Object Diagnostics.ProcessStartInfo $ExePath
$psi.Arguments = "form `"$copy`""
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$proc = [Diagnostics.Process]::Start($psi)

try {
=======
<#
.SYNOPSIS
  UI Automation test of the Send Notes window, run through the test host on copies of a
  consultant notes project and a receiving project (Settings.xml + Notes_*.xml), plus
  tests\fixtures\Notes_Ana Consultora.xml in the source copy. Scenarios:
    1. No target remembered: "(choose a project)" is shown; the target is picked in the window.
    2. The window is closed and opened again: the picked target is still the default.
    3. At ACT 1 two notes are ticked, the first comment of the string at ACT 1:3 is chosen,
       Send + Yes: both arrive in the target (the chosen comment, with its bold), the source
       notes get "Sent to", and they are hidden from the list afterwards.
    4. Go to in Paratext asks for the verse in the receiving project.
    5. SN_MOVE: when Paratext moves to ACT 2, the window follows.
    6. SN_REOPENFILE: choosing the command again reads the notes again (a note added meanwhile
       appears, ticks are kept) and moves to the chapter it was chosen at.
    7. Only verse: the list keeps one verse (a note on ACT 1:4-6 shows at verse 5), Next moves
       by verse, and the choice is kept for the next opening, which follows Paratext to ACT 2:4.
  The mouse is never moved: clicks and combo-box choices are posted to the test window only.

.EXAMPLE
  .\ui_smoke.ps1 -Source "C:\My Paratext 8 Projects\PLe11" -Target "C:\My Paratext 8 Projects\PDT15" -Out C:\temp\sn
#>
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Target,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Lang = 'en',
    [string]$ExePath = ''
)
$ErrorActionPreference = 'Stop'
if (-not $ExePath) { $ExePath = Join-Path $PSScriptRoot 'TestHost\bin\Release\SendNotesTestHost.exe' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class SnWin {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct Pt { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, int flags);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref Pt p);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, StringBuilder l);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
    const int WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, BM_CLICK = 0xF5, WM_COMMAND = 0x111;
    const int CB_GETCOUNT = 0x146, CB_GETLBTEXT = 0x148, CB_GETLBTEXTLEN = 0x149, CB_SETCURSEL = 0x14E, CB_GETCURSEL = 0x147;
    public static void Click(IntPtr h, int sx, int sy) {
        Pt p; p.X = sx; p.Y = sy;
        ScreenToClient(h, ref p);
        IntPtr lp = (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));
        PostMessage(h, WM_LBUTTONDOWN, (IntPtr)1, lp);
        PostMessage(h, WM_LBUTTONUP, IntPtr.Zero, lp);
    }
    public static void PressButton(IntPtr h) { PostMessage(h, BM_CLICK, IntPtr.Zero, IntPtr.Zero); }
    public static string ComboItem(IntPtr h, int i) {
        int n = (int)SendMessage(h, CB_GETLBTEXTLEN, (IntPtr)i, IntPtr.Zero);
        var sb = new StringBuilder(n + 1);
        SendMessage(h, CB_GETLBTEXT, (IntPtr)i, sb);
        return sb.ToString();
    }
    public static int ComboCount(IntPtr h) { return (int)SendMessage(h, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero); }
    public static int ComboSel(IntPtr h) { return (int)SendMessage(h, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero); }
    /// <summary>Selects an item and tells the window, as a user's choice would (CBN_SELCHANGE).</summary>
    public static void ComboSelect(IntPtr h, int i) {
        SendMessage(h, CB_SETCURSEL, (IntPtr)i, IntPtr.Zero);
        int id = GetDlgCtrlID(h);
        SendMessage(GetParent(h), WM_COMMAND, (IntPtr)((1 << 16) | (id & 0xFFFF)), h);
    }
    const int LB_SETCURSEL = 0x186, LB_GETCOUNT = 0x18B;
    public static int ListCount(IntPtr h) { return (int)SendMessage(h, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero); }
    /// <summary>Selects a list-box item and tells the window (LBN_SELCHANGE).</summary>
    public static void ListSelect(IntPtr h, int i) {
        SendMessage(h, LB_SETCURSEL, (IntPtr)i, IntPtr.Zero);
        int id = GetDlgCtrlID(h);
        SendMessage(GetParent(h), WM_COMMAND, (IntPtr)((1 << 16) | (id & 0xFFFF)), h);
    }
}
'@
[SnWin]::SetProcessDPIAware() | Out-Null
>>>>>>> 057390ea1c9eaaf472f51f6516e34b020467b583
$AE = [Windows.Automation.AutomationElement]
$Scope = [Windows.Automation.TreeScope]
$CT = [Windows.Automation.ControlType]
$root = $AE::RootElement
<<<<<<< HEAD
$byProcess = New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $proc.Id)
$win = $null
for ($i = 0; $i -lt 100 -and -not $win; $i++) {
    Start-Sleep -Milliseconds 200
    $win = $root.FindFirst($Scope::Children, $byProcess)
}
if (-not $win) { throw 'window did not appear' }

function Shot($name) {
    $h = [IntPtr]$win.Current.NativeWindowHandle
    $r = New-Object SgWin+Rect
    [SgWin]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [SgWin]::PrintWindow($h, $hdc, 2) | Out-Null
=======
$failures = 0
function Check($name, [bool]$ok, $detail = '') {
    if ($ok) { "PASS $name" } else { "FAIL $name  $detail"; $script:failures++ }
}

# ------------------------------------------------------------ copies
New-Item -ItemType Directory -Force $Out | Out-Null
$src = Join-Path $Out (Split-Path $Source -Leaf)
$dst = Join-Path $Out (Split-Path $Target -Leaf)
foreach ($pair in @(@($Source, $src), @($Target, $dst))) {
    if (Test-Path $pair[1]) { Remove-Item -Recurse -Force $pair[1] }
    New-Item -ItemType Directory -Force $pair[1] | Out-Null
    Get-ChildItem -LiteralPath $pair[0] -File | Where-Object { $_.Name -eq 'Settings.xml' -or $_.Name -like 'Notes_*.xml' } |
        Copy-Item -Destination $pair[1]
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures\Notes_Ana Consultora.xml') -Destination $src
$settings = Join-Path $Out 'settings.xml'
if (Test-Path $settings) { Remove-Item $settings }
$dstName = Split-Path $dst -Leaf

# ------------------------------------------------------------ helpers
$script:proc = $null
$script:win = $null
function Launch($extraEnv) {
    $psi = New-Object Diagnostics.ProcessStartInfo $ExePath
    $psi.Arguments = "form `"$src`" `"$dst`" --lang $Lang --ref ACT 1"
    $psi.EnvironmentVariables['SN_SETTINGS'] = $settings
    $psi.EnvironmentVariables.Remove('SN_MOVE')
    if ($extraEnv) { foreach ($k in $extraEnv.Keys) { $psi.EnvironmentVariables[$k] = $extraEnv[$k] } }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $script:proc = [Diagnostics.Process]::Start($psi)
    $byProcess = New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $script:proc.Id)
    $script:win = $null
    for ($i = 0; $i -lt 100 -and -not $script:win; $i++) {
        Start-Sleep -Milliseconds 200
        $script:win = $root.FindFirst($Scope::Children, $byProcess)
    }
    if (-not $script:win) { throw 'window did not appear' }
    Start-Sleep -Milliseconds 2500
}
function CloseWindow {
    $script:win.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    $script:proc.WaitForExit(10000) | Out-Null
    $script:proc.StandardOutput.ReadToEnd()
}
function Shot($name) {
    $h = [IntPtr]$script:win.Current.NativeWindowHandle
    $r = New-Object SnWin+Rect
    [SnWin]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [SnWin]::PrintWindow($h, $hdc, 2) | Out-Null
>>>>>>> 057390ea1c9eaaf472f51f6516e34b020467b583
    $g.ReleaseHdc($hdc)
    $bmp.Save((Join-Path $Out $name), [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "SHOT $name"
}
<<<<<<< HEAD

function Status {
    @($win.FindAll($Scope::Descendants, (New-Object Windows.Automation.PropertyCondition ($AE::ControlTypeProperty, $CT::Text)))) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ -match 'books read' } | Select-Object -First 1
}

function Press($name) {
    $el = $win.FindFirst($Scope::Descendants, (New-Object Windows.Automation.PropertyCondition ($AE::NameProperty, $name)))
    if (-not $el -or -not $el.Current.NativeWindowHandle) { throw "control '$name' not found" }
    [SgWin]::PressButton([IntPtr]$el.Current.NativeWindowHandle)
}

Start-Sleep -Milliseconds 1500
Shot 'surveying.png'   # the progress line, top right of Rescan, while the survey runs
$status = $null
for ($i = 0; $i -lt 300 -and -not $status; $i++) { Start-Sleep -Milliseconds 200; $status = Status }
if (-not $status) { throw 'the survey did not finish' }
"STATUS $status"
Shot 'first.png'

# Select the question row: the second row of the second section. WinForms tree views reach UI
# Automation as a plain pane, so the row is chosen with tree-view messages.
$all = @($win.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition))
$treeEl = $all | Where-Object { $_.Current.ClassName -match 'SysTreeView32' } | Select-Object -First 1
if (-not $treeEl) { throw 'tree not found' }
$tree = [IntPtr]$treeEl.Current.NativeWindowHandle
$section = [SgWin]::TreeNext($tree, 1, [SgWin]::TreeNext($tree, 0, [IntPtr]::Zero))
$row = [SgWin]::TreeNext($tree, 1, [SgWin]::TreeNext($tree, 4, $section))
if ($row -eq [IntPtr]::Zero) { throw 'question row not found' }
[SgWin]::TreeSelect($tree, $row)
Start-Sleep -Milliseconds 800
Shot 'question.png'

# The answer box is the only EDIT control (the guide is a rich edit box).
$edits = @($win.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.ClassName -match '\.EDIT\.' })
if ($edits.Count -ne 1) { throw "expected one answer box, found $($edits.Count)" }
$answer = 'Questions open with an inverted question mark and end with a question mark (UI test).'
[SgWin]::SetText([IntPtr]$edits[0].Current.NativeWindowHandle, $answer)
Press 'Reviewed'
Start-Sleep -Milliseconds 300
Press 'Save'
Start-Sleep -Milliseconds 800
Shot 'answered.png'

$file = Join-Path $copy 'local\StyleGuide\Punctuation.xml'
if (-not (Test-Path $file)) { throw 'answers were not saved' }
[xml]$x = Get-Content $file -Encoding UTF8
$a = $x.Answers.Answer | Where-Object { $_.id -eq 'sent-question' }
if (-not $a -or $a.'#text' -ne $answer -or $a.state -ne 'reviewed') { throw "saved answer is wrong: $($a.OuterXml)" }
"SAVED $($a.OuterXml)"
$suggested = @($x.Answers.Answer | Where-Object { $_.state -eq 'suggested' }).Count
"SUGGESTED $suggested"

# Footnotes module: the answers of its own file, then the doubtful-verses row (second row of the
# third section, "Types of footnotes").
$combo = @($win.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.ClassName -match '\.COMBOBOX\.' }) | Select-Object -First 1
if (-not $combo) { throw 'module box not found' }
[SgWin]::SelectIndex([IntPtr]$combo.Current.NativeWindowHandle, 1)
$fnStatus = $null
for ($i = 0; $i -lt 150 -and (-not $fnStatus -or $fnStatus -eq $status); $i++) { Start-Sleep -Milliseconds 200; $fnStatus = Status }
if ($fnStatus -eq $status) { throw 'the module did not change' }
"STATUS FOOTNOTES $fnStatus"
$section = [SgWin]::TreeNext($tree, 1, [SgWin]::TreeNext($tree, 1, [SgWin]::TreeNext($tree, 0, [IntPtr]::Zero)))
$row = [SgWin]::TreeNext($tree, 1, [SgWin]::TreeNext($tree, 4, $section))
if ($row -eq [IntPtr]::Zero) { throw 'doubtful-verses row not found' }
[SgWin]::TreeSelect($tree, $row)
Start-Sleep -Milliseconds 800
Shot 'footnotes.png'
$fnFile = Join-Path $copy 'local\StyleGuide\Footnotes.xml'
if (-not (Test-Path $fnFile)) { throw 'footnote suggestions were not saved' }
[xml]$y = Get-Content $fnFile -Encoding UTF8
$d = $y.Answers.Answer | Where-Object { $_.id -eq 'type-doubtful' }
if (-not $d) { throw 'no suggestion for the doubtful verses' }
"DOUBTFUL $($d.'#text')"

# Headings module: the first row of the fourth section ("Section headings"), whose NIV examples
# show the project's headings at those verses.
[SgWin]::SelectIndex([IntPtr]$combo.Current.NativeWindowHandle, 2)
$hdStatus = $null
for ($i = 0; $i -lt 150 -and (-not $hdStatus -or $hdStatus -eq $fnStatus); $i++) { Start-Sleep -Milliseconds 200; $hdStatus = Status }
if ($hdStatus -eq $fnStatus) { throw 'the module did not change to Headings' }
"STATUS HEADINGS $hdStatus"
$section = [SgWin]::TreeNext($tree, 0, [IntPtr]::Zero)
for ($i = 0; $i -lt 3; $i++) { $section = [SgWin]::TreeNext($tree, 1, $section) }
$row = [SgWin]::TreeNext($tree, 4, $section)
if ($row -eq [IntPtr]::Zero) { throw 'section-headings row not found' }
[SgWin]::TreeSelect($tree, $row)
Start-Sleep -Milliseconds 800
Shot 'headings.png'
$hdFile = Join-Path $copy 'local\StyleGuide\Headings.xml'
if (-not (Test-Path $hdFile)) { throw 'heading suggestions were not saved' }
[xml]$z = Get-Content $hdFile -Encoding UTF8
$g = $z.Answers.Answer | Where-Object { $_.id -eq 's-general' }
if (-not $g) { throw 'no suggestion for the section-heading length' }
"HEADINGS $($g.'#text')"

# Hyphenation module: the first row (does the language hyphenate?), answered from the word list.
[SgWin]::SelectIndex([IntPtr]$combo.Current.NativeWindowHandle, 3)
$hyStatus = $null
for ($i = 0; $i -lt 150 -and (-not $hyStatus -or $hyStatus -eq $hdStatus); $i++) { Start-Sleep -Milliseconds 200; $hyStatus = Status }
if ($hyStatus -eq $hdStatus) { throw 'the module did not change to Hyphenation' }
"STATUS HYPHENATION $hyStatus"
$row = [SgWin]::TreeNext($tree, 4, [SgWin]::TreeNext($tree, 0, [IntPtr]::Zero))
if ($row -eq [IntPtr]::Zero) { throw 'hyphenation row not found' }
[SgWin]::TreeSelect($tree, $row)
Start-Sleep -Milliseconds 800
Shot 'hyphenation.png'
$hyFile = Join-Path $copy 'local\StyleGuide\Hyphenation.xml'
if (-not (Test-Path $hyFile)) { throw 'hyphenation suggestions were not saved' }
[xml]$h = Get-Content $hyFile -Encoding UTF8
$l = $h.Answers.Answer | Where-Object { $_.id -eq 'hy-allowed' }
if (-not $l) { throw 'no suggestion for the word list' }
"HYPHENATION $($l.'#text')"

# Lists module: the second row of the second section ("\li1 for every line"), whose NIV examples
# show the project's paragraphs with their markers.
[SgWin]::SelectIndex([IntPtr]$combo.Current.NativeWindowHandle, 4)
$lsStatus = $null
for ($i = 0; $i -lt 150 -and (-not $lsStatus -or $lsStatus -eq $hyStatus); $i++) { Start-Sleep -Milliseconds 200; $lsStatus = Status }
if ($lsStatus -eq $hyStatus) { throw 'the module did not change to Lists' }
"STATUS LISTS $lsStatus"
$section = [SgWin]::TreeNext($tree, 1, [SgWin]::TreeNext($tree, 0, [IntPtr]::Zero))
$row = [SgWin]::TreeNext($tree, 1, [SgWin]::TreeNext($tree, 4, $section))
if ($row -eq [IntPtr]::Zero) { throw 'lists row not found' }
[SgWin]::TreeSelect($tree, $row)
Start-Sleep -Milliseconds 800
Shot 'lists.png'
$lsFile = Join-Path $copy 'local\StyleGuide\Lists.xml'
if (-not (Test-Path $lsFile)) { throw 'list suggestions were not saved' }
[xml]$k = Get-Content $lsFile -Encoding UTF8
$q = $k.Answers.Answer | Where-Object { $_.id -eq 'ls-li1' }
if (-not $q) { throw 'no suggestion for the simple lists' }
"LISTS $($q.'#text')"

# Numbers module: the first row (Paratext's Number settings), filled in label by label.
[SgWin]::SelectIndex([IntPtr]$combo.Current.NativeWindowHandle, 5)
$nmStatus = $null
for ($i = 0; $i -lt 150 -and (-not $nmStatus -or $nmStatus -eq $lsStatus); $i++) { Start-Sleep -Milliseconds 200; $nmStatus = Status }
if ($nmStatus -eq $lsStatus) { throw 'the module did not change to Numbers' }
"STATUS NUMBERS $nmStatus"
$row = [SgWin]::TreeNext($tree, 4, [SgWin]::TreeNext($tree, 0, [IntPtr]::Zero))
if ($row -eq [IntPtr]::Zero) { throw 'numbers row not found' }
[SgWin]::TreeSelect($tree, $row)
Start-Sleep -Milliseconds 800
Shot 'numbers.png'
$nmFile = Join-Path $copy 'local\StyleGuide\Numbers.xml'
if (-not (Test-Path $nmFile)) { throw 'number suggestions were not saved' }
[xml]$n = Get-Content $nmFile -Encoding UTF8
$s = $n.Answers.Answer | Where-Object { $_.id -eq 'nm-number-settings' }
if (-not $s -or $s.'#text' -notmatch 'Decimal point:') { throw 'no suggestion for the number settings' }
"NUMBERS $(($s.'#text' -split "`n")[0])"

# Poetry module: the second row of the third section ("\q1 \q2"), a four-column table.
[SgWin]::SelectIndex([IntPtr]$combo.Current.NativeWindowHandle, 6)
$poStatus = $null
for ($i = 0; $i -lt 150 -and (-not $poStatus -or $poStatus -eq $nmStatus); $i++) { Start-Sleep -Milliseconds 200; $poStatus = Status }
if ($poStatus -eq $nmStatus) { throw 'the module did not change to Poetry' }
"STATUS POETRY $poStatus"
$section = [SgWin]::TreeNext($tree, 0, [IntPtr]::Zero)
for ($i = 0; $i -lt 2; $i++) { $section = [SgWin]::TreeNext($tree, 1, $section) }
$row = [SgWin]::TreeNext($tree, 1, [SgWin]::TreeNext($tree, 4, $section))
if ($row -eq [IntPtr]::Zero) { throw 'poetry row not found' }
[SgWin]::TreeSelect($tree, $row)
Start-Sleep -Milliseconds 800
Shot 'poetry.png'
$poFile = Join-Path $copy 'local\StyleGuide\Poetry.xml'
if (-not (Test-Path $poFile)) { throw 'poetry suggestions were not saved' }
[xml]$o = Get-Content $poFile -Encoding UTF8
$q12 = $o.Answers.Answer | Where-Object { $_.id -eq 'po-q1-q2' }
if (-not $q12) { throw 'no suggestion for the poetry levels' }
"POETRY $(($q12.'#text' -split "`n")[0])"

# Quotations module: the first row of the second section (the quotation mark system).
[SgWin]::SelectIndex([IntPtr]$combo.Current.NativeWindowHandle, 7)
$quStatus = $null
for ($i = 0; $i -lt 150 -and (-not $quStatus -or $quStatus -eq $poStatus); $i++) { Start-Sleep -Milliseconds 200; $quStatus = Status }
if ($quStatus -eq $poStatus) { throw 'the module did not change to Quotations' }
"STATUS QUOTATIONS $quStatus"
$section = [SgWin]::TreeNext($tree, 0, [IntPtr]::Zero)
for ($i = 0; $i -lt 1; $i++) { $section = [SgWin]::TreeNext($tree, 1, $section) }
$row = [SgWin]::TreeNext($tree, 4, $section)
if ($row -eq [IntPtr]::Zero) { throw 'quotations row not found' }
[SgWin]::TreeSelect($tree, $row)
Start-Sleep -Milliseconds 800
Shot 'quotations.png'
$quFile = Join-Path $copy 'local\StyleGuide\Quotations.xml'
if (-not (Test-Path $quFile)) { throw 'quotation suggestions were not saved' }
[xml]$u = Get-Content $quFile -Encoding UTF8
$q1 = $u.Answers.Answer | Where-Object { $_.id -eq 'qu-level1' }
if (-not $q1) { throw 'no suggestion for the quotation system' }
"QUOTATIONS $(($q1.'#text' -split "`n")[0])"

# Syntax module: the first row of the second section (the alphabet).
[SgWin]::SelectIndex([IntPtr]$combo.Current.NativeWindowHandle, 8)
$syStatus = $null
for ($i = 0; $i -lt 150 -and (-not $syStatus -or $syStatus -eq $quStatus); $i++) { Start-Sleep -Milliseconds 200; $syStatus = Status }
if ($syStatus -eq $quStatus) { throw 'the module did not change to Syntax' }
"STATUS SYNTAX $syStatus"
$row = [SgWin]::TreeNext($tree, 4, [SgWin]::TreeNext($tree, 1, [SgWin]::TreeNext($tree, 0, [IntPtr]::Zero)))
if ($row -eq [IntPtr]::Zero) { throw 'syntax row not found' }
[SgWin]::TreeSelect($tree, $row)
Start-Sleep -Milliseconds 800
Shot 'syntax.png'
$syFile = Join-Path $copy 'local\StyleGuide\Syntax.xml'
if (-not (Test-Path $syFile)) { throw 'syntax suggestions were not saved' }
[xml]$y2 = Get-Content $syFile -Encoding UTF8
$al = $y2.Answers.Answer | Where-Object { $_.id -eq 'sy-alphabet' }
if (-not $al) { throw 'no suggestion for the alphabet' }
"SYNTAX $(($al.'#text' -split "`n")[0])"

# Translation Considerations (draft): the names come from the project's Biblical Terms renderings.
[SgWin]::SelectIndex([IntPtr]$combo.Current.NativeWindowHandle, 11)
$trStatus = $null
for ($i = 0; $i -lt 150 -and (-not $trStatus -or $trStatus -eq $syStatus); $i++) { Start-Sleep -Milliseconds 200; $trStatus = Status }
if ($trStatus -eq $syStatus) { throw 'the module did not change to Translation Considerations' }
"STATUS TRANSLATION $trStatus"
$row = [SgWin]::TreeNext($tree, 4, [SgWin]::TreeNext($tree, 0, [IntPtr]::Zero))
[SgWin]::TreeSelect($tree, $row)
Start-Sleep -Milliseconds 800
Shot 'translation.png'
$trFile = Join-Path $copy 'local\StyleGuide\Translation.xml'
if (-not (Test-Path $trFile)) { throw 'translation suggestions were not saved' }
[xml]$tx = Get-Content $trFile -Encoding UTF8
$aaron = $tx.Answers.Answer | Where-Object { $_.id -eq 'tr-name-aaron' }
if (-not $aaron) { throw 'no suggestion for the name Aaron' }
"TRANSLATION Aaron: $(($aaron.'#text' -split "`n")[0])"

# Books: choose the New Testament only. The chooser is modal, a top-level window of the process.
function BooksButton {
    @($win.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) |
        Where-Object { $_.Current.Name -match '^Books: \d+ of \d+' } | Select-Object -First 1
}
$bb = BooksButton
if (-not $bb) { throw 'books button not found' }
"BOOKS BEFORE $($bb.Current.Name)"
[SgWin]::PressButton([IntPtr]$bb.Current.NativeWindowHandle)
$dlg = $null
for ($i = 0; $i -lt 50 -and -not $dlg; $i++) {
    Start-Sleep -Milliseconds 200
    $dlg = @($root.FindAll($Scope::Children, $byProcess)) | Where-Object { $_.Current.Name -eq 'Books to survey' } | Select-Object -First 1
    if (-not $dlg) { $dlg = $win.FindFirst($Scope::Children, (New-Object Windows.Automation.PropertyCondition ($AE::NameProperty, 'Books to survey'))) }
}
if (-not $dlg) { throw 'book chooser did not open' }
$nt = $dlg.FindFirst($Scope::Descendants, (New-Object Windows.Automation.PropertyCondition ($AE::NameProperty, 'New Testament')))
[SgWin]::PressButton([IntPtr]$nt.Current.NativeWindowHandle)
Start-Sleep -Milliseconds 500
Shot 'books.png'
$ok = $dlg.FindFirst($Scope::Descendants, (New-Object Windows.Automation.PropertyCondition ($AE::NameProperty, 'OK')))
[SgWin]::PressButton([IntPtr]$ok.Current.NativeWindowHandle)
$ntStatus = $null
for ($i = 0; $i -lt 300 -and -not ($ntStatus -match '^27 of \d+ books read'); $i++) { Start-Sleep -Milliseconds 200; $ntStatus = Status }
if (-not ($ntStatus -match '^27 of \d+ books read')) { throw "the NT survey did not finish: $ntStatus" }
"STATUS NT $ntStatus"
$bb = BooksButton
if ($bb.Current.Name -notmatch '^Books: 27 of ') { throw "books button says $($bb.Current.Name)" }
"BOOKS AFTER $($bb.Current.Name)"
Shot 'nt.png'
[xml]$st = Get-Content (Join-Path $copy 'local\StyleGuide\settings.xml') -Encoding UTF8
$excluded = @($st.StyleGuideSettings.ExcludedBooks -split ' ' | Where-Object { $_ })
if ($excluded -notcontains 'GEN' -or $excluded -contains 'MAT') { throw "settings.xml is wrong: $($st.OuterXml)" }
"EXCLUDED $($excluded.Count) books"

Press 'Close'
if (-not $proc.WaitForExit(10000)) { $proc.Kill(); throw 'window did not close' }
$proc.StandardOutput.ReadToEnd()

# Opened again, the window keeps the choice.
$proc = [Diagnostics.Process]::Start($psi)
$byProcess = New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $proc.Id)
$win = $null
for ($i = 0; $i -lt 100 -and -not $win; $i++) { Start-Sleep -Milliseconds 200; $win = $root.FindFirst($Scope::Children, $byProcess) }
$again = $null
for ($i = 0; $i -lt 300 -and -not ($again -match '^27 of \d+ books read'); $i++) { Start-Sleep -Milliseconds 200; $again = Status }
if (-not ($again -match '^27 of \d+ books read')) { throw "the choice was not kept: $again" }
"REOPENED $again"
Press 'Close'
if (-not $proc.WaitForExit(10000)) { $proc.Kill(); throw 'window did not close' }
'OK'
}
finally {
    # Never leave the test window open (a failed step would otherwise keep the run waiting).
    if (-not $proc.HasExited) { $proc.Kill() }
}
=======
function All { @($script:win.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) }
function FindByText($pattern) {
    All | Where-Object { $_.Current.Name -match $pattern -and $_.Current.Name -notmatch ' Row ' } | Select-Object -First 1
}
function Combo($name) {
    $c1 = New-Object Windows.Automation.PropertyCondition ($AE::NameProperty, $name)
    $c2 = New-Object Windows.Automation.PropertyCondition ($AE::ControlTypeProperty, $CT::ComboBox)
    $el = $script:win.FindFirst($Scope::Descendants, (New-Object Windows.Automation.AndCondition $c1, $c2))
    if (-not $el) { throw "combo $name not found" }
    [IntPtr]$el.Current.NativeWindowHandle
}
function ComboText($name) { $h = Combo $name; $i = [SnWin]::ComboSel($h); if ($i -ge 0) { [SnWin]::ComboItem($h, $i) } else { '' } }
function Cell($header, $row, $type) {
    $c1 = New-Object Windows.Automation.PropertyCondition ($AE::NameProperty, "$header Row $row")
    $c2 = New-Object Windows.Automation.PropertyCondition ($AE::ControlTypeProperty, $type)
    $script:win.FindFirst($Scope::Descendants, (New-Object Windows.Automation.AndCondition $c1, $c2))
}
function CellValue($el) { $el.GetCurrentPropertyValue([Windows.Automation.ValuePattern]::ValueProperty) }
# The row whose "Comment to send" starts with the text (the fixture's notes share their verses with real ones).
function RowOf($text) {
    for ($r = 0; $r -lt 500; $r++) {
        $c = Cell $colText $r $CT::Edit
        if (-not $c) { return -1 }
        if ((CellValue $c) -like "$text*") { return $r }
    }
    -1
}
function PostClick($el) {
    $r = $el.Current.BoundingRectangle
    $owner = $el
    $walker = [Windows.Automation.TreeWalker]::ControlViewWalker
    while ($owner -and $owner.Current.NativeWindowHandle -eq 0) { $owner = $walker.GetParent($owner) }
    [SnWin]::Click([IntPtr]$owner.Current.NativeWindowHandle, [int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
}
function CountsText {
    (All | Where-Object { $_.Current.ControlType -eq $CT::Text -and $_.Current.Name -match '^ACT \d+' } | Select-Object -First 1).Current.Name
}
function DstComments {
    $f = Join-Path $dst 'Notes_Test User.xml'
    if (-not (Test-Path $f)) { return @() }
    @(([xml](Get-Content -LiteralPath $f -Raw -Encoding UTF8)).CommentList.Comment)
}
function SrcMarkers {
    $f = Join-Path $src 'Notes_Test User.xml'
    if (-not (Test-Path $f)) { return @() }
    @(([xml](Get-Content -LiteralPath $f -Raw -Encoding UTF8)).CommentList.Comment)
}

$es = $Lang -like 'es*'; $fr = $Lang -like 'fr*'
$colRef = if ($es) { 'Referencia' } elseif ($fr) { "R$([char]0xE9)f$([char]0xE9)rence" } else { 'Reference' }
$colText = if ($es) { "Comentario que se enviar$([char]0xE1)" } elseif ($fr) { "Commentaire $([char]0xE0) envoyer" } else { 'Comment to send' }
$colSend = if ($es) { 'Enviar' } elseif ($fr) { 'Envoyer' } else { 'Send' }
$goToText = if ($es) { '^Ir al vers.culo en Paratext$' } elseif ($fr) { '^Aller au verset dans Paratext$' } else { '^Go to in Paratext$' }
$confirmTitle = if ($es) { 'Enviar notas' } elseif ($fr) { 'Envoyer des notes' } else { 'Send notes' }

# ------------------------------------------------------------ 1. pick the target
Launch $null
Shot '1_start.png'
$to = Combo 'SendTo'
Check 'no target remembered: (choose a project)' ((ComboText 'SendTo') -match '^\(') (ComboText 'SendTo')
$pick = -1
for ($i = 0; $i -lt [SnWin]::ComboCount($to); $i++) { if ([SnWin]::ComboItem($to, $i) -like "$dstName*") { $pick = $i } }
if ($pick -lt 0) { throw "$dstName not offered" }
[SnWin]::ComboSelect($to, $pick)
Start-Sleep -Milliseconds 800
Check 'target picked' ((ComboText 'SendTo') -like "$dstName*") (ComboText 'SendTo')
Check 'book/chapter from Paratext' ((ComboText 'Book') -eq 'ACT' -and (ComboText 'Chapter') -eq '1') ((ComboText 'Book') + ' ' + (ComboText 'Chapter'))
CloseWindow | Out-Null

# ------------------------------------------------------------ 2. still the default
Check 'settings saved' ((Test-Path $settings) -and ((Get-Content $settings -Raw) -match '<Target ')) ''
Launch $null
Check 'target remembered after reopening' ((ComboText 'SendTo') -like "$dstName*") (ComboText 'SendTo')

# ------------------------------------------------------------ 3. tick two notes, choose comment 1, send
$r3 = RowOf '3. Nota final'
$r8 = RowOf "$([char]0xBF)Qu$([char]0xE9) palabra usan para"
Check 'fixture rows found' ($r3 -ge 0 -and $r8 -ge 0) "$r3 $r8"
foreach ($r in @($r8, $r3)) {
    (Cell $colSend $r $CT::CheckBox).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 300
}
# Ticking ACT 1:3 last made it the current row: its comments must be in the list below.
# The list box gives UI Automation no items: it is found by window class and driven by messages.
$list = All | Where-Object { $_.Current.ClassName -like '*LISTBOX*' } | Select-Object -First 1
$lh = [IntPtr]$list.Current.NativeWindowHandle
Check 'comment list shows the 3 comments' ([SnWin]::ListCount($lh) -eq 3) "$([SnWin]::ListCount($lh))"
[SnWin]::ListSelect($lh, 0)
Start-Sleep -Milliseconds 500
$preview = (All | Where-Object { $_.Current.ClassName -like '*EDIT*' -and $_.Current.Name -like 'ACT 1:*' } | Select-Object -First 1).Current.Name
Check 'preview = ACT 1:3, comment 1' ($preview -like 'ACT 1:3*Primer borrador*') $preview
$text3 = CellValue (Cell $colText $r3 $CT::Edit)
Check 'row shows comment 1' ($text3 -like '1. Primer borrador*') $text3
Shot '3_chosen.png'
$send = FindByText '\(2\)$'
if (-not $send) { Shot '3_tick_failed.png'; throw 'Send button does not show 2 ticked notes' }
[SnWin]::PressButton([IntPtr]$send.Current.NativeWindowHandle)
$byTitle = New-Object Windows.Automation.PropertyCondition ($AE::NameProperty, $confirmTitle)
$dialog = $null
for ($i = 0; $i -lt 30 -and -not $dialog; $i++) {
    Start-Sleep -Milliseconds 200
    $dialog = $root.FindFirst($Scope::Children, (New-Object Windows.Automation.AndCondition (New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $script:proc.Id)), $byTitle))
    if (-not $dialog) { $dialog = $script:win.FindFirst($Scope::Children, $byTitle) }
}
if (-not $dialog) { throw 'confirmation dialog did not appear' }
$yes = @($dialog.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) |
    Where-Object { $_.Current.Name -match "^&?(Yes|S$([char]0xED)|Oui)$" } | Select-Object -First 1
[SnWin]::PressButton([IntPtr]$yes.Current.NativeWindowHandle)
Start-Sleep -Milliseconds 2000
Shot '3_after_send.png'
$sent = DstComments
Check 'two notes arrived' ($sent.Count -eq 2) "$($sent.Count)"
$n3 = $sent | Where-Object { $_.VerseRef -eq 'ACT 1:3' }
Check 'ACT 1:3 = comment 1, bold kept' ($n3 -and $n3.Contents.InnerXml -match '<bold>cuarenta d') $(if ($n3) { $n3.Contents.InnerXml })
$marks = SrcMarkers
Check 'two Sent markers in the source' ($marks.Count -eq 2 -and ($marks | Where-Object { $_.Contents -match $dstName }).Count -eq 2) "$($marks.Count)"
Check 'sent notes hidden afterwards' ((RowOf '1. Primer borrador') -lt 0 -and (RowOf "$([char]0xBF)Qu$([char]0xE9) palabra usan para") -lt 0 -and (CountsText) -match ' 2 not') (CountsText)

# ------------------------------------------------------------ 4. go to
PostClick (Cell $colRef 0 $CT::Edit)
Start-Sleep -Milliseconds 300
$ref0 = CellValue (Cell $colRef 0 $CT::Edit)
[SnWin]::PressButton([IntPtr](FindByText $goToText).Current.NativeWindowHandle)
Start-Sleep -Milliseconds 500
$outText = CloseWindow
$goto = ($outText -split "`r?`n" | Where-Object { $_ -like 'GOTO*' }) -join ''
Check 'go to: verse in the receiving project' ($goto -match [regex]::Escape("$dstName $ref0")) $goto

# ------------------------------------------------------------ 5. follow Paratext
Launch @{ 'SN_MOVE' = 'ACT 2' }
Start-Sleep -Milliseconds 1500
Check 'follows Paratext to ACT 2' ((ComboText 'Book') -eq 'ACT' -and (ComboText 'Chapter') -eq '2') ((ComboText 'Book') + ' ' + (ComboText 'Chapter'))
Shot '5_follow.png'
CloseWindow | Out-Null

# ------------------------------------------------------------ 6. opening the tool again refreshes
$reopen = Join-Path $Out 'reopen.txt'
if (Test-Path $reopen) { Remove-Item $reopen }
Launch @{ 'SN_REOPENFILE' = $reopen }
function ShownCount { if ((CountsText) -match '^ACT \d+ ?: (\d+)') { [int]$Matches[1] } else { -1 } }
$n0 = ShownCount
(Cell $colSend 0 $CT::CheckBox).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
Start-Sleep -Milliseconds 400
# A note added in Paratext meanwhile (by the test user, at ACT 1:2).
$notesFile = Join-Path $src 'Notes_Test User.xml'
$doc = New-Object Xml.XmlDocument
$doc.PreserveWhitespace = $true
$doc.Load($notesFile)
$new = $doc.DocumentElement.SelectSingleNode('Comment').CloneNode($true)
$new.SetAttribute('Thread', 'fx000099')
$new.SetAttribute('VerseRef', 'ACT 1:2')
$new.SelectSingleNode('Contents').InnerText = 'Nota agregada en Paratext'
$doc.DocumentElement.AppendChild($new) | Out-Null
$doc.Save($notesFile)
Set-Content -LiteralPath $reopen -Value "$(Split-Path $src -Leaf) ACT 1"
Start-Sleep -Milliseconds 2000
Check 'reopen: the new note is listed' ((ShownCount) -eq $n0 + 1 -and (RowOf 'Nota agregada en Paratext') -ge 0) "$n0 -> $(ShownCount)"
$tick0 = (Cell $colSend 0 $CT::CheckBox).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState
Check 'reopen: the tick is kept' ($tick0 -eq 'On' -and (FindByText '\(1\)$')) "$tick0"
Shot '6_refreshed.png'
Set-Content -LiteralPath $reopen -Value "$(Split-Path $src -Leaf) ACT 3"
Start-Sleep -Milliseconds 2000
Check "reopen from ACT 3: moves there" ((ComboText 'Book') -eq 'ACT' -and (ComboText 'Chapter') -eq '3') ((ComboText 'Book') + ' ' + (ComboText 'Chapter'))
CloseWindow | Out-Null

# ------------------------------------------------------------ 7. only one verse
# Every listed note must be at the verse (a note on a range counts for each of its verses).
function AllRefsAt($verse) {
    $refs = @()
    for ($r = 0; $r -lt 300; $r++) {
        $c = Cell $colRef $r $CT::Edit
        if (-not $c) { break }
        $refs += (CellValue $c)
    }
    ($refs.Count -gt 0) -and -not ($refs | Where-Object { $_ -notmatch "^ACT 1:$verse( |$)" -and $_ -notmatch '^ACT 1:4-6' })
}
$onlyVerse = if ($es) { '^Solo el vers.culo:$' } elseif ($fr) { '^Seulement le verset :$' } else { '^Only verse:$' }
Launch $null
[SnWin]::PressButton([IntPtr](FindByText $onlyVerse).Current.NativeWindowHandle)
Start-Sleep -Milliseconds 800
Check 'only verse: ACT 1:1 only' ((CountsText) -match '^ACT 1:1 ?:' -and (AllRefsAt 1)) (CountsText)
[SnWin]::ComboSelect((Combo 'Verse'), 2)
Start-Sleep -Milliseconds 800
Check 'only verse: verse 3 chosen' ((CountsText) -match '^ACT 1:3 ?:' -and (AllRefsAt 3)) (CountsText)
# A note on a range of verses (ACT 1:4-6) is listed at verse 5.
$doc = New-Object Xml.XmlDocument
$doc.PreserveWhitespace = $true
$doc.Load($notesFile)
$new = $doc.DocumentElement.SelectSingleNode('Comment').CloneNode($true)
$new.SetAttribute('Thread', 'fx000100')
$new.SetAttribute('VerseRef', 'ACT 1:4-6')
$new.SelectSingleNode('Contents').InnerText = 'Nota sobre un rango de versos'
$doc.DocumentElement.AppendChild($new) | Out-Null
$doc.Save($notesFile)
[SnWin]::ComboSelect((Combo 'Verse'), 4)
Start-Sleep -Milliseconds 800
Check 'only verse: a range note shows at verse 5' ((RowOf 'Nota sobre un rango') -ge 0 -and (CountsText) -match '^ACT 1:5 ?:') (CountsText)
Shot '7_verse.png'
[SnWin]::PressButton([IntPtr](FindByText '>$').Current.NativeWindowHandle)
Start-Sleep -Milliseconds 800
Check 'only verse: Next goes to verse 6' ((ComboText 'Verse') -eq '6' -and (CountsText) -match '^ACT 1:6 ?:') (CountsText)
CloseWindow | Out-Null
Launch @{ 'SN_MOVE' = 'ACT 2 4' }
Start-Sleep -Milliseconds 1500
Check 'only verse: remembered, follows Paratext to ACT 2:4' ((ComboText 'Chapter') -eq '2' -and (ComboText 'Verse') -eq '4' -and (CountsText) -match '^ACT 2:4 ?:') (CountsText)
CloseWindow | Out-Null

"$failures failure(s)"
>>>>>>> 057390ea1c9eaaf472f51f6516e34b020467b583
