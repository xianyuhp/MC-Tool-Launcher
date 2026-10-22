Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = $args[0]
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 9

$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
if ($null -eq $win) { 'window not found'; Stop-Process -Id $p.Id -Force; exit 1 }
"window: $($win.Current.Name)"

$radioType = [System.Windows.Automation.ControlType]::RadioButton
$navCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $radioType)
$navs = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $navCond)
"radio buttons: $($navs.Count)"
$navs[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1600

$editType = [System.Windows.Automation.ControlType]::Edit
$editCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $editType)
$edits = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)
"edits on page: $($edits.Count)"
if ($edits.Count -gt 0) {
    $edits[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('portal')
    Start-Sleep -Milliseconds 300
}

$btnType = [System.Windows.Automation.ControlType]::Button
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $btnType)
$buttons = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
"buttons: " + (($buttons | ForEach-Object { $_.Current.Name }) -join '|')
$buttons[7].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 9

$itemType = [System.Windows.Automation.ControlType]::ListItem
$itemCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $itemType)
$items = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCond)
$names = @()
foreach ($i in $items) { if ($i.Current.Name) { $names += $i.Current.Name } }
"list items: $($items.Count)"
"first items: " + (($names | Select-Object -First 3) -join ' || ')

$textType = [System.Windows.Automation.ControlType]::Text
$textCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $textType)
$texts = @()
foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) { if ($t.Current.Name) { $texts += $t.Current.Name } }
"price/low texts: " + (($texts | Where-Object { $_ -match 'AppID|Price|price|low|Low|CNY|USD' }) -join ' | ')
"status texts: " + (($texts | Select-Object -Last 8) -join ' | ')

$p.Refresh()
"alive=$(-not $p.HasExited) responding=$($p.Responding)"
Stop-Process -Id $p.Id -Force

