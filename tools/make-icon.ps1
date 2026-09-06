# Generates src/WebAppShield/app.ico (and samples/mstodo.ico) with no external tools.
# The .ico holds PNG compressed entries at 16/24/32/48/64/128/256 px, which Windows
# Vista and later understand.

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function New-ShieldBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [double]$size

    # rounded square background, blue gradient
    $pad = $s * 0.04
    $rect = New-Object System.Drawing.RectangleF($pad, $pad, ($s - 2 * $pad), ($s - 2 * $pad))
    $radius = $s * 0.22
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 43, 108, 209),
        [System.Drawing.Color]::FromArgb(255, 17, 58, 128),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($brush, $path)

    # white shield in the middle
    $cx = $s / 2.0
    $top = $s * 0.20
    $bottom = $s * 0.82
    $halfW = $s * 0.215
    $shield = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pts = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    $pts.Add((New-Object System.Drawing.PointF(($cx - $halfW), $top)))
    $pts.Add((New-Object System.Drawing.PointF(($cx + $halfW), $top)))
    $pts.Add((New-Object System.Drawing.PointF(($cx + $halfW), ($top + ($bottom - $top) * 0.45))))
    $pts.Add((New-Object System.Drawing.PointF($cx, $bottom)))
    $pts.Add((New-Object System.Drawing.PointF(($cx - $halfW), ($top + ($bottom - $top) * 0.45))))
    $shield.AddPolygon($pts.ToArray())
    $shield.CloseFigure()
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $g.FillPath($white, $shield)

    # blue "window" bar across the shield, so it reads as a web window
    if ($size -ge 24) {
        $barH = [Math]::Max(1.0, $s * 0.055)
        $barRect = New-Object System.Drawing.RectangleF(($cx - $halfW), ($top + $s * 0.055), (2 * $halfW), $barH)
        $blue = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 26, 74, 158))
        $g.FillRectangle($blue, $barRect)
        $blue.Dispose()
    }

    $white.Dispose(); $brush.Dispose(); $path.Dispose(); $shield.Dispose(); $g.Dispose()
    return $bmp
}

function Write-Ico([string]$outPath, [int[]]$sizes) {
    $pngs = @()
    foreach ($size in $sizes) {
        $bmp = New-ShieldBitmap $size
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs += , @{ size = $size; bytes = $ms.ToArray() }
        $ms.Dispose(); $bmp.Dispose()
    }

    $fs = [System.IO.File]::Create($outPath)
    $bw = New-Object System.IO.BinaryWriter($fs)
    try {
        $bw.Write([uint16]0)               # reserved
        $bw.Write([uint16]1)               # type: icon
        $bw.Write([uint16]$pngs.Count)

        $offset = 6 + 16 * $pngs.Count
        foreach ($p in $pngs) {
            $dim = if ($p.size -ge 256) { 0 } else { $p.size }
            $bw.Write([byte]$dim)          # width
            $bw.Write([byte]$dim)          # height
            $bw.Write([byte]0)             # palette colours
            $bw.Write([byte]0)             # reserved
            $bw.Write([uint16]1)           # colour planes
            $bw.Write([uint16]32)          # bits per pixel
            $bw.Write([uint32]$p.bytes.Length)
            $bw.Write([uint32]$offset)
            $offset += $p.bytes.Length
        }
        foreach ($p in $pngs) { $bw.Write($p.bytes) }
    }
    finally {
        $bw.Dispose(); $fs.Dispose()
    }
    Write-Host "wrote $outPath"
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
Write-Ico (Join-Path $root 'src\WebAppShield\app.ico') $sizes
Write-Ico (Join-Path $root 'samples\app.ico') $sizes
