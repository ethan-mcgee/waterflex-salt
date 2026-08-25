[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "waterflex-hosted-ui-logo.png")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$width = 350
$height = 120
$bitmap = [System.Drawing.Bitmap]::new($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)

try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $tileBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#082b50"))
    $waterBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#7fcef4"))
    $titleBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $subtitleBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#b8c8d7"))
    $titleFont = [System.Drawing.Font]::new("Bahnschrift", 28, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $subtitleFont = [System.Drawing.Font]::new("Segoe UI", 12, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()

    try {
        $graphics.FillRectangle($tileBrush, 0, 0, $width, $height)

        # Two WaterFlex droplets, matching the application header mark.
        $path.AddBezier(29, 61, 29, 45, 43, 35, 48, 24)
        $path.AddBezier(48, 24, 53, 35, 67, 45, 67, 61)
        $path.AddBezier(67, 61, 67, 73, 59, 81, 48, 81)
        $path.AddBezier(48, 81, 37, 81, 29, 73, 29, 61)
        $graphics.FillPath($waterBrush, $path)
        $path.Reset()
        $path.AddBezier(53, 72, 53, 60, 64, 51, 69, 42)
        $path.AddBezier(69, 42, 74, 51, 85, 60, 85, 72)
        $path.AddBezier(85, 72, 85, 82, 78, 89, 69, 89)
        $path.AddBezier(69, 89, 60, 89, 53, 82, 53, 72)
        $graphics.FillPath($waterBrush, $path)

        $graphics.DrawString("WaterFlex", $titleFont, $titleBrush, 102, 32)
        $graphics.DrawString("FIELDOPS", $subtitleFont, $subtitleBrush, 104, 69)
    }
    finally {
        $path.Dispose()
        $titleFont.Dispose()
        $subtitleFont.Dispose()
        $tileBrush.Dispose()
        $waterBrush.Dispose()
        $titleBrush.Dispose()
        $subtitleBrush.Dispose()
    }

    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
    $bitmap.Save($resolvedOutput, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Wrote $resolvedOutput ($width x $height)."
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}
