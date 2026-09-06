<#
.SYNOPSIS
    Turns an SVG, PNG or existing icon into a proper multi-size Windows .ico.

.DESCRIPTION
    Wrapped apps look wrong with a generic icon, and the favicon a site serves is
    usually 16 or 32 pixels, which is far too small for the taskbar or Explorer.
    Most companies publish their app logo as an SVG, and an SVG can be rendered
    crisply at any size, so that is the best source to start from.

    The .ico that comes out holds 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixel
    images, which is what Windows asks for in its various places.

    SVG support is a focused subset, not a full renderer: viewBox, path, rect,
    circle, ellipse, polygon, polyline, line, g (with transform), and linear or
    radial gradients. That covers brand and product logos, which is what this is
    for. Anything with text, filters, clip paths or embedded images will not come
    out right - export it to a large PNG first and pass that instead.

.PARAMETER Source
    An .svg, .png, .jpg, .bmp or .ico file, or an http(s) URL to one.

.PARAMETER Preset
    Shortcut for a Microsoft 365 product logo, taken from Microsoft's own Fluent
    brand-icon CDN. Run with -ListPresets to see the names.

.PARAMETER ListPresets
    Print the known preset names and exit.

.PARAMETER Out
    Where to write the .ico file.

.PARAMETER Sizes
    Which pixel sizes to put in the file. The default set suits Windows.

.PARAMETER Padding
    Fraction of the canvas to leave empty around the artwork, 0 to 0.4. Some logos
    are drawn edge to edge and look cramped as an app icon; 0.06 gives them room.

.PARAMETER Background
    Optional flat background colour, for example "#2564CF" or "white". Useful when
    a logo is a plain dark shape that would vanish on a dark taskbar.

.EXAMPLE
    # Microsoft To Do, from Microsoft's own Fluent brand-icon CDN
    .\tools\make-app-icon.ps1 `
        -Source 'https://res.cdn.office.net/files/fabric-cdn-prod_20230815.001/assets/brand-icons/product/svg/todo_48x1.svg' `
        -Out .\icons\mstodo.ico

.EXAMPLE
    .\tools\make-app-icon.ps1 -Source .\logo.png -Out .\icons\myapp.ico -Padding 0.08
#>
[CmdletBinding()]
param(
    [string]$Source,
    [string]$Preset,
    [switch]$ListPresets,
    [string]$Out,
    [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256),
    [ValidateRange(0.0, 0.4)][double]$Padding = 0.0,
    [string]$Background = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Microsoft publishes its product logos as SVG on the Fluent brand-icon CDN. These
# are the names that answer on it; a 404 means Microsoft never put that one there.
$CdnBase = 'https://res.cdn.office.net/files/fabric-cdn-prod_20230815.001/assets/brand-icons/product/svg'
$Presets = [ordered]@{
    todo       = 'todo'
    outlook    = 'outlook'
    teams      = 'teams'
    onenote    = 'onenote'
    excel      = 'excel'
    word       = 'word'
    powerpoint = 'powerpoint'
    onedrive   = 'onedrive'
    sharepoint = 'sharepoint'
    access     = 'access'
    visio      = 'visio'
    project    = 'project'
    forms      = 'forms'
    sway       = 'sway'
    stream     = 'stream'
    delve      = 'delve'
    yammer     = 'yammer'
    loop       = 'loop'
}

if ($ListPresets) {
    Write-Host 'Presets (Microsoft Fluent brand-icon CDN):'
    foreach ($name in $Presets.Keys) { Write-Host "  $name" }
    Write-Host ''
    Write-Host 'Example:  .\tools\make-app-icon.ps1 -Preset todo -Out .\icons\mstodo.ico'
    exit 0
}

if ($Preset) {
    if ($Source) { throw 'Give either -Source or -Preset, not both.' }
    if (-not $Presets.Contains($Preset.ToLowerInvariant())) {
        throw "Unknown preset '$Preset'. Run with -ListPresets to see the names."
    }
    $Source = "$CdnBase/$($Presets[$Preset.ToLowerInvariant()])_48x1.svg"
}

if (-not $Source) { throw 'Give a -Source file or URL, or a -Preset name. -ListPresets shows the names.' }
if (-not $Out) { throw 'Give an -Out path for the .ico file.' }

# WPF rasterises the SVG, and it wants a single-threaded apartment. Windows
# PowerShell is STA already; pwsh is not, so hand the work to an STA process.
if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    Write-Verbose 'Not on an STA thread, re-running under powershell.exe -STA'
    $arguments = @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Padding', $Padding)
    if ($ListPresets) { $arguments += '-ListPresets' }
    if ($Source) { $arguments += @('-Source', $Source) }
    if ($Preset) { $arguments += @('-Preset', $Preset) }
    if ($Out) { $arguments += @('-Out', $Out) }
    if ($Background) { $arguments += @('-Background', $Background) }
    if ($Sizes) { $arguments += @('-Sizes', ($Sizes -join ',')) }
    & powershell.exe @arguments
    exit $LASTEXITCODE
}

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Drawing
. (Join-Path $PSScriptRoot 'lib\IcoWriter.ps1')

# ---------------------------------------------------------------- source file

function Resolve-Source([string]$source) {
    if ($source -notmatch '^https?://') {
        if (-not (Test-Path $source)) { throw "Source file not found: $source" }
        return (Resolve-Path $source).Path
    }

    Write-Host "Downloading $source"
    $extension = [System.IO.Path]::GetExtension(($source -split '\?')[0])
    if (-not $extension) { $extension = '.svg' }
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ("app-icon-" + [guid]::NewGuid().ToString('N').Substring(0, 8) + $extension)

    # Some Microsoft CDNs answer 417 when the Expect header is sent, so use
    # HttpClient rather than Invoke-WebRequest.
    Add-Type -AssemblyName System.Net.Http
    $client = New-Object System.Net.Http.HttpClient
    try {
        $client.Timeout = [TimeSpan]::FromSeconds(60)
        $client.DefaultRequestHeaders.Add('User-Agent', 'win-webapp-shield/1.0 (make-app-icon)')
        $bytes = $client.GetByteArrayAsync($source).GetAwaiter().GetResult()
        if ($bytes.Length -lt 32) { throw "The download was only $($bytes.Length) bytes, that cannot be an image." }
        [System.IO.File]::WriteAllBytes($temp, $bytes)
    }
    finally {
        $client.Dispose()
    }
    Write-Host "  $($bytes.Length) bytes -> $temp"
    return $temp
}

# ---------------------------------------------------------------- SVG parsing

function Convert-SvgColor([string]$value, [double]$opacity, [hashtable]$gradients) {
    if (-not $value) { return $null }
    $value = $value.Trim()
    if ($value -eq 'none' -or $value -eq 'transparent') { return $null }

    if ($value -match '^url\(#(.+)\)$') {
        $id = $Matches[1]
        if (-not $gradients.ContainsKey($id)) {
            Write-Warning "Gradient '$id' was not found, falling back to grey."
            return New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Colors]::Gray)
        }
        $brush = $gradients[$id].Clone()
        $brush.Opacity = $opacity
        return $brush
    }

    try {
        $color = [System.Windows.Media.ColorConverter]::ConvertFromString($value)
    }
    catch {
        Write-Warning "Could not read the colour '$value', using black."
        $color = [System.Windows.Media.Colors]::Black
    }
    $solid = New-Object System.Windows.Media.SolidColorBrush($color)
    $solid.Opacity = $opacity
    return $solid
}

function Get-SvgAttr($node, [string]$name, $fallback = $null) {
    $attribute = $node.Attributes[$name]
    if ($null -eq $attribute) { return $fallback }
    return $attribute.Value
}

function Get-SvgNumber($node, [string]$name, [double]$fallback) {
    $raw = Get-SvgAttr $node $name $null
    if ($null -eq $raw) { return $fallback }
    $raw = $raw -replace '(px|pt|%)$', ''
    $parsed = 0.0
    if ([double]::TryParse($raw, [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        return $parsed
    }
    return $fallback
}

function Read-SvgGradients($root, [System.Xml.XmlNamespaceManager]$ns) {
    $gradients = @{}

    foreach ($node in $root.SelectNodes('//svg:linearGradient', $ns)) {
        $id = Get-SvgAttr $node 'id' ''
        if (-not $id) { continue }
        $brush = New-Object System.Windows.Media.LinearGradientBrush
        $brush.StartPoint = New-Object System.Windows.Point((Get-SvgNumber $node 'x1' 0), (Get-SvgNumber $node 'y1' 0))
        $brush.EndPoint = New-Object System.Windows.Point((Get-SvgNumber $node 'x2' 1), (Get-SvgNumber $node 'y2' 0))
        $brush.MappingMode = if ((Get-SvgAttr $node 'gradientUnits' 'objectBoundingBox') -eq 'userSpaceOnUse') {
            [System.Windows.Media.BrushMappingMode]::Absolute
        }
        else {
            [System.Windows.Media.BrushMappingMode]::RelativeToBoundingBox
        }
        Add-SvgStops $node $ns $brush
        $gradients[$id] = $brush
    }

    foreach ($node in $root.SelectNodes('//svg:radialGradient', $ns)) {
        $id = Get-SvgAttr $node 'id' ''
        if (-not $id) { continue }
        $brush = New-Object System.Windows.Media.RadialGradientBrush
        $brush.Center = New-Object System.Windows.Point((Get-SvgNumber $node 'cx' 0.5), (Get-SvgNumber $node 'cy' 0.5))
        $brush.GradientOrigin = $brush.Center
        $radius = Get-SvgNumber $node 'r' 0.5
        $brush.RadiusX = $radius
        $brush.RadiusY = $radius
        $brush.MappingMode = if ((Get-SvgAttr $node 'gradientUnits' 'objectBoundingBox') -eq 'userSpaceOnUse') {
            [System.Windows.Media.BrushMappingMode]::Absolute
        }
        else {
            [System.Windows.Media.BrushMappingMode]::RelativeToBoundingBox
        }
        Add-SvgStops $node $ns $brush
        $gradients[$id] = $brush
    }

    return $gradients
}

function Add-SvgStops($node, [System.Xml.XmlNamespaceManager]$ns, $brush) {
    foreach ($stop in $node.SelectNodes('svg:stop', $ns)) {
        $offset = Get-SvgNumber $stop 'offset' 0
        $colorText = Get-SvgAttr $stop 'stop-color' '#000000'
        $stopOpacity = Get-SvgNumber $stop 'stop-opacity' 1
        try { $color = [System.Windows.Media.ColorConverter]::ConvertFromString($colorText) }
        catch { $color = [System.Windows.Media.Colors]::Black }
        if ($stopOpacity -lt 1) {
            $color = [System.Windows.Media.Color]::FromArgb([byte](255 * $stopOpacity), $color.R, $color.G, $color.B)
        }
        $brush.GradientStops.Add((New-Object System.Windows.Media.GradientStop($color, $offset)))
    }
    if ($brush.GradientStops.Count -eq 0) {
        $brush.GradientStops.Add((New-Object System.Windows.Media.GradientStop([System.Windows.Media.Colors]::Gray, 0)))
    }
}

function ConvertTo-Geometry($node, [string]$localName) {
    switch ($localName) {
        'path' {
            $d = Get-SvgAttr $node 'd' ''
            if (-not $d) { return $null }
            # WPF's path mini-language is a superset of the SVG path syntax.
            return [System.Windows.Media.Geometry]::Parse($d)
        }
        'rect' {
            $x = Get-SvgNumber $node 'x' 0
            $y = Get-SvgNumber $node 'y' 0
            $w = Get-SvgNumber $node 'width' 0
            $h = Get-SvgNumber $node 'height' 0
            if ($w -le 0 -or $h -le 0) { return $null }
            $rx = Get-SvgNumber $node 'rx' 0
            $ry = Get-SvgNumber $node 'ry' $rx
            $rect = New-Object System.Windows.Rect($x, $y, $w, $h)
            return New-Object System.Windows.Media.RectangleGeometry($rect, $rx, $ry)
        }
        'circle' {
            $r = Get-SvgNumber $node 'r' 0
            if ($r -le 0) { return $null }
            $center = New-Object System.Windows.Point((Get-SvgNumber $node 'cx' 0), (Get-SvgNumber $node 'cy' 0))
            return New-Object System.Windows.Media.EllipseGeometry($center, $r, $r)
        }
        'ellipse' {
            $rx = Get-SvgNumber $node 'rx' 0
            $ry = Get-SvgNumber $node 'ry' 0
            if ($rx -le 0 -or $ry -le 0) { return $null }
            $center = New-Object System.Windows.Point((Get-SvgNumber $node 'cx' 0), (Get-SvgNumber $node 'cy' 0))
            return New-Object System.Windows.Media.EllipseGeometry($center, $rx, $ry)
        }
        { $_ -in @('polygon', 'polyline') } {
            $points = Get-SvgAttr $node 'points' ''
            if (-not $points) { return $null }
            $numbers = [regex]::Matches($points, '-?\d*\.?\d+(?:[eE][-+]?\d+)?') | ForEach-Object { [double]$_.Value }
            if ($numbers.Count -lt 4) { return $null }
            $sb = New-Object System.Text.StringBuilder
            for ($i = 0; $i + 1 -lt $numbers.Count; $i += 2) {
                $verb = if ($i -eq 0) { 'M' } else { 'L' }
                $null = $sb.Append("$verb$($numbers[$i]),$($numbers[$i+1]) ")
            }
            if ($localName -eq 'polygon') { $null = $sb.Append('Z') }
            return [System.Windows.Media.Geometry]::Parse($sb.ToString())
        }
        'line' {
            $x1 = Get-SvgNumber $node 'x1' 0; $y1 = Get-SvgNumber $node 'y1' 0
            $x2 = Get-SvgNumber $node 'x2' 0; $y2 = Get-SvgNumber $node 'y2' 0
            return [System.Windows.Media.Geometry]::Parse("M$x1,$y1 L$x2,$y2")
        }
        default { return $null }
    }
}

function ConvertTo-Transform([string]$value) {
    if (-not $value) { return $null }
    $group = New-Object System.Windows.Media.TransformGroup

    # Order matters and the two systems disagree. In SVG, transform="translate(...)
    # rotate(...)" means rotate the element first and then translate it - the list
    # reads right to left. WPF applies a TransformGroup left to right. So the list is
    # walked backwards here; without that, "translate(24 40) rotate(45)" would rotate
    # the already-moved shape about the origin and fling it off the canvas.
    $matches = @([regex]::Matches($value, '(\w+)\s*\(([^)]*)\)'))
    [Array]::Reverse($matches)

    foreach ($match in $matches) {
        $kind = $match.Groups[1].Value
        $numbers = @([regex]::Matches($match.Groups[2].Value, '-?\d*\.?\d+(?:[eE][-+]?\d+)?') | ForEach-Object { [double]$_.Value })
        switch ($kind) {
            'translate' {
                $ty = if ($numbers.Count -gt 1) { $numbers[1] } else { 0 }
                $group.Children.Add((New-Object System.Windows.Media.TranslateTransform($numbers[0], $ty)))
            }
            'scale' {
                $sy = if ($numbers.Count -gt 1) { $numbers[1] } else { $numbers[0] }
                $group.Children.Add((New-Object System.Windows.Media.ScaleTransform($numbers[0], $sy)))
            }
            'rotate' {
                if ($numbers.Count -ge 3) {
                    $group.Children.Add((New-Object System.Windows.Media.RotateTransform($numbers[0], $numbers[1], $numbers[2])))
                }
                else {
                    $group.Children.Add((New-Object System.Windows.Media.RotateTransform($numbers[0])))
                }
            }
            'matrix' {
                if ($numbers.Count -ge 6) {
                    $matrix = New-Object System.Windows.Media.Matrix($numbers[0], $numbers[1], $numbers[2], $numbers[3], $numbers[4], $numbers[5])
                    $group.Children.Add((New-Object System.Windows.Media.MatrixTransform($matrix)))
                }
            }
            default { Write-Warning "Ignoring unsupported transform '$kind'." }
        }
    }
    if ($group.Children.Count -eq 0) { return $null }
    return $group
}

function Add-SvgShapes($parent, [System.Xml.XmlNamespaceManager]$ns, $context, [hashtable]$gradients,
    [string]$inheritedFill, [double]$inheritedOpacity) {

    foreach ($node in $parent.ChildNodes) {
        if ($node.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        $localName = $node.LocalName

        # <defs> only declares gradients, and <title>/<desc> are text
        if ($localName -in @('defs', 'title', 'desc', 'metadata', 'style')) { continue }

        $fill = Get-SvgAttr $node 'fill' $inheritedFill
        $opacity = $inheritedOpacity * (Get-SvgNumber $node 'opacity' 1) * (Get-SvgNumber $node 'fill-opacity' 1)
        $transform = ConvertTo-Transform (Get-SvgAttr $node 'transform' '')

        if ($transform) { $context.PushTransform($transform) }
        try {
            if ($localName -eq 'g') {
                Add-SvgShapes $node $ns $context $gradients $fill $opacity
                continue
            }

            $geometry = ConvertTo-Geometry $node $localName
            if ($null -eq $geometry) { continue }

            $brush = Convert-SvgColor $fill $opacity $gradients
            if ($null -eq $brush) { continue }     # fill="none" is a spacer, skip it

            $context.DrawGeometry($brush, $null, $geometry)
        }
        finally {
            if ($transform) { $context.Pop() }
        }
    }
}

function Convert-SvgToPng([string]$svgPath, [int]$size, [double]$padding, [string]$background) {
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $false
    $xml.Load($svgPath)

    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace('svg', 'http://www.w3.org/2000/svg')

    $svg = $xml.DocumentElement
    if ($svg.LocalName -ne 'svg') { throw "$svgPath does not look like an SVG file." }

    # Work out the user-space box the artwork lives in
    $viewBox = Get-SvgAttr $svg 'viewBox' ''
    if ($viewBox) {
        $parts = @([regex]::Matches($viewBox, '-?\d*\.?\d+(?:[eE][-+]?\d+)?') | ForEach-Object { [double]$_.Value })
        if ($parts.Count -lt 4) { throw "Could not read the viewBox '$viewBox'." }
        $minX = $parts[0]; $minY = $parts[1]; $boxW = $parts[2]; $boxH = $parts[3]
    }
    else {
        $minX = 0; $minY = 0
        $boxW = Get-SvgNumber $svg 'width' 0
        $boxH = Get-SvgNumber $svg 'height' 0
        if ($boxW -le 0 -or $boxH -le 0) { throw 'The SVG has neither a viewBox nor a width and height.' }
    }

    $gradients = Read-SvgGradients $xml $ns

    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    try {
        if ($background) {
            $color = [System.Windows.Media.ColorConverter]::ConvertFromString($background)
            $fillBrush = New-Object System.Windows.Media.SolidColorBrush($color)
            $whole = New-Object System.Windows.Rect(0, 0, $size, $size)
            $context.DrawRectangle($fillBrush, $null, $whole)
        }

        # Fit the box into the square, keeping the aspect ratio, with the padding
        # left empty around it.
        $inner = $size * (1.0 - 2.0 * $padding)
        $scale = [Math]::Min($inner / $boxW, $inner / $boxH)
        $offsetX = ($size - $boxW * $scale) / 2.0
        $offsetY = ($size - $boxH * $scale) / 2.0

        $context.PushTransform((New-Object System.Windows.Media.TranslateTransform($offsetX, $offsetY)))
        $context.PushTransform((New-Object System.Windows.Media.ScaleTransform($scale, $scale)))
        $context.PushTransform((New-Object System.Windows.Media.TranslateTransform(-$minX, -$minY)))

        Add-SvgShapes $svg $ns $context $gradients 'black' 1.0

        $context.Pop(); $context.Pop(); $context.Pop()
    }
    finally {
        $context.Close()
    }

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    try {
        $encoder.Save($stream)
        # The leading comma stops PowerShell unrolling the byte[] into the pipeline.
        return ,$stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

# ---------------------------------------------------------------- raster input

function Convert-RasterToPng([string]$imagePath, [int]$size, [double]$padding, [string]$background) {
    $source = if ([System.IO.Path]::GetExtension($imagePath) -eq '.ico') {
        Get-IcoLargestBitmap -Path $imagePath
    }
    else {
        [System.Drawing.Image]::FromFile($imagePath)
    }

    try {
        $canvas = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($canvas)
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.Clear([System.Drawing.Color]::Transparent)

            if ($background) {
                $color = [System.Drawing.ColorTranslator]::FromHtml($background)
                $brush = New-Object System.Drawing.SolidBrush($color)
                $graphics.FillRectangle($brush, 0, 0, $size, $size)
                $brush.Dispose()
            }

            $inner = $size * (1.0 - 2.0 * $padding)
            $scale = [Math]::Min($inner / $source.Width, $inner / $source.Height)
            $w = $source.Width * $scale
            $h = $source.Height * $scale
            $graphics.DrawImage($source, [float](($size - $w) / 2), [float](($size - $h) / 2), [float]$w, [float]$h)
        }
        finally {
            $graphics.Dispose()
        }

        $stream = New-Object System.IO.MemoryStream
        try {
            $canvas.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return ,$stream.ToArray()
        }
        finally {
            $stream.Dispose()
            $canvas.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }
}

# ---------------------------------------------------------------------- main

$resolved = Resolve-Source $Source
$extension = [System.IO.Path]::GetExtension($resolved).ToLowerInvariant()
$isSvg = $extension -eq '.svg'

if (-not $isSvg -and $extension -notin @('.png', '.jpg', '.jpeg', '.bmp', '.gif', '.ico')) {
    throw "Unsupported source type '$extension'. Use .svg, .png, .jpg, .bmp, .gif or .ico."
}

if (-not $isSvg) {
    # warn when the source is too small to fill the biggest size cleanly
    $probe = if ($extension -eq '.ico') {
        Get-IcoLargestBitmap -Path $resolved
    }
    else {
        [System.Drawing.Image]::FromFile($resolved)
    }
    $smallest = [Math]::Min($probe.Width, $probe.Height)
    $probe.Dispose()
    $largest = ($Sizes | Measure-Object -Maximum).Maximum
    if ($smallest -lt $largest) {
        Write-Warning "The source is only ${smallest}px, so the ${largest}px entry will be an upscale and will look soft. An SVG source avoids this."
    }
}

$entries = @()
foreach ($size in ($Sizes | Sort-Object -Unique)) {
    $bytes = if ($isSvg) {
        Convert-SvgToPng $resolved $size $Padding $Background
    }
    else {
        Convert-RasterToPng $resolved $size $Padding $Background
    }
    $entries += , @{ Size = $size; Bytes = $bytes }
    Write-Host ("  {0,3}x{1,-3} {2,6} bytes" -f $size, $size, $bytes.Length)
}

$null = Write-IcoFromPngs -Entries $entries -Path $Out
$final = (Resolve-Path $Out).Path

# Read the file back and check every entry really decodes, so a broken icon is
# reported here instead of quietly turning into a blank square later.
Write-Host ''
Write-Host 'Verifying:'
Test-IcoFile -Path $final | ForEach-Object { Write-Host $_ }

$size = (Get-Item $final).Length
Write-Host ''
Write-Host ("Wrote {0}  ({1:N1} KB, {2} sizes, all verified)" -f $final, ($size / 1KB), $entries.Count) -ForegroundColor Green
