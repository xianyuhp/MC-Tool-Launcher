Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = $args[0]
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 12

$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
if ($null -eq $win) { 'window not found'; Stop-Process -Id $p.Id -Force; exit 1 }

function Names($type) {
    $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)
    return @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c) | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
}

"== HOME"
"buttons: " + ((Names ([System.Windows.Automation.ControlType]::Button)) -join '|')
$items = Names ([System.Windows.Automation.ControlType]::ListItem)
"list items: $($items.Count) -> " + (($items | Select-Object -First 3) -join ' || ')

$navCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$navs = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $navCond)
$navs[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 7

"== DOWNLOAD"
"buttons: " + ((Names ([System.Windows.Automation.ControlType]::Button)) -join '|')
$radios = Names ([System.Windows.Automation.ControlType]::RadioButton)
"chips: $($radios.Count) -> " + (($radios | Select-Object -Skip 5) -join '|')
$items = Names ([System.Windows.Automation.ControlType]::ListItem)
"list items: $($items.Count)"
"first texts: " + (((Names ([System.Windows.Automation.ControlType]::Text)) | Select-Object -First 12) -join ' | ')

$navs[4].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 2
"== SETTINGS"
"buttons: " + ((Names ([System.Windows.Automation.ControlType]::Button)) -join '|')
"texts: " + (((Names ([System.Windows.Automation.ControlType]::Text)) | Select-Object -Skip 8 -First 14) -join ' | ')

$p.Refresh()
"alive=$(-not $p.HasExited) responding=$($p.Responding)"
Stop-Process -Id $p.Id -Force
