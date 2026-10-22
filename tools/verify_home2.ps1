Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = $args[0]
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 12
$AE = [System.Windows.Automation.AutomationElement]
$win = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)))
if (-not $win) { 'window not found'; exit 1 }

function ListCount {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::List)
    return @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)).Count
}
function FindToggle($name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    return $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

"初始 lists = $(ListCount)  （日志默认关闭，应为 0）"
$log = FindToggle '运行日志'
if (-not $log) { 'log toggle not found'; Stop-Process -Id $p.Id -Force; exit 1 }
$log.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
Start-Sleep -Seconds 2
"打开日志后 lists = $(ListCount)  （应为 1）"
$log.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
Start-Sleep -Seconds 2
"关闭日志后 lists = $(ListCount)  （应为 0）"

# 版本列表按钮
$btnCond = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
    (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, '版本列表')))
$btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
if ($btn) {
    $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    "打开版本面板后 lists = $(ListCount)  （应为 1：版本列表）"
} else { 'version button not found' }
"alive=$(-not $p.HasExited)"
Stop-Process -Id $p.Id -Force
