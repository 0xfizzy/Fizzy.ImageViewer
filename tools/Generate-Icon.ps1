[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
$output = Join-Path $assets 'icon.png'
$bitmap = [Drawing.Bitmap]::new(512, 512, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.Color]::Transparent)

function Add-RoundedRectangle([Drawing.Drawing2D.GraphicsPath] $path, [float] $x, [float] $y, [float] $width, [float] $height, [float] $radius) {
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
}

$outer = [Drawing.Drawing2D.GraphicsPath]::new()
Add-RoundedRectangle $outer 0 0 512 512 112
$graphics.FillPath([Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#0f172a')), $outer)
$frame = [Drawing.Drawing2D.GraphicsPath]::new()
Add-RoundedRectangle $frame 82 116 348 280 44
$graphics.FillPath([Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#38bdf8')), $frame)
$graphics.FillEllipse([Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#fde047')), 306, 154, 68, 68)

$mountain = [Drawing.PointF[]]@([Drawing.PointF]::new(112,346),[Drawing.PointF]::new(216,228),[Drawing.PointF]::new(286,310),[Drawing.PointF]::new(334,259),[Drawing.PointF]::new(402,346))
$graphics.FillPolygon([Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#f8fafc')), $mountain)
$shade = [Drawing.PointF[]]@([Drawing.PointF]::new(112,346),[Drawing.PointF]::new(216,228),[Drawing.PointF]::new(258,277),[Drawing.PointF]::new(198,346))
$graphics.FillPolygon([Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#cbd5e1')), $shade)
$bubble = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#a3e635'))
$graphics.FillEllipse($bubble, 364, 56, 52, 52)
$graphics.FillEllipse($bubble, 423, 33, 30, 30)

$bitmap.Save($output, [Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()
Write-Output $output
