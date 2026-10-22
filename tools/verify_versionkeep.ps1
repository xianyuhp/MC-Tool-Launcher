Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = $args[0]
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 13
$AE = [System.Windows.Automation.AutomationElement]
$win = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)))
if (-not $win) { 'window not found'; exit 1 }

function FindButtonLike($prefix) {
    $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    foreach ($b in $all) { if ($b.Current.Name -like "$prefix*") { return $b } }
    return $null
}
function ToggleText { $b = FindButtonLike '版本列表'; if ($b) { $b.Current.Name } else { '(not found)' } }

"初始 = $(ToggleText)"

# 打开版本面板
$btn = FindButtonLike '版本列表'
if ($btn) { $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
Start-Sleep -Seconds 2

# 选择第二个版本
$itemCond = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)),
    (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, '1.20.1-Forge')))
$item = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCond)
if ($item) {
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 1
    "选择后 = $(ToggleText)"
} else { 'list item not found' }

# 关闭面板（Esc）
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace Win2 -Name Native -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);'
[Win2.Native]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle) | Out-Null
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1

# 跳到下载页再回主页
$navCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$navs = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $navCond))
$navs[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 4
$navs = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $navCond))
$navs[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 3
"返回主页后 = $(ToggleText)"

"alive=$(-not $p.HasExited)"
Stop-Process -Id $p.Id -Force
