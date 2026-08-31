[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Operator,
    [Parameter(Mandatory)] [string] $JobState,
    [Parameter(Mandatory)] [string] $LabelOut,
    [string] $BaseUrl = 'http://127.0.0.1:5188',
    [string] $Port
)

$ErrorActionPreference = 'Stop'
$pio = Join-Path $env:USERPROFILE '.platformio\penv\Scripts\pio.exe'
$python = Join-Path $env:USERPROFILE '.platformio\penv\Scripts\python.exe'
$script = Join-Path $PSScriptRoot 'factory_provision_sensor.py'
if (-not (Test-Path -LiteralPath $pio) -or -not (Test-Path -LiteralPath $python)) {
    throw 'PlatformIO was not found in the current Windows user profile.'
}
if ([string]::IsNullOrWhiteSpace($env:WATERFLEX_FACTORY_KEY)) {
    throw 'Set WATERFLEX_FACTORY_KEY in this PowerShell session before provisioning.'
}

$arguments = @(
    $script,
    '--base-url', $BaseUrl,
    '--operator', $Operator,
    '--pio', $pio,
    '--job-state', $JobState,
    '--label-out', $LabelOut
)
if ($Port) {
    $arguments += @('--port', $Port)
}
& $python @arguments
exit $LASTEXITCODE
