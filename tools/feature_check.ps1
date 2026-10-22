Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = $args[0]
$musicDir = Join-Path (Split-Path $exe) 'Music'
New-Item -ItemType Directory -Force -Path $musicDir | Out-Null

# 生成 2 秒 440Hz 测试音频
$wav = Join-Path $musicDir 'demo.wav'
$fs = [System.IO.File]::Create($wav)
$bw = New-Object System.IO.BinaryWriter($fs)
$sampleRate = 22050; $seconds = 2; $samples = $sampleRate * $seconds
$bw.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $bw.Write([int](36 + $samples * 2))
$bw.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $bw.Write([int]16); $bw.Write([int16]1)
$bw.Write([int16]1); $bw.Write([int]$sampleRate); $bw.Write([int]($sampleRate * 2))
$bw.Write([int16]2); $bw.Write([int16]16); $bw.Write([Text.Encoding]::ASCII.GetBytes('data'))
$bw.Write([int]($samples * 2))
for ($i = 0; $i -lt $samples; $i++) {
    $v = [int16](8000 * [math]::Sin(2 * [math]::PI * 440 * $i / $sampleRate))
    $bw.Write($v)
}
$bw.Close(); $fs.Close()
"wav created: $((Get-Item $wav).Length) bytes"

$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 9

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if ($null -eq $win) { 'window not found'; Stop-Process -Id $p.Id -Force; exit 1 }

function All-Texts {
    $textCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $result = @()
    foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) {
        if ($t.Current.Name) { $result += $t.Current.Name }
    }
    return $result
}

$texts = All-Texts
"music track visible: $([bool]($texts | Where-Object { $_ -eq 'demo' }))"
"music hint: " + (($texts | Where-Object { $_ -like '*Music*' -or $_ -like '*demo*' }) -join ' | ')

# 切到 Steam 页
$navCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$navs = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $navCond)
$navs[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1500

$editCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$edits = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)
"steam edits: $($edits.Count)"
if ($edits.Count -gt 0) {
    $edits[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('portal')
    Start-Sleep -Milliseconds 400
}

$buttonCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$buttons = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCond)
"buttons: " + (($buttons | ForEach-Object { $_.Current.Name }) -join ',')
if ($buttons.Count -gt 6) {
    $buttons[6].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
Start-Sleep -Seconds 10

$listCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$items = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listCond)
"list items: $($items.Count)"
$names = @()
foreach ($i in $items) { if ($i.Current.Name) { $names += $i.Current.Name } }
"first items: " + (($names | Select-Object -First 4) -join ' || ')

$texts = All-Texts
"price texts: " + (($texts | Where-Object { $_ -match '¥|\$|史低|AppID|价格' }) -join ' | ')

$p.Refresh()
"alive=$(-not $p.HasExited) responding=$($p.Responding)"
Stop-Process -Id $p.Id -Force
Remove-Item $wav -Force -ErrorAction SilentlyContinue
