Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = $args[0]
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 14
$AE = [System.Windows.Automation.AutomationElement]
$win = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)))
if (-not $win) { 'window not found'; exit 1 }

function SelectRadio($name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    $el = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $el) { return $false }
    $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    return $true
}

# 导航到下载页（第 2 个导航项）
$navCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $navCond)[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 6

function ListCount {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::List)
    return @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)).Count
}

"download page lists = $(ListCount)  (mod 列表)"
$texts = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))))
foreach ($t in $texts) { if ($t.Current.Name -like '*当前客户端*' -or $t.Current.Name -like '*保存到*') { $t.Current.Name } }

"选择 原版 = $(SelectRadio '原版')"
Start-Sleep -Seconds 3
$lists = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::List))))
"原版页 lists = $($lists.Count)"

"选择 光影 = $(SelectRadio '光影')"
Start-Sleep -Seconds 4
$lists2 = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::List))))
"光影页 lists = $($lists2.Count)"
foreach ($t in $texts) { }

"选择 材质包 = $(SelectRadio '材质包')"
Start-Sleep -Seconds 4
$texts2 = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))))
foreach ($t in $texts2) { if ($t.Current.Name -like '热门*') { $t.Current.Name } }

"alive=$(-not $p.HasExited)"
Stop-Process -Id $p.Id -Force
