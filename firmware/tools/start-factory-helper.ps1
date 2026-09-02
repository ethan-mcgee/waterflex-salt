[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BundleDirectory,
    [Parameter(Mandatory)] [string] $ApiBaseUrl,
    [string[]] $AllowedOrigin = @()
)

$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'WaterFlexFactoryHelper.exe'
$python = Join-Path $env:USERPROFILE '.platformio\penv\Scripts\python.exe'
$helper = Join-Path $PSScriptRoot 'factory_helper.py'
if (-not (Test-Path -LiteralPath $BundleDirectory)) {
    throw 'The selected WaterFlex factory firmware bundle does not exist.'
}

$resolvedBundle = (Resolve-Path -LiteralPath $BundleDirectory).Path
$arguments = @('--bundle-dir', $resolvedBundle, '--esptool', (Join-Path $resolvedBundle 'tools\esptool.py'), '--api-base-url', $ApiBaseUrl)
foreach ($origin in $AllowedOrigin) {
    $arguments += @('--allowed-origin', $origin)
}
if (Test-Path -LiteralPath $executable) {
    & $executable @arguments
} elseif (Test-Path -LiteralPath $python) {
    & $python $helper @arguments
} else {
    throw 'The WaterFlex factory helper executable is missing.'
}
exit $LASTEXITCODE
