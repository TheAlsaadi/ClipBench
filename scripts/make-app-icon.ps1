# Draws the ClipBench icon (a clipboard on a blue tile) at several sizes and writes one .ico file whose images are PNGs.
# Run it again to change the picture: powershell -File scripts/make-app-icon.ps1
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\src\ClipBench.App\Assets\ClipBench.ico'),
    [string]$PreviewPng = ''
)

Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$blue = [System.Drawing.Color]::FromArgb(255, 15, 108, 189)
$paper = [System.Drawing.Color]::White
$clip = [System.Drawing.Color]::FromArgb(255, 205, 224, 245)
$line = [System.Drawing.Color]::FromArgb(255, 15, 108, 189)

function New-RoundedRectangle([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-FramePng([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 64.0

    function Fill($color, $x, $y, $w, $h, $r) {
        $brush = New-Object System.Drawing.SolidBrush $color
        $path = New-RoundedRectangle ($x * $s) ($y * $s) ($w * $s) ($h * $s) ($r * $s)
        $g.FillPath($brush, $path)
        $brush.Dispose(); $path.Dispose()
    }

    Fill $blue 2 2 60 60 13          # the tile
    Fill $paper 15 13 34 42 4        # the sheet
    Fill $clip 23 8 18 12 3          # the clip
    Fill $line 21 28 22 3.5 1.7      # three lines of text
    Fill $line 21 36 22 3.5 1.7
    Fill $line 21 44 14 3.5 1.7

    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($PreviewPng -ne '' -and $size -eq 256) { $bitmap.Save($PreviewPng, [System.Drawing.Imaging.ImageFormat]::Png) }
    $bitmap.Dispose()
    return $stream.ToArray()
}

$frames = foreach ($size in $sizes) { , @($size, (New-FramePng $size)) }

$directory = Split-Path -Parent $Out
New-Item -ItemType Directory -Force $directory | Out-Null
$file = [System.IO.File]::Create($Out)
$writer = New-Object System.IO.BinaryWriter $file
try {
    $writer.Write([uint16]0)               # reserved
    $writer.Write([uint16]1)               # type: icon
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
        $size = $frame[0]; $png = $frame[1]
        $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))   # width (0 means 256)
        $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))   # height
        $writer.Write([byte]0)             # colors in the palette
        $writer.Write([byte]0)             # reserved
        $writer.Write([uint16]1)           # color planes
        $writer.Write([uint16]32)          # bits per pixel
        $writer.Write([uint32]$png.Length)
        $writer.Write([uint32]$offset)
        $offset += $png.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Output ("Wrote {0} ({1} images, {2} bytes)" -f (Resolve-Path $Out), $frames.Count, (Get-Item $Out).Length)
