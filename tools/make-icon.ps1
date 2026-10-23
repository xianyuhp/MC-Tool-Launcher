# Build the MCTL icon: transparent background with the letters "MCT" (pixel font,
# integer nearest-neighbour upscale, packed into a multi-size ICO with PNG frames).
Add-Type -AssemblyName System.Drawing

$letters = @('M', 'C', 'T')
$glyphs = @{
  'M' = @('101', '111', '111', '101', '101', '101', '101')
  'C' = @('111', '100', '100', '100', '100', '100', '111')
  'T' = @('111', '010', '010', '010', '010', '010', '010')
}

$gw = 3
$gh = 7
$gap = 1
$baseW = $letters.Count * $gw + ($letters.Count - 1) * $gap
$ink = [System.Drawing.Color]::FromArgb(255, 63, 111, 232)

$base = New-Object System.Drawing.Bitmap $baseW, $gh
for ($i = 0; $i -lt $letters.Count; $i++) {
  $rows = $glyphs[$letters[$i]]
  for ($y = 0; $y -lt $gh; $y++) {
    for ($x = 0; $x -lt $gw; $x++) {
      if ($rows[$y][$x] -eq '1') {
        $base.SetPixel($i * ($gw + $gap) + $x, $y, $ink)
      }
    }
  }
}

$sizes = @(16, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($size in $sizes) {
  # Integer scale only, so the pixel edges stay crisp.
  $scale = [Math]::Max(1, [Math]::Floor($size / $baseW))
  $w = $baseW * $scale
  $h = $gh * $scale
  $left = [Math]::Floor(($size - $w) / 2)
  $top = [Math]::Floor(($size - $h) / 2)

  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
  $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
  $g.DrawImage($base, (New-Object System.Drawing.Rectangle $left, $top, $w, $h))
  $g.Dispose()

  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += , @($size, $ms.ToArray())
  $bmp.Dispose()
}
$base.Dispose()

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0)             # reserved
$bw.Write([uint16]1)             # type = icon
$bw.Write([uint16]$sizes.Count)  # image count

$offset = 6 + 16 * $sizes.Count
foreach ($item in $pngs) {
  $size = $item[0]
  $bytes = $item[1]
  $bw.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
  $bw.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
  $bw.Write([byte]0)    # palette
  $bw.Write([byte]0)    # reserved
  $bw.Write([uint16]1)  # planes
  $bw.Write([uint16]32) # bpp
  $bw.Write([uint32]$bytes.Length)
  $bw.Write([uint32]$offset)
  $offset += $bytes.Length
}
foreach ($item in $pngs) { $bw.Write($item[1]) }
$bw.Flush()

$target = Join-Path $PSScriptRoot "..\src\MCTL\app.ico"
[System.IO.File]::WriteAllBytes((Resolve-Path (Split-Path $target -Parent)).Path + "\app.ico", $out.ToArray())
$bw.Dispose()
"saved: $target ($((Get-Item $target).Length) bytes)"
