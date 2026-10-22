Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = $args[0]
$shot = $args[1]

$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 10
$p.Refresh()
"started pid=$($p.Id) exited=$($p.HasExited) title=$($p.MainWindowTitle)"

# 截图
$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$bmp.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()

# 简单像素统计：中心区域的平均亮度与颜色多样性
$colors = New-Object 'System.Collections.Generic.HashSet[int]'
$sum = 0L; $count = 0
for ($x = [int]($bounds.Width * 0.3); $x -lt $bounds.Width * 0.7; $x += 17) {
    for ($y = [int]($bounds.Height * 0.3); $y -lt $bounds.Height * 0.7; $y += 17) {
        $c = $bmp.GetPixel($x, $y)
        [void]$colors.Add(($c.R -shl 16) -bor ($c.G -shl 8) -bor $c.B)
        $sum += ($c.R + $c.G + $c.B) / 3
        $count++
    }
}
$bmp.Dispose()
"screenshot: $($bounds.Width)x$($bounds.Height) samples=$count uniqueColors=$($colors.Count) meanBrightness=$([math]::Round($sum / [math]::Max(1,$count),1))"

# UI 自动化：点击左侧导航
try {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
    $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if ($null -eq $win) { "UIA: window not found"; } else {
        "UIA window: $($win.Current.Name)"
        $rbCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
        $buttons = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rbCond)
        "UIA radio buttons: $($buttons.Count)"
        foreach ($b in $buttons) {
            $name = $b.Current.Name
            try {
                $pattern = $b.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                $pattern.Select()
                Start-Sleep -Milliseconds 1200
                $p.Refresh()
                "  selected [$name] -> alive=$(-not $p.HasExited)"
            } catch {
                "  FAIL select [$name]: $($_.Exception.Message)"
            }
        }
    }
} catch {
    "UIA error: $($_.Exception.Message)"
}

Start-Sleep -Seconds 1
$p.Refresh()
"final: exited=$($p.HasExited) responding=$($p.Responding)"
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; "killed" }
