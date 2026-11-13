<#
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
$AE = [Windows.Automation.AutomationElement]
$Scope = [Windows.Automation.TreeScope]
$CT = [Windows.Automation.ControlType]
$root = $AE::RootElement
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
    $g.ReleaseHdc($hdc)
    $bmp.Save((Join-Path $Out $name), [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "SHOT $name"
}

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
