# Writes a real multi-size Windows .ico file. Dot-source this from another script.
#
# Storage per size is chosen the way icon editors choose it:
#
#   <= 48 px   uncompressed 32-bit DIB (BITMAPINFOHEADER + BGRA + AND mask)
#   >  48 px   PNG compressed
#
# Windows itself reads PNG entries at any size from Vista onwards, but plenty of
# other code does not - the .NET Framework's System.Drawing.Icon, for one, throws
# on a PNG entry. The small sizes are the ones everything touches (title bar,
# tray, taskbar, Explorer lists), so those are stored the way every reader
# understands, and only the big ones get the compression they actually need.

Add-Type -AssemblyName System.Drawing

function ConvertTo-IcoDib {
    <#
    .SYNOPSIS
        Turns PNG bytes into the DIB payload of an .ico entry.
    #>
    param([Parameter(Mandatory)][byte[]]$Png)

    $stream = New-Object System.IO.MemoryStream($Png, $false)
    try {
        $image = [System.Drawing.Image]::FromStream($stream)
        try {
            $width = $image.Width
            $height = $image.Height

            # Normalise to a known pixel layout we can read row by row.
            $bitmap = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.Clear([System.Drawing.Color]::Transparent)
                    $graphics.DrawImageUnscaled($image, 0, 0)
                }
                finally { $graphics.Dispose() }

                $rect = New-Object System.Drawing.Rectangle(0, 0, $width, $height)
                $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
                try {
                    $stride = $data.Stride
                    $raw = New-Object byte[] ($stride * $height)
                    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
                }
                finally { $bitmap.UnlockBits($data) }

                # An icon DIB is stored bottom-up, and the AND mask rows are padded
                # to a 4 byte boundary.
                $maskStride = [Math]::Floor(($width + 31) / 32) * 4
                $colorBytes = $width * $height * 4
                $maskBytes = $maskStride * $height

                $payload = New-Object byte[] (40 + $colorBytes + $maskBytes)
                $writer = New-Object System.IO.BinaryWriter((New-Object System.IO.MemoryStream($payload, $true)))
                try {
                    # BITMAPINFOHEADER
                    $writer.Write([uint32]40)              # biSize
                    $writer.Write([int32]$width)           # biWidth
                    $writer.Write([int32]($height * 2))    # biHeight: colour + mask stacked
                    $writer.Write([uint16]1)               # biPlanes
                    $writer.Write([uint16]32)              # biBitCount
                    $writer.Write([uint32]0)               # biCompression = BI_RGB
                    $writer.Write([uint32]$colorBytes)     # biSizeImage
                    $writer.Write([int32]0)                # biXPelsPerMeter
                    $writer.Write([int32]0)                # biYPelsPerMeter
                    $writer.Write([uint32]0)               # biClrUsed
                    $writer.Write([uint32]0)               # biClrImportant

                    # BGRA rows, bottom row first
                    for ($y = $height - 1; $y -ge 0; $y--) {
                        $writer.Write($raw, $y * $stride, $width * 4)
                    }

                    # AND mask: all zero means "the alpha channel decides", which is
                    # what every 32-bit icon does.
                    $writer.Write((New-Object byte[] $maskBytes))
                }
                finally { $writer.Dispose() }

                return , $payload
            }
            finally { $bitmap.Dispose() }
        }
        finally { $image.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-IcoLargestBitmap {
    <#
    .SYNOPSIS
        Returns the biggest image inside an .ico as a Bitmap. Caller disposes it.
    .DESCRIPTION
        System.Drawing.Icon cannot decode a PNG-compressed entry on the .NET
        Framework, and those are exactly the entries the large sizes use, so the
        container is read here and each entry decoded with whatever suits it.
    #>
    param([Parameter(Mandatory)][string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 6 -or [BitConverter]::ToUInt16($bytes, 2) -ne 1) {
        throw "$Path is not an icon file."
    }
    $count = [BitConverter]::ToUInt16($bytes, 4)
    if ($count -lt 1) { throw "$Path holds no images." }

    $bestWidth = -1
    $bestOffset = 0
    $bestLength = 0
    $bestSize = 0
    for ($i = 0; $i -lt $count; $i++) {
        $base = 6 + 16 * $i
        $width = $bytes[$base]; if ($width -eq 0) { $width = 256 }
        if ($width -gt $bestWidth) {
            $bestWidth = $width
            $bestLength = [BitConverter]::ToUInt32($bytes, $base + 8)
            $bestOffset = [BitConverter]::ToUInt32($bytes, $base + 12)
            $bestSize = $width
        }
    }

    $isPng = $bytes[$bestOffset] -eq 0x89 -and $bytes[$bestOffset + 1] -eq 0x50
    if ($isPng) {
        $blob = New-Object byte[] $bestLength
        [Array]::Copy($bytes, $bestOffset, $blob, 0, $bestLength)
        # The stream has to stay open for the lifetime of the Image, so copy the
        # pixels into a Bitmap we own and let the stream go.
        $stream = New-Object System.IO.MemoryStream($blob, $false)
        try {
            $image = [System.Drawing.Image]::FromStream($stream)
            try {
                $copy = New-Object System.Drawing.Bitmap($image.Width, $image.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
                $graphics = [System.Drawing.Graphics]::FromImage($copy)
                try {
                    $graphics.Clear([System.Drawing.Color]::Transparent)
                    $graphics.DrawImageUnscaled($image, 0, 0)
                }
                finally { $graphics.Dispose() }
                return $copy
            }
            finally { $image.Dispose() }
        }
        finally { $stream.Dispose() }
    }

    $icon = New-Object System.Drawing.Icon($Path, $bestSize, $bestSize)
    try { return $icon.ToBitmap() }
    finally { $icon.Dispose() }
}

function Write-IcoFromPngs {
    <#
    .SYNOPSIS
        Packs a set of square PNG images into one .ico file.
    .PARAMETER Entries
        One hashtable per image: @{ Size = 32; Bytes = <byte[]> }.
    .PARAMETER Path
        Where to write the .ico file.
    .PARAMETER PngThreshold
        Sizes above this are stored as PNG, sizes up to it as an uncompressed DIB.
    #>
    param(
        [Parameter(Mandatory)][object[]]$Entries,
        [Parameter(Mandatory)][string]$Path,
        [int]$PngThreshold = 48
    )

    if ($Entries.Count -lt 1) { throw 'Write-IcoFromPngs needs at least one image.' }
    if ($Entries.Count -gt 255) { throw 'An .ico file cannot hold more than 255 images.' }

    $prepared = @()
    foreach ($entry in $Entries) {
        $png = [byte[]]$entry.Bytes
        if ($entry.Size -le $PngThreshold) {
            $prepared += , @{ Size = $entry.Size; Payload = [byte[]](ConvertTo-IcoDib -Png $png); Kind = 'dib' }
        }
        else {
            $prepared += , @{ Size = $entry.Size; Payload = $png; Kind = 'png' }
        }
    }
    $prepared = @($prepared | Sort-Object { $_.Size })

    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $stream = [System.IO.File]::Create($Path)
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        # ICONDIR
        $writer.Write([uint16]0)                  # reserved, always 0
        $writer.Write([uint16]1)                  # 1 = icon (2 would be a cursor)
        $writer.Write([uint16]$prepared.Count)

        # ICONDIRENTRY per image
        $offset = 6 + 16 * $prepared.Count
        foreach ($item in $prepared) {
            # 256 is stored as 0, because the field is a single byte
            $dimension = if ($item.Size -ge 256) { 0 } else { $item.Size }
            $writer.Write([byte]$dimension)       # width
            $writer.Write([byte]$dimension)       # height
            $writer.Write([byte]0)                # palette colours (0 = truecolour)
            $writer.Write([byte]0)                # reserved
            $writer.Write([uint16]1)              # colour planes
            $writer.Write([uint16]32)             # bits per pixel
            $writer.Write([uint32]$item.Payload.Length)
            $writer.Write([uint32]$offset)
            $offset += $item.Payload.Length
        }

        # The cast matters: PowerShell hands a byte[] around as Object[] once it has
        # been through a function return, and BinaryWriter then writes nothing.
        foreach ($item in $prepared) { $writer.Write([byte[]]$item.Payload) }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }

    return , $prepared
}

function Test-IcoFile {
    <#
    .SYNOPSIS
        Reads an .ico back and checks every entry really decodes. Throws if not.
    .OUTPUTS
        One description line per entry.
    #>
    param([Parameter(Mandatory)][string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 6) { throw "$Path is too short to be an icon." }
    if ([BitConverter]::ToUInt16($bytes, 0) -ne 0) { throw "$Path has a bad reserved field." }
    if ([BitConverter]::ToUInt16($bytes, 2) -ne 1) { throw "$Path is not an icon (type is not 1)." }

    $count = [BitConverter]::ToUInt16($bytes, 4)
    if ($count -lt 1) { throw "$Path holds no images." }

    $report = @()
    for ($i = 0; $i -lt $count; $i++) {
        $base = 6 + 16 * $i
        $width = $bytes[$base]; if ($width -eq 0) { $width = 256 }
        $height = $bytes[$base + 1]; if ($height -eq 0) { $height = 256 }
        $length = [BitConverter]::ToUInt32($bytes, $base + 8)
        $offset = [BitConverter]::ToUInt32($bytes, $base + 12)

        if ($offset + $length -gt $bytes.Length) {
            throw "$Path entry $i points past the end of the file ($offset + $length > $($bytes.Length))."
        }

        $isPng = $bytes[$offset] -eq 0x89 -and $bytes[$offset + 1] -eq 0x50
        if ($isPng) {
            $blob = New-Object byte[] $length
            [Array]::Copy($bytes, $offset, $blob, 0, $length)
            $stream = New-Object System.IO.MemoryStream($blob, $false)
            try {
                $image = [System.Drawing.Image]::FromStream($stream)
                try {
                    if ($image.Width -ne $width -or $image.Height -ne $height) {
                        throw "$Path entry $i says ${width}x${height} but the PNG is $($image.Width)x$($image.Height)."
                    }
                }
                finally { $image.Dispose() }
            }
            finally { $stream.Dispose() }
            $report += ("  {0,3}x{1,-3} PNG  {2,7} bytes  ok" -f $width, $height, $length)
        }
        else {
            $headerSize = [BitConverter]::ToUInt32($bytes, $offset)
            $dibWidth = [BitConverter]::ToInt32($bytes, $offset + 4)
            $dibHeight = [BitConverter]::ToInt32($bytes, $offset + 8)
            $bitCount = [BitConverter]::ToUInt16($bytes, $offset + 14)
            if ($headerSize -ne 40) { throw "$Path entry $i has a $headerSize byte DIB header, expected 40." }
            if ($dibWidth -ne $width) { throw "$Path entry $i says width $width but the DIB says $dibWidth." }
            if ($dibHeight -ne $height * 2) { throw "$Path entry $i should have a doubled DIB height, got $dibHeight." }
            if ($bitCount -ne 32) { throw "$Path entry $i is $bitCount bpp, expected 32." }

            # System.Drawing can decode DIB entries, so use it as a second opinion
            $icon = New-Object System.Drawing.Icon($Path, $width, $height)
            try {
                $bitmap = $icon.ToBitmap()
                try {
                    if ($bitmap.Width -lt 1 -or $bitmap.Height -lt 1) {
                        throw "$Path entry $i decoded to an empty bitmap."
                    }
                }
                finally { $bitmap.Dispose() }
            }
            finally { $icon.Dispose() }
            $report += ("  {0,3}x{1,-3} DIB  {2,7} bytes  ok" -f $width, $height, $length)
        }
    }

    return $report
}
