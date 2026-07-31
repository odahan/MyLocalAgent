[CmdletBinding()]
param(
    [Parameter()]
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\OD.MyLocalAgent.UI\Assets\OD.MyLocalAgent.ico')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.RectangleF]$Bounds,

        [Parameter(Mandatory)]
        [single]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Bounds.X, $Bounds.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.X, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$bitmap = [System.Drawing.Bitmap]::new(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)

try {
    $backgroundPath = New-RoundedRectanglePath -Bounds ([System.Drawing.RectangleF]::new(8, 8, 240, 240)) -Radius 48
    $backgroundBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#171B24'))
    $graphics.FillPath($backgroundBrush, $backgroundPath)
    $backgroundBrush.Dispose()
    $backgroundPath.Dispose()

    $orbitPen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#9B7CF6'), 12)
    $graphics.DrawEllipse($orbitPen, 40, 40, 176, 176)
    $orbitPen.Dispose()

    $headPath = New-RoundedRectanglePath -Bounds ([System.Drawing.RectangleF]::new(65, 88, 126, 92)) -Radius 26
    $headBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#5937B6'))
    $headPen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#C4B5FD'), 5)
    $graphics.FillPath($headBrush, $headPath)
    $graphics.DrawPath($headPen, $headPath)
    $headPen.Dispose()
    $headBrush.Dispose()
    $headPath.Dispose()

    $eyeBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#A3E635'))
    $graphics.FillEllipse($eyeBrush, 91, 120, 18, 18)
    $graphics.FillEllipse($eyeBrush, 147, 120, 18, 18)
    $eyeBrush.Dispose()

    $antennaPen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#C4B5FD'), 6)
    $graphics.DrawLine($antennaPen, 128, 88, 128, 68)
    $graphics.DrawLine($antennaPen, 128, 68, 143, 55)
    $antennaPen.Dispose()
    $antennaBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#A3E635'))
    $graphics.FillEllipse($antennaBrush, 134, 46, 18, 18)
    $antennaBrush.Dispose()

    $smilePen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 5)
    $smilePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $smilePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawArc($smilePen, 107, 131, 42, 28, 15, 150)
    $smilePen.Dispose()
}
finally {
    $graphics.Dispose()
}

try {
    $pngStream = [System.IO.MemoryStream]::new()
    try {
        $bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngBytes = $pngStream.ToArray()
    }
    finally {
        $pngStream.Dispose()
    }

    $fileStream = [System.IO.File]::Open(
        $resolvedOutput,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    $writer = [System.IO.BinaryWriter]::new($fileStream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]1)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$pngBytes.Length)
        $writer.Write([uint32]22)
        $writer.Write($pngBytes)
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    $bitmap.Dispose()
}

Write-Host "Created application icon: $resolvedOutput"
