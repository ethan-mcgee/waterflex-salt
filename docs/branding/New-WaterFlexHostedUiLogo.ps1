[CmdletBinding()]
param(
    [string]$InputPath = (Join-Path $PSScriptRoot "waterflex-website-logo.png"),

    [string]$OutputPath = (Join-Path $PSScriptRoot "waterflex-hosted-ui-logo.png")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$resolvedInput = [System.IO.Path]::GetFullPath($InputPath)
if (-not [System.IO.File]::Exists($resolvedInput)) {
    throw "Official WaterFlex logo source was not found: $resolvedInput"
}

$source = [System.Drawing.Image]::FromFile($resolvedInput)
$width = 350
$height = [Math]::Max(1, [Math]::Round($width * $source.Height / $source.Width))
$bitmap = [System.Drawing.Bitmap]::new(
    $width,
    $height,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)

try {
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, $width, $height))

    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
    $bitmap.Save($resolvedOutput, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Wrote $resolvedOutput ($width x $height)."
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
    $source.Dispose()
}
