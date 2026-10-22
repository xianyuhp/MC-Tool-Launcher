Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$exe = $args[0]
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 11
$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
$textCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$texts = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond) | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
"subtitle: " + (($texts | Where-Object { $_ -match 'Forge|1\.20\.1' }) -join ' | ')
$itemCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$items = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCond) | ForEach-Object { $_.Current.Name })
"items: " + (($items | Where-Object { $_ -notmatch 'INFO' }) -join ' | ')
Stop-Process -Id $p.Id -Force
