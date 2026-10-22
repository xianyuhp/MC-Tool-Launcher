Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace Win -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
'@

$exe = $args[0]
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 12
$AE = [System.Windows.Automation.AutomationElement]
$win = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)))
if (-not $win) { 'window not found'; exit 1 }
[Win.Native]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle) | Out-Null
Start-Sleep -Milliseconds 800

function FindBtn($name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    return $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

$toggle = FindBtn '版本列表*'
if (-not $toggle) {
    $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    foreach ($b in $all) { if ($b.Current.Name -like '版本列表*') { $toggle = $b; break } }
}
if (-not $toggle) { 'version button not found'; Stop-Process -Id $p.Id -Force; exit 1 }
"button = $($toggle.Current.Name)"
$toggle.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 2

$refresh = FindBtn '刷新'
"panel open (刷新 button found) = $([bool]$refresh)"
$wr = $win.Current.BoundingRectangle
$listCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::List)
$lists = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listCond))
"lists = $($lists.Count)"
foreach ($l in $lists) {
    $r = $l.Current.BoundingRectangle
    "  list rect=($([int]$r.X),$([int]$r.Y)) $([int]$r.Width)x$([int]$r.Height)  (window right = $([int]($wr.X + $wr.Width)))"
}

# Esc 关闭
[Win.Native]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle) | Out-Null
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1
$refresh2 = FindBtn '刷新'
"after ESC (刷新 button found) = $([bool]$refresh2)"

# 指令页输入
$navCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $navCond)[6].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 5
$editCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$edits = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond))
$count = $edits[2]
# 用 UIA 直接写入数值（不依赖窗口是否在前台），验证生成逻辑同步
try {
    $count.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('64')
    $edits[3].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Excalibur')
} catch { "set failed: $($_.Exception.Message)" }
Start-Sleep -Milliseconds 900
$v = ''
try { $v = $count.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { }
"count = [$v]"
$out = ''
try { $out = $edits[$edits.Count - 1].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { }
"output = $out"

Stop-Process -Id $p.Id -Force
