Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = $args[0]
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 11

$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
if ($null -eq $win) { 'window not found'; Stop-Process -Id $p.Id -Force; exit 1 }

$radioCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$textCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$editCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$itemCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)

$navs = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)
$navs[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1200

function Texts {
    $r = @()
    foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) { if ($t.Current.Name) { $r += $t.Current.Name } }
    return $r
}
function Buttons {
    $r = @()
    foreach ($b in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)) { if ($b.Current.Name) { $r += $b.Current.Name } }
    return $r
}

"== HOME"
"buttons: " + ((Buttons) -join '|')
"texts: " + ((Texts | Where-Object { $_ -match 'G$|微软|未登录|离线|版本' }) -join ' | ')

# Download page
$navs[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 5
"== DOWNLOAD"
"buttons: " + ((Buttons) -join '|')
"texts: " + ((Texts | Where-Object { $_ -match '热门|安装到|镜像|Forge|NeoForge' }) -join ' | ')
$items = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCond)
"list items: $($items.Count)"

# Settings page
$navs[4].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 2
"== SETTINGS"
"buttons: " + ((Buttons) -join '|')
"texts: " + ((Texts | Where-Object { $_ -match '游戏目录|下载源|内存|G$|镜像' }) -join ' | ')

# Steam page with search
$navs[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1500
$edits = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)
if ($edits.Count -gt 0) {
    $edits[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('portal')
    Start-Sleep -Milliseconds 300
}
$buttons = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
$buttons[6].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 9
"== STEAM"
"texts: " + ((Texts | Where-Object { $_ -match '史低|国区|¥|\$|AppID' }) -join ' | ')

$p.Refresh()
"alive=$(-not $p.HasExited) responding=$($p.Responding)"
Stop-Process -Id $p.Id -Force
