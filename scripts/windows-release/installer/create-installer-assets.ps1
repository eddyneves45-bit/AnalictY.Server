param(
    [string]$SourceLogo = "..\..\..\frontend\public\logos\iiot-cloud-bi-logo.png",
    [string]$OutputDir = ".\assets"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourcePath = Resolve-Path (Join-Path $scriptDir $SourceLogo)
$assetDir = Join-Path $scriptDir $OutputDir
New-Item -ItemType Directory -Force -Path $assetDir | Out-Null

function New-CanvasBitmap {
    param(
        [System.Drawing.Image]$Source,
        [int]$Width,
        [int]$Height,
        [string]$OutputPath,
        [int]$Padding = 10
    )

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::White)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $maxWidth = $Width - ($Padding * 2)
    $maxHeight = $Height - ($Padding * 2)
    $scale = [Math]::Min($maxWidth / $Source.Width, $maxHeight / $Source.Height)
    $drawWidth = [Math]::Max(1, [int]($Source.Width * $scale))
    $drawHeight = [Math]::Max(1, [int]($Source.Height * $scale))
    $x = [int](($Width - $drawWidth) / 2)
    $y = [int](($Height - $drawHeight) / 2)

    $graphics.DrawImage($Source, $x, $y, $drawWidth, $drawHeight)
    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $graphics.Dispose()
    $bitmap.Dispose()
}

function Remove-CheckerBackground {
    param(
        [System.Drawing.Image]$Source
    )

    $bitmap = New-Object System.Drawing.Bitmap $Source.Width, $Source.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.DrawImage($Source, 0, 0, $Source.Width, $Source.Height)
    $graphics.Dispose()

    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            $pixel = $bitmap.GetPixel($x, $y)
            $isLightBackground = $pixel.R -ge 218 -and $pixel.G -ge 218 -and $pixel.B -ge 218

            if ($isLightBackground) {
                $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
            }
        }
    }

    return $bitmap
}

function New-PngIcon {
    param(
        [System.Drawing.Image]$Source,
        [string]$OutputPath
    )

    $size = 256
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::White)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $padding = 4
    $maxSize = $size - ($padding * 2)
    $scale = [Math]::Min($maxSize / $Source.Width, $maxSize / $Source.Height)
    $drawWidth = [Math]::Max(1, [int]($Source.Width * $scale))
    $drawHeight = [Math]::Max(1, [int]($Source.Height * $scale))
    $x = [int](($size - $drawWidth) / 2)
    $y = [int](($size - $drawHeight) / 2)

    $graphics.DrawImage($Source, $x, $y, $drawWidth, $drawHeight)

    $memory = New-Object System.IO.MemoryStream
    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $memory.ToArray()

    $file = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
    $writer = New-Object System.IO.BinaryWriter $file
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]1)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$pngBytes.Length)
    $writer.Write([UInt32]22)
    $writer.Write($pngBytes)
    $writer.Close()
    $file.Close()

    $memory.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$sourceLogoImage = [System.Drawing.Image]::FromFile($sourcePath)
$logo = Remove-CheckerBackground -Source $sourceLogoImage
try {
    New-CanvasBitmap -Source $logo -Width 164 -Height 314 -Padding 14 -OutputPath (Join-Path $assetDir "analicty-wizard-large.bmp")
    New-CanvasBitmap -Source $logo -Width 55 -Height 55 -Padding 4 -OutputPath (Join-Path $assetDir "analicty-wizard-small.bmp")
    New-PngIcon -Source $logo -OutputPath (Join-Path $assetDir "analicty.ico")
}
finally {
    $logo.Dispose()
    $sourceLogoImage.Dispose()
}

Write-Host "Installer assets created in $assetDir"
