param(
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path (Split-Path -Parent $root) "assets\DiskPulse.ico"
}
$outDir = Split-Path -Parent $Output
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

function New-RoundedPath([System.Drawing.RectangleF]$rect, [float]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($radius * 2, [Math]::Min($rect.Width, $rect.Height))
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(0.5, $size * 0.015)
    $rect = New-Object System.Drawing.RectangleF -ArgumentList $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
    $radius = [Math]::Max(1.0, $size * 0.22)
    $path = New-RoundedPath $rect $radius

    $c1 = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)
    $c2 = [System.Drawing.Color]::FromArgb(255, 34, 211, 238)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList $rect, $c1, $c2, 45.0
    $g.FillPath($brush, $path)

    $inner = New-Object System.Drawing.RectangleF -ArgumentList ($rect.X + $size * 0.07), ($rect.Y + $size * 0.07), ($rect.Width - $size * 0.14), ($rect.Height * 0.44)
    $innerPath = New-RoundedPath $inner ([Math]::Max(1.0, $size * 0.15))
    $hiBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList $inner, ([System.Drawing.Color]::FromArgb(55, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90.0
    $g.FillPath($hiBrush, $innerPath)

    $penWidth = [Math]::Max(1.0, $size * 0.075)
    $pen = New-Object System.Drawing.Pen -ArgumentList ([System.Drawing.Color]::White), $penWidth
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pts = @(
        (New-Object System.Drawing.PointF -ArgumentList ($size * 0.18), ($size * 0.54)),
        (New-Object System.Drawing.PointF -ArgumentList ($size * 0.34), ($size * 0.54)),
        (New-Object System.Drawing.PointF -ArgumentList ($size * 0.43), ($size * 0.33)),
        (New-Object System.Drawing.PointF -ArgumentList ($size * 0.56), ($size * 0.71)),
        (New-Object System.Drawing.PointF -ArgumentList ($size * 0.65), ($size * 0.54)),
        (New-Object System.Drawing.PointF -ArgumentList ($size * 0.82), ($size * 0.54))
    )
    $g.DrawLines($pen, $pts)

    $g.Dispose()
    $path.Dispose()
    $innerPath.Dispose()
    $brush.Dispose()
    $hiBrush.Dispose()
    $pen.Dispose()
    return $bmp
}

# Build a classic 32bpp BMP/DIB icon frame (with AND mask) instead of a PNG
# frame, because .NET's Icon class is happiest with DIB frames.
function New-IconFrame([int]$size) {
    $bmp = New-IconBitmap $size
    $rect = New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $size, $size
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = [Math]::Abs($data.Stride)
    $pixels = New-Object byte[] ($stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    $maskRowBytes = [int]([Math]::Ceiling($size / 8.0))
    while (($maskRowBytes % 4) -ne 0) { $maskRowBytes++ }
    $maskSize = $maskRowBytes * $size
    $imageSize = $size * $size * 4 + $maskSize

    $ms = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($ms)

    # BITMAPINFOHEADER
    $writer.Write([UInt32]40)
    $writer.Write([Int32]$size)
    $writer.Write([Int32]($size * 2))
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32]$imageSize)
    $writer.Write([Int32]0)
    $writer.Write([Int32]0)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32]0)

    # XOR pixels, bottom-up.
    for ($y = $size - 1; $y -ge 0; $y--) {
        $offset = $y * $stride
        $row = New-Object byte[] ($size * 4)
        [Array]::Copy($pixels, $offset, $row, 0, $size * 4)
        $writer.Write($row)
    }

    # AND mask, bottom-up. 1 = fully transparent.
    $maskRow = New-Object byte[] $maskRowBytes
    for ($y = $size - 1; $y -ge 0; $y--) {
        [Array]::Clear($maskRow, 0, $maskRowBytes)
        for ($x = 0; $x -lt $size; $x++) {
            $alpha = $pixels[$y * $stride + $x * 4 + 3]
            if ($alpha -eq 0) {
                $byteIndex = $x -shr 3
                $maskRow[$byteIndex] = $maskRow[$byteIndex] -bor ([byte](0x80 -shr ($x % 8)))
            }
        }
        $writer.Write($maskRow)
    }

    $writer.Flush()
    $bytes = $ms.ToArray()
    $writer.Dispose()
    $ms.Dispose()
    $bmp.Dispose()
    return ,$bytes
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($s in $sizes) { $frames += ,(New-IconFrame $s) }

$ms = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($ms)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $writer.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $writer.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frames[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($Output, $ms.ToArray())
$writer.Dispose()
$ms.Dispose()

Write-Host "Icon written: $Output"
