Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$exe = $args[0]
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 10
$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
$radioCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$navs = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)
$navs[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 14

$itemCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$items = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCond) | ForEach-Object { $_.Current.Name })
"items: $($items.Count)"
"first: " + (($items | Select-Object -First 5) -join ' || ')

$textCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$texts = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond) | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
"detail: " + (($texts | Where-Object { $_ -match 'AppID|¥|\$|史低|折|版|折扣' }) -join ' | ')

$p.Refresh()
"alive=$(-not $p.HasExited)"
Stop-Process -Id $p.Id -Force
