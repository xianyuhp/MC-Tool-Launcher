Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = $args[0]
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 8

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if ($null -eq $win) { "window not found"; Stop-Process -Id $p.Id -Force; exit 1 }

$navCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$navs = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $navCond)

$textCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$buttonCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$editCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)

for ($i = 0; $i -lt 5; $i++) {
    $target = $navs[$i]
    if ($null -eq $target) { "nav missing at $i"; continue }
    $label = $target.Current.Name
    $pattern = $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
    Start-Sleep -Milliseconds 1500

    $texts = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)
    $names = @()
    foreach ($t in $texts) { if ($t.Current.Name) { $names += $t.Current.Name } }
    $unique = $names | Select-Object -Unique | Select-Object -First 12
    $buttons = ($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCond) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) -join ','
    $edits = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond).Count
    $p.Refresh()
    "== nav[$i] '$label' texts=$($names.Count) edits=$edits alive=$(-not $p.HasExited)"
    "   buttons: $buttons"
    "   texts: " + (($unique | ForEach-Object { $_ -replace "`r?`n", ' / ' }) -join ' | ')
}

$p.Refresh()
"alive=$(-not $p.HasExited) responding=$($p.Responding)"
Stop-Process -Id $p.Id -Force
