param(
    [string]$PreviewPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\TinyTranscriber\Assets'
[System.IO.Directory]::CreateDirectory($assets) | Out-Null
$sizes = @(16, 20, 24, 32, 48, 64, 256)

function New-IconBitmap([int]$size, [string]$state) {
    $large = [System.Drawing.Bitmap]::new($size * 4, $size * 4)
    $graphics = [System.Drawing.Graphics]::FromImage($large)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(23, 25, 29))
    $border = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(98, 104, 116), 1)
    $accent = switch ($state) {
        'Recording' { [System.Drawing.Color]::FromArgb(255, 111, 97) }
        'Transcribing' { [System.Drawing.Color]::FromArgb(111, 181, 255) }
        default { [System.Drawing.Color]::FromArgb(248, 249, 251) }
    }
    $pen = [System.Drawing.Pen]::new($accent, 3)
    $brush = [System.Drawing.SolidBrush]::new($accent)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size / 8.0, $size / 8.0)
        $path.AddArc(1, 1, 14, 14, 180, 90)
        $path.AddArc(17, 1, 14, 14, 270, 90)
        $path.AddArc(17, 17, 14, 14, 0, 90)
        $path.AddArc(1, 17, 14, 14, 90, 90)
        $path.CloseFigure()
        $graphics.FillPath($background, $path)
        $graphics.DrawPath($border, $path)

        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        if ($state -eq 'Transcribing') {
            foreach ($x in @(9, 16, 23)) {
                $graphics.FillEllipse($brush, $x - 2, 14, 4, 4)
            }
        }
        else {
            $heights = if ($state -eq 'Recording') { @(4, 10, 17, 10, 4) } else { @(8, 17, 8) }
            $spacing = if ($state -eq 'Recording') { 4.5 } else { 6 }
            for ($index = 0; $index -lt $heights.Count; $index++) {
                $x = 16 + ($index - ($heights.Count - 1) / 2) * $spacing
                $height = $heights[$index]
                $graphics.DrawLine($pen, [single]$x, [single](16 - $height / 2),
                    [single]$x, [single](16 + $height / 2))
            }
        }

        $result = [System.Drawing.Bitmap]::new($size, $size)
        $resize = [System.Drawing.Graphics]::FromImage($result)
        try {
            $resize.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $resize.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $resize.DrawImage($large, 0, 0, $size, $size)
        }
        finally {
            $resize.Dispose()
        }
        return $result
    }
    finally {
        $brush.Dispose()
        $pen.Dispose()
        $border.Dispose()
        $background.Dispose()
        $path.Dispose()
        $graphics.Dispose()
        $large.Dispose()
    }
}

foreach ($state in @('Idle', 'Recording', 'Transcribing')) {
    $frames = foreach ($size in $sizes) {
        $bitmap = New-IconBitmap $size $state
        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            ,$stream.ToArray()
        }
        finally {
            $stream.Dispose()
            $bitmap.Dispose()
        }
    }

    $fileName = if ($state -eq 'Idle') { 'TinyTranscriber.ico' } else { "$state.ico" }
    $file = [System.IO.File]::Create((Join-Path $assets $fileName))
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $frames[$index].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    }
    finally {
        $writer.Dispose()
    }
    Write-Output "Generated $fileName ($($sizes -join ', ') px)"
}

if ($PreviewPath) {
    $preview = [System.Drawing.Bitmap]::new(600, 230)
    $graphics = [System.Drawing.Graphics]::FromImage($preview)
    $font = [System.Drawing.Font]::new('Segoe UI', 11)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(240, 242, 245))
        $graphics.FillRectangle([System.Drawing.Brushes]::Black, 0, 130, 600, 100)
        $column = 0
        foreach ($state in @('Idle', 'Recording', 'Transcribing')) {
            $graphics.DrawString($state, $font, [System.Drawing.Brushes]::Black, 20 + $column * 200, 12)
            $x = 20 + $column * 200
            foreach ($size in @(16, 24, 32, 48)) {
                $bitmap = New-IconBitmap $size $state
                try {
                    $graphics.DrawImageUnscaled($bitmap, $x, 78 - $size / 2)
                    $graphics.DrawImageUnscaled($bitmap, $x, 178 - $size / 2)
                }
                finally { $bitmap.Dispose() }
                $x += $size + 10
            }
            $column++
        }
        $preview.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $font.Dispose()
        $graphics.Dispose()
        $preview.Dispose()
    }
}
