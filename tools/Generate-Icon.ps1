[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
$output = Join-Path $assets 'icon.png'
# The SVG is the source of truth. Support only the explicit primitives used by this icon;
# reject new elements rather than silently producing an incomplete package icon.
[xml]$svg = Get-Content (Join-Path $assets 'icon.svg') -Raw
$bitmap = [Drawing.Bitmap]::new(512, 512, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.Color]::Transparent)

function Get-Number($node, [string]$name) {
    return [float]::Parse($node.GetAttribute($name), [Globalization.CultureInfo]::InvariantCulture)
}

try {
    foreach ($node in $svg.DocumentElement.ChildNodes) {
        if ($node.LocalName -in @('title', 'desc', 'defs', '#comment')) { continue }
        $path = [Drawing.Drawing2D.GraphicsPath]::new()
        $brush = $null
        $pen = $null
        try {
            switch ($node.LocalName) {
                'path' {
                    # This mark uses absolute M/L/C/Z commands; fail on any other syntax.
                    $data = $node.GetAttribute('d')
                    if ($data -match '[^MLCZ0-9.,\s-]') { throw 'Unsupported SVG path syntax.' }
                    $tokens = [regex]::Matches($data, '[MLCZ]|-?\d+(?:\.\d+)?')
                    $index = 0
                    $current = [Drawing.PointF]::new(0, 0)
                    while ($index -lt $tokens.Count) {
                        $command = $tokens[$index++].Value
                        $count = switch ($command) { 'M' { 2 } 'L' { 2 } 'C' { 6 } 'Z' { 0 } default { throw "Unsupported path command: $command" } }
                        $values = @()
                        for ($j = 0; $j -lt $count; $j++) {
                            $values += [float]::Parse($tokens[$index++].Value, [Globalization.CultureInfo]::InvariantCulture)
                        }
                        switch ($command) {
                            'M' {
                                $path.StartFigure()
                                $current = [Drawing.PointF]::new($values[0], $values[1])
                            }
                            'L' {
                                $next = [Drawing.PointF]::new($values[0], $values[1])
                                $path.AddLine($current, $next)
                                $current = $next
                            }
                            'C' {
                                $next = [Drawing.PointF]::new($values[4], $values[5])
                                $path.AddBezier($current, [Drawing.PointF]::new($values[0], $values[1]), [Drawing.PointF]::new($values[2], $values[3]), $next)
                                $current = $next
                            }
                            'Z' { $path.CloseFigure() }
                        }
                    }
                    $path.FillMode = if ($node.GetAttribute('fill-rule') -eq 'evenodd') { [Drawing.Drawing2D.FillMode]::Alternate } else { [Drawing.Drawing2D.FillMode]::Winding }
                }
                'rect' {
                    $x = Get-Number $node 'x'; $y = Get-Number $node 'y'
                    $w = Get-Number $node 'width'; $h = Get-Number $node 'height'
                    $d = 2 * (Get-Number $node 'rx')
                    $path.AddArc($x, $y, $d, $d, 180, 90)
                    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
                    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
                    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
                    $path.CloseFigure()
                }
                'circle' {
                    $r = Get-Number $node 'r'
                    $path.AddEllipse((Get-Number $node 'cx') - $r, (Get-Number $node 'cy') - $r, 2 * $r, 2 * $r)
                }
                'line' {
                    $path.AddLine((Get-Number $node 'x1'), (Get-Number $node 'y1'), (Get-Number $node 'x2'), (Get-Number $node 'y2'))
                }
                { $_ -in @('polygon', 'polyline') } {
                    [Drawing.PointF[]]$points = foreach ($pair in ($node.GetAttribute('points').Trim() -split '\s+')) {
                        $xy = $pair -split ','
                        [Drawing.PointF]::new([float]::Parse($xy[0], [Globalization.CultureInfo]::InvariantCulture), [float]::Parse($xy[1], [Globalization.CultureInfo]::InvariantCulture))
                    }
                    $path.AddLines($points)
                    if ($node.LocalName -eq 'polygon') { $path.CloseFigure() }
                }
                default { throw "Unsupported SVG element: $($node.LocalName)" }
            }
            $fill = $node.GetAttribute('fill')
            if ($fill -and $fill -ne 'none') {
                if ($fill -match '^url\(#([a-zA-Z][\w-]*)\)$') {
                    $gradientId = $Matches[1]
                    $gradient = $svg.SelectSingleNode("//*[local-name()='linearGradient' and @id='$gradientId']")
                    if (-not $gradient -or $gradient.GetAttribute('gradientUnits') -ne 'userSpaceOnUse') {
                        throw "Unsupported SVG gradient: $fill"
                    }
                    $stops = @($gradient.ChildNodes | Where-Object LocalName -eq 'stop')
                    $brush = [Drawing.Drawing2D.LinearGradientBrush]::new(
                        [Drawing.PointF]::new((Get-Number $gradient 'x1'), (Get-Number $gradient 'y1')),
                        [Drawing.PointF]::new((Get-Number $gradient 'x2'), (Get-Number $gradient 'y2')),
                        [Drawing.ColorTranslator]::FromHtml($stops[0].GetAttribute('stop-color')),
                        [Drawing.ColorTranslator]::FromHtml($stops[-1].GetAttribute('stop-color')))
                    $blend = [Drawing.Drawing2D.ColorBlend]::new($stops.Count)
                    $blend.Colors = [Drawing.Color[]]@($stops | ForEach-Object { [Drawing.ColorTranslator]::FromHtml($_.GetAttribute('stop-color')) })
                    $blend.Positions = [float[]]@($stops | ForEach-Object { Get-Number $_ 'offset' })
                    $brush.InterpolationColors = $blend
                    $brush.WrapMode = [Drawing.Drawing2D.WrapMode]::TileFlipXY
                }
                else {
                    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($fill))
                }
                $graphics.FillPath($brush, $path)
            }
            $stroke = $node.GetAttribute('stroke')
            if ($stroke -and $stroke -ne 'none') {
                $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($stroke), (Get-Number $node 'stroke-width'))
                $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Miter
                $graphics.DrawPath($pen, $path)
            }
        }
        finally {
            if ($brush) { $brush.Dispose() }
            if ($pen) { $pen.Dispose() }
            $path.Dispose()
        }
    }
    $bitmap.Save($output, [Drawing.Imaging.ImageFormat]::Png)
    # Standard Markdown has no image-size attribute, so provide a native 160px image.
    $preview = [Drawing.Bitmap]::new(160, 160, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $previewGraphics = [Drawing.Graphics]::FromImage($preview)
    try {
        $previewGraphics.Clear([Drawing.Color]::Transparent)
        $previewGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $previewGraphics.DrawImage($bitmap, 0, 0, 160, 160)
        $preview.Save((Join-Path $assets 'icon-readme.png'), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $previewGraphics.Dispose()
        $preview.Dispose()
    }
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}
Write-Output $output
