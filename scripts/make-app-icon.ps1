# Makes the ClipBench icons from the logo, docs/images/clipbench-logo.svg:
#   src/ClipBench.App/Assets/ClipBench.ico            the app and tray icon (PNG images from 16 to 256 pixels)
#   src/ClipBench.App/Assets/ClipBench-incognito.ico  the tray icon while incognito is on: the logo in grey with a slash
#   src/ClipBench.App/Assets/ClipBench-256.png        the logo shown inside the app (Settings)
# Windows has no SVG renderer for scripts, so Microsoft Edge draws the logo once at 1024 pixels, offline and headless,
# from the local file, in a throwaway profile folder. Every size is scaled down from that drawing.
# Run it again after changing the logo: powershell -File scripts/make-app-icon.ps1
param(
    [string]$Svg = (Join-Path $PSScriptRoot '..\docs\images\clipbench-logo.svg'),
    [string]$Assets = (Join-Path $PSScriptRoot '..\src\ClipBench.App\Assets'),
    [string]$Edge = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$master = 1024

if ($Edge -eq '') {
    $Edge = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Edge) { throw 'Microsoft Edge was not found. Pass its path with -Edge.' }

# Draws the SVG at 1024 x 1024 with a transparent background.
function Render-Master([string]$svgPath) {
    $work = Join-Path ([System.IO.Path]::GetTempPath()) ('clipbench-icon-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $work | Out-Null
    try {
        Copy-Item $svgPath (Join-Path $work 'logo.svg')
        $page = '<!doctype html><html><head><style>html,body{margin:0;padding:0;background:transparent;overflow:hidden}' +
            "img{display:block;width:${master}px;height:${master}px}</style></head><body><img src=""logo.svg""></body></html>"
        Set-Content -Encoding utf8 (Join-Path $work 'page.html') $page
        $png = Join-Path $work 'logo.png'
        $arguments = @(
            '--headless=new', '--disable-gpu', '--no-first-run', '--disable-extensions', '--hide-scrollbars',
            '--default-background-color=00000000', '--force-device-scale-factor=1',
            "--user-data-dir=$(Join-Path $work 'profile')", "--window-size=$master,$master", "--screenshot=$png",
            ('file:///' + ((Join-Path $work 'page.html') -replace '\\', '/'))
        )
        $process = Start-Process -FilePath $Edge -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
        if ($process.ExitCode -ne 0 -or -not (Test-Path $png)) { throw "Edge could not draw the logo (exit code $($process.ExitCode))." }

        # Copied into memory, so the folder can be deleted while the picture is still in use.
        $loaded = [System.Drawing.Bitmap]::FromFile($png)
        try { return New-Object System.Drawing.Bitmap $loaded } finally { $loaded.Dispose() }
    }
    finally {
        Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    }
}

# The incognito picture: the logo in a lighter grey, so it reads as switched off, with a white slash edged in that grey.
function New-Incognito([System.Drawing.Bitmap]$logo) {
    $bitmap = New-Object System.Drawing.Bitmap $master, $master, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Grey = 0.27 + 0.73 x luminance, so the dark blue becomes a mid grey and white stays white.
    $k = 0.73; $o = 0.27
    $rows = @(
        [single[]]@((0.299 * $k), (0.299 * $k), (0.299 * $k), 0, 0),
        [single[]]@((0.587 * $k), (0.587 * $k), (0.587 * $k), 0, 0),
        [single[]]@((0.114 * $k), (0.114 * $k), (0.114 * $k), 0, 0),
        [single[]]@(0, 0, 0, 1, 0),
        [single[]]@($o, $o, $o, 0, 1)
    )
    $attributes = New-Object System.Drawing.Imaging.ImageAttributes
    $attributes.SetColorMatrix((New-Object System.Drawing.Imaging.ColorMatrix (, $rows)))
    $g.DrawImage($logo, (New-Object System.Drawing.Rectangle 0, 0, $master, $master), 0, 0, $master, $master, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
    $attributes.Dispose()

    $grey = [int][math]::Round(255 * ($o + $k * (0.299 * 30 + 0.587 * 58 + 0.114 * 138) / 255))
    $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, $grey, $grey, $grey)), 150
    $line = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 80
    foreach ($pen in $edge, $line) {
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLine($pen, 232, 232, 792, 792)
        $pen.Dispose()
    }

    $g.Dispose()
    return $bitmap
}

function Get-Png([System.Drawing.Bitmap]$source, [int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $stream.ToArray()
}

# One .ico file whose images are PNGs, smallest first.
function Write-Ico([System.Drawing.Bitmap]$source, [string]$path) {
    $frames = foreach ($size in $sizes) { , @($size, (Get-Png $source $size)) }
    $file = [System.IO.File]::Create($path)
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
    Write-Output ("Wrote {0} ({1} images, {2} bytes)" -f (Resolve-Path $path), $frames.Count, (Get-Item $path).Length)
}

New-Item -ItemType Directory -Force $Assets | Out-Null
$logo = Render-Master (Resolve-Path $Svg)
$incognito = New-Incognito $logo
try {
    Write-Ico $logo (Join-Path $Assets 'ClipBench.ico')
    Write-Ico $incognito (Join-Path $Assets 'ClipBench-incognito.ico')
    $png = Join-Path $Assets 'ClipBench-256.png'
    [System.IO.File]::WriteAllBytes($png, (Get-Png $logo 256))
    Write-Output ("Wrote {0}" -f (Resolve-Path $png))
}
finally {
    $logo.Dispose()
    $incognito.Dispose()
}
