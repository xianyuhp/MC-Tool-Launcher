Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = $args[0]
$target = $args[1]

function StartApp {
    $p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
    Start-Sleep -Seconds 13
    $AE = [System.Windows.Automation.AutomationElement]
    $win = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)))
    return @{ Process = $p; Window = $win; AE = $AE }
}

function SelectedVersion($ctx) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($ctx.AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $items = @($ctx.Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond))
    foreach ($it in $items) {
        try {
            if ($it.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) {
                return $it.Current.Name
            }
        } catch { }
    }
    return '(none)'
}

$ctx = StartApp
"首次启动选中 = $(SelectedVersion $ctx)"

# 选中目标版本
$cond = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($ctx.AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)),
    (New-Object System.Windows.Automation.PropertyCondition($ctx.AE::NameProperty, $target)))
$item = $ctx.Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
if ($item) {
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 1
    "选择后 = $(SelectedVersion $ctx)"
} else { "未找到 $target" }

Stop-Process -Id $ctx.Process.Id -Force
Start-Sleep -Seconds 2

# 读取配置
$cfg = Get-Content (Join-Path (Split-Path $exe) 'config\settings.json') -Raw | ConvertFrom-Json
"配置保存 = $($cfg.SelectedVersionId)"

# 重新启动
$ctx2 = StartApp
"重启后选中 = $(SelectedVersion $ctx2)"
Stop-Process -Id $ctx2.Process.Id -Force
