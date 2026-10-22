Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = $args[0]
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 14
$AE = [System.Windows.Automation.AutomationElement]
$win = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)))
if (-not $win) { 'window not found'; exit 1 }
"title=$($win.Current.Name)"

function FindByName($name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    return $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

$toggle = FindByName '版本列表'
if ($toggle) {
    "toolbar toggle name = $($toggle.Current.Name)"
} else {
    # 已选中版本时名称会带版本号，模糊查找
    $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    foreach ($b in $all) { if ($b.Current.Name -like '版本列表*') { "toggle = $($b.Current.Name)" } }
}

# 切到下载页
$navCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $navCond)[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 4

# 点击「模组」分类
$mod = FindByName '模组'
if ($mod) { $mod.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Seconds 6 }

# 读取所有名称中含「保存到」的文本
$textCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$texts = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond))
foreach ($t in $texts) {
    $n = $t.Current.Name
    if ($n -like '*保存到*' -or $n -like '*筛选*') { $n }
}
"alive=$(-not $p.HasExited)"
Stop-Process -Id $p.Id -Force
