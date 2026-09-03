# Advanced/engineer override entry point for the factory helper: use this when you need a custom
# local bundle directory, a custom esptool, or a non-default WaterFlex API URL. The primary,
# non-technical path is simply double-clicking WaterFlexFactoryHelper-<environment>.exe with no
# arguments -- it fetches and caches the WaterFlex-approved bundle automatically.
[CmdletBinding()]
param(
    [ValidateSet('staging', 'production')]
    [string] $Environment,
    [string] $BundleDirectory,
    [string] $ApiBaseUrl,
    [string[]] $AllowedOrigin = @()
)

$ErrorActionPreference = 'Stop'
$python = Join-Path $env:USERPROFILE '.platformio\penv\Scripts\python.exe'
$helper = Join-Path $PSScriptRoot 'factory_helper.py'

if ($Environment) {
    $executable = Join-Path $PSScriptRoot "WaterFlexFactoryHelper-$Environment.exe"
} else {
    $legacy = Join-Path $PSScriptRoot 'WaterFlexFactoryHelper.exe'
    $staging = Join-Path $PSScriptRoot 'WaterFlexFactoryHelper-staging.exe'
    $production = Join-Path $PSScriptRoot 'WaterFlexFactoryHelper-production.exe'
    if (Test-Path -LiteralPath $legacy) {
        $executable = $legacy
    } elseif ((Test-Path -LiteralPath $staging) -and (Test-Path -LiteralPath $production)) {
        throw 'Both WaterFlexFactoryHelper-staging.exe and WaterFlexFactoryHelper-production.exe are present. Pass -Environment staging or -Environment production to pick one.'
    } elseif (Test-Path -LiteralPath $staging) {
        $executable = $staging
    } elseif (Test-Path -LiteralPath $production) {
        $executable = $production
    } else {
        $executable = $legacy
    }
}

$arguments = @()
if ($BundleDirectory) {
    if (-not (Test-Path -LiteralPath $BundleDirectory)) {
        throw 'The selected WaterFlex factory firmware bundle does not exist.'
    }
    $resolvedBundle = (Resolve-Path -LiteralPath $BundleDirectory).Path
    $arguments += @('--bundle-dir', $resolvedBundle, '--esptool', (Join-Path $resolvedBundle 'tools\esptool.py'))
}
if ($ApiBaseUrl) {
    $arguments += @('--api-base-url', $ApiBaseUrl)
}
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
