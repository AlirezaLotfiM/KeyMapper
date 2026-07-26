param(
    [Parameter(Mandatory = $true)]
    [string] $SourcePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $projectRoot "Resources\Characters\Luffy"
$idleOutputPath = Join-Path $outputDirectory "Luffy_Idle_4.png"
$walkOutputPath = Join-Path $outputDirectory "Luffy_Walk_6.png"

$idleFrames = @(
    [System.Drawing.Rectangle]::new(5, 6, 33, 46),
    [System.Drawing.Rectangle]::new(43, 6, 32, 46),
    [System.Drawing.Rectangle]::new(83, 6, 31, 46),
    [System.Drawing.Rectangle]::new(122, 6, 30, 46)
)

$walkFrames = @(
    [System.Drawing.Rectangle]::new(2, 294, 31, 55),
    [System.Drawing.Rectangle]::new(39, 294, 29, 55),
    [System.Drawing.Rectangle]::new(75, 294, 30, 55),
    [System.Drawing.Rectangle]::new(113, 294, 30, 55),
    [System.Drawing.Rectangle]::new(153, 294, 30, 55),
    [System.Drawing.Rectangle]::new(192, 294, 27, 55)
)

function Remove-ConnectedWhiteBackground {
    param([System.Drawing.Bitmap] $Bitmap)

    $width = $Bitmap.Width
    $height = $Bitmap.Height
    $visited = [bool[]]::new($width * $height)
    $queue = [System.Collections.Generic.Queue[System.Drawing.Point]]::new()

    function Add-BackgroundCandidate {
        param([int] $X, [int] $Y)

        $index = ($Y * $width) + $X
        if ($visited[$index]) {
            return
        }

        $visited[$index] = $true
        $color = $Bitmap.GetPixel($X, $Y)
        if ($color.R -ge 250 -and $color.G -ge 250 -and $color.B -ge 250) {
            $queue.Enqueue([System.Drawing.Point]::new($X, $Y))
        }
    }

    for ($x = 0; $x -lt $width; $x++) {
        Add-BackgroundCandidate $x 0
        Add-BackgroundCandidate $x ($height - 1)
    }
    for ($y = 0; $y -lt $height; $y++) {
        Add-BackgroundCandidate 0 $y
        Add-BackgroundCandidate ($width - 1) $y
    }

    while ($queue.Count -gt 0) {
        $point = $queue.Dequeue()
        $Bitmap.SetPixel(
            $point.X,
            $point.Y,
            [System.Drawing.Color]::Transparent)

        if ($point.X -gt 0) {
            Add-BackgroundCandidate ($point.X - 1) $point.Y
        }
        if ($point.X + 1 -lt $width) {
            Add-BackgroundCandidate ($point.X + 1) $point.Y
        }
        if ($point.Y -gt 0) {
            Add-BackgroundCandidate $point.X ($point.Y - 1)
        }
        if ($point.Y + 1 -lt $height) {
            Add-BackgroundCandidate $point.X ($point.Y + 1)
        }
    }
}

function Export-SpriteStrip {
    param(
        [System.Drawing.Bitmap] $Source,
        [System.Drawing.Rectangle[]] $Frames,
        [string] $OutputPath
    )

    $cellWidth = 48
    $cellHeight = 64
    $feetBaseline = 59
    $strip = [System.Drawing.Bitmap]::new(
        $cellWidth * $Frames.Count,
        $cellHeight,
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

            for ($index = 0; $index -lt $Frames.Count; $index++) {
                $bounds = $Frames[$index]
                $frame = $Source.Clone(
                    $bounds,
                    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
                try {
                    Remove-ConnectedWhiteBackground $frame
                    $x = ($index * $cellWidth) +
                        [Math]::Floor(($cellWidth - $frame.Width) / 2)
                    $y = $feetBaseline - $frame.Height + 1
                    $graphics.DrawImageUnscaled($frame, $x, $y)
                }
                finally {
                    $frame.Dispose()
                }
            }
        }
        finally {
            $graphics.Dispose()
        }

        $strip.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $strip.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
    throw "Sprite atlas was not found: $SourcePath"
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$source = [System.Drawing.Bitmap]::new($SourcePath)
try {
    Export-SpriteStrip $source $idleFrames $idleOutputPath
    Export-SpriteStrip $source $walkFrames $walkOutputPath
}
finally {
    $source.Dispose()
}

Write-Host "Created $idleOutputPath"
Write-Host "Created $walkOutputPath"
