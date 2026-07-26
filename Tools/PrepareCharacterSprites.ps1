param(
    [Parameter(Mandatory = $true)]
    [string] $SourcePath,

    [Parameter(Mandatory = $true)]
    [string] $IdleOutputPath,

    [Parameter(Mandatory = $true)]
    [string] $WalkOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

function Find-ArtworkBounds {
    param(
        [System.Drawing.Bitmap] $Bitmap,
        [System.Drawing.Rectangle] $SearchArea
    )

    $left = $SearchArea.Right
    $top = $SearchArea.Bottom
    $right = -1
    $bottom = -1

    for ($y = $SearchArea.Top; $y -lt $SearchArea.Bottom; $y++) {
        for ($x = $SearchArea.Left; $x -lt $SearchArea.Right; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -lt 64) {
                continue
            }

            $left = [Math]::Min($left, $x)
            $top = [Math]::Min($top, $y)
            $right = [Math]::Max($right, $x)
            $bottom = [Math]::Max($bottom, $y)
        }
    }

    if ($right -lt $left -or $bottom -lt $top) {
        throw "No visible sprite was found inside cell $SearchArea"
    }

    return [System.Drawing.Rectangle]::FromLTRB(
        $left,
        $top,
        $right + 1,
        $bottom + 1)
}

function Make-AlphaCrisp {
    param([System.Drawing.Bitmap] $Bitmap)

    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            $color = $Bitmap.GetPixel($x, $y)
            if ($color.A -lt 112) {
                $Bitmap.SetPixel(
                    $x,
                    $y,
                    [System.Drawing.Color]::Transparent)
                continue
            }

            $Bitmap.SetPixel(
                $x,
                $y,
                [System.Drawing.Color]::FromArgb(
                    255,
                    $color.R,
                    $color.G,
                    $color.B))
        }
    }
}

function Find-FrameColumns {
    param(
        [System.Drawing.Bitmap] $Bitmap,
        [int] $RowTop,
        [int] $RowHeight
    )

    $rowBottom = $RowTop + $RowHeight
    $regions = [System.Collections.Generic.List[System.Drawing.Rectangle]]::new()
    $regionStart = -1
    $lastArtworkColumn = -1
    $maximumInternalGap = 12

    for ($x = 0; $x -lt $Bitmap.Width; $x++) {
        $containsArtwork = $false
        for ($y = $RowTop; $y -lt $rowBottom; $y++) {
            if ($Bitmap.GetPixel($x, $y).A -ge 64) {
                $containsArtwork = $true
                break
            }
        }

        if (-not $containsArtwork) {
            continue
        }

        if ($regionStart -lt 0) {
            $regionStart = $x
        }
        elseif ($x - $lastArtworkColumn -gt $maximumInternalGap) {
            $regions.Add(
                [System.Drawing.Rectangle]::FromLTRB(
                    $regionStart,
                    $RowTop,
                    $lastArtworkColumn + 1,
                    $rowBottom))
            $regionStart = $x
        }
        $lastArtworkColumn = $x
    }

    if ($regionStart -ge 0) {
        $regions.Add(
            [System.Drawing.Rectangle]::FromLTRB(
                $regionStart,
                $RowTop,
                $lastArtworkColumn + 1,
                $rowBottom))
    }

    return $regions
}

function Export-AnimationRow {
    param(
        [System.Drawing.Bitmap] $Source,
        [int] $RowTop,
        [int] $RowHeight,
        [int] $FrameCount,
        [string] $OutputPath
    )

    $cellSize = 64
    $maximumArtworkWidth = 58
    $maximumArtworkHeight = 56
    $feetBaseline = 59
    $frameRegions = Find-FrameColumns `
        -Bitmap $Source `
        -RowTop $RowTop `
        -RowHeight $RowHeight
    if ($frameRegions.Count -ne $FrameCount) {
        throw "Expected $FrameCount frames, but found $($frameRegions.Count) in row starting at $RowTop"
    }

    $strip = [System.Drawing.Bitmap]::new(
        $cellSize * $FrameCount,
        $cellSize,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    try {
        $strip.SetResolution(96, 96)
        $graphics = [System.Drawing.Graphics]::FromImage($strip)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode =
                [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode =
                [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $graphics.PixelOffsetMode =
                [System.Drawing.Drawing2D.PixelOffsetMode]::Half
            $graphics.SmoothingMode =
                [System.Drawing.Drawing2D.SmoothingMode]::None

            for ($index = 0; $index -lt $FrameCount; $index++) {
                $artworkBounds = Find-ArtworkBounds `
                    $Source `
                    $frameRegions[$index]
                $frame = $Source.Clone(
                    $artworkBounds,
                    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

                try {
                    Make-AlphaCrisp $frame
                    $scale = [Math]::Min(
                        $maximumArtworkWidth / [double]$frame.Width,
                        $maximumArtworkHeight / [double]$frame.Height)
                    $targetWidth = [Math]::Max(
                        1,
                        [Math]::Round($frame.Width * $scale))
                    $targetHeight = [Math]::Max(
                        1,
                        [Math]::Round($frame.Height * $scale))
                    $targetLeft = ($index * $cellSize) +
                        [Math]::Floor(($cellSize - $targetWidth) / 2)
                    $targetTop = $feetBaseline - $targetHeight + 1
                    $destination = [System.Drawing.Rectangle]::new(
                        $targetLeft,
                        $targetTop,
                        $targetWidth,
                        $targetHeight)
                    $graphics.DrawImage(
                        $frame,
                        $destination,
                        0,
                        0,
                        $frame.Width,
                        $frame.Height,
                        [System.Drawing.GraphicsUnit]::Pixel)
                }
                finally {
                    $frame.Dispose()
                }
            }
        }
        finally {
            $graphics.Dispose()
        }

        $outputDirectory = Split-Path -Parent $OutputPath
        New-Item -ItemType Directory -Path $outputDirectory -Force |
            Out-Null
        $strip.Save(
            $OutputPath,
            [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $strip.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
    throw "Generated character atlas was not found: $SourcePath"
}

$source = [System.Drawing.Bitmap]::new($SourcePath)
try {
    $rowSplit = [Math]::Floor($source.Height / 2)
    Export-AnimationRow `
        -Source $source `
        -RowTop 0 `
        -RowHeight $rowSplit `
        -FrameCount 4 `
        -OutputPath $IdleOutputPath
    Export-AnimationRow `
        -Source $source `
        -RowTop $rowSplit `
        -RowHeight ($source.Height - $rowSplit) `
        -FrameCount 6 `
        -OutputPath $WalkOutputPath
}
finally {
    $source.Dispose()
}

Write-Host "Created $IdleOutputPath"
Write-Host "Created $WalkOutputPath"
