Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = $args[0]
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 11
$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
$radioCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$navs = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)
$navs[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 10

$textCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
function PageLabel {
    $texts = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond) | ForEach-Object { $_.Current.Name })
    return (@($texts | Where-Object { $_ -match '^\d+/\d+$' }) -join ',')
}

"page before: " + (PageLabel)
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$buttons = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
$b = $buttons[14]
"button[14]: name=$($b.Current.Name) enabled=$($b.Current.IsEnabled) bounds=$($b.Current.BoundingRectangle)"
try {
    $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    'invoked ok'
} catch { "invoke failed: $($_.Exception.Message)" }
Start-Sleep -Seconds 8
"page after: " + (PageLabel)
$itemCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$all = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCond) | ForEach-Object { $_.Current.Name })
"mods after: " + ((@($all | Where-Object { $_ -and $_ -notmatch '\d{4}-\d{2}-\d{2}' }) | Select-Object -First 3) -join ' || ')
Stop-Process -Id $p.Id -Force
