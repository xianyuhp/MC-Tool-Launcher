Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace Win -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
'@

$exe = $args[0]
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 12

$AE = [System.Windows.Automation.AutomationElement]
$root = $AE::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
if (-not $win) { 'window not found'; exit 1 }
[Win.Native]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle) | Out-Null
Start-Sleep -Milliseconds 800

function NavTo($index) {
    $navCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
    $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $navCond) | Out-Null
}

$navCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $navCond)[6].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 5

function GetEdits {
    $editCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    return @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond))
}
function Val($e) { try { return $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { return '' } }

function TypeInto($edit, $text, $label) {
    $r = $edit.Current.BoundingRectangle
    $x = [int]($r.X + $r.Width / 2); $y = [int]($r.Y + $r.Height / 2)
    [Win.Native]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 150
    [Win.Native]::mouse_event(2, 0, 0, 0, 0)
    [Win.Native]::mouse_event(4, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 250
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    Start-Sleep -Milliseconds 100
    [System.Windows.Forms.SendKeys]::SendWait($text)
    Start-Sleep -Milliseconds 700
    "$label value=[$(Val $edit)]"
}

function SelectTab($name) {
    $tabCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    $tab = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
    if ($tab) { $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Seconds 2; return $true }
    return $false
}

# ---- GIVE ----
$e = GetEdits
'GIVE edits=' + $e.Count
TypeInto $e[2] '12' '  数量'
TypeInto $e[3] 'TestName' '  名称'
$e = GetEdits
'  输出=' + (Val $e[$e.Count - 1])

# ---- SUMMON ----
if (SelectTab 'summon 生物') {
    $e = GetEdits
    'SUMMON edits=' + $e.Count
    for ($i = 0; $i -lt $e.Count; $i++) { $r = $e[$i].Current.BoundingRectangle; "  [$i] ($([int]$r.X),$([int]$r.Y)) $([int]$r.Width)x$([int]$r.Height) val=[$(Val $e[$i])]" }
    if ($e.Count -gt 2) { TypeInto $e[2] '100' '  生命值' }
    $e = GetEdits
    '  输出=' + (Val $e[$e.Count - 1])
}

# ---- CATALOG ----
if (SelectTab '全部指令') {
    $e = GetEdits
    'CATALOG edits=' + $e.Count
    for ($i = 0; $i -lt $e.Count; $i++) { $r = $e[$i].Current.BoundingRectangle; "  [$i] ($([int]$r.X),$([int]$r.Y)) $([int]$r.Width)x$([int]$r.Height) val=[$(Val $e[$i])]" }
    if ($e.Count -gt 1) { TypeInto $e[1] 'abc123' '  字段1' }
    $e = GetEdits
    '  输出=' + (Val $e[$e.Count - 1])
}

$p.Refresh()
"alive=$(-not $p.HasExited)"
Stop-Process -Id $p.Id -Force
