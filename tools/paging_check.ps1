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

$itemCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
function ModNames {
    $all = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCond) | ForEach-Object { $_.Current.Name })
    return @($all | Where-Object { $_ -and $_ -notmatch '\d{4}-\d{2}-\d{2}' })
}

"BEFORE: " + ((ModNames | Select-Object -First 3) -join ' || ')

$radios = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)
$radios[14].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 7
"AFTER CATEGORY: " + ((ModNames | Select-Object -First 3) -join ' || ')

$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$buttons = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
$buttons[14].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 7
"AFTER NEXT PAGE: " + ((ModNames | Select-Object -First 3) -join ' || ')

$textCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$texts = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond) | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
"page texts: " + (($texts | Where-Object { $_ -match '^\d+/\d+$|共|热门|分类' }) -join ' | ')
$p.Refresh()
"alive=$(-not $p.HasExited)"
Stop-Process -Id $p.Id -Force
