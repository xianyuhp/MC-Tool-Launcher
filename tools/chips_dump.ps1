Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = $args[0]
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 11
$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
if ($null -eq $win) { 'no window'; exit 1 }

$radioCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$navs = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)
$navs[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 9

$radios = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)
for ($i = 0; $i -lt $radios.Count; $i++) { "R$i = $($radios[$i].Current.Name)" }
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$buttons = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
for ($i = 0; $i -lt $buttons.Count; $i++) { "B$i = $($buttons[$i].Current.Name)" }

$itemCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$items = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCond)
"items: $($items.Count)"
for ($i = 0; $i -lt [Math]::Min(4, $items.Count); $i++) { "I$i = $($items[$i].Current.Name)" }
Stop-Process -Id $p.Id -Force
