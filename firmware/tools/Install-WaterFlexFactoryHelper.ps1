param(
    [Parameter(Mandatory = $true)][ValidateSet('staging', 'production')][string]$Environment,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:LOCALAPPDATA 'WaterFlex\FactoryHelper\bin'
$programsDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$startupDirectory = Join-Path $programsDirectory 'Startup'
$shortcutName = "WaterFlex Factory Helper ($Environment).lnk"
$shortcutPath = Join-Path $programsDirectory $shortcutName
$legacyStartupShortcuts = @(
    (Join-Path $startupDirectory 'WaterFlex Factory Helper (staging).lnk'),
    (Join-Path $startupDirectory 'WaterFlex Factory Helper (production).lnk')
)

foreach ($legacyShortcut in $legacyStartupShortcuts) {
    Remove-Item -LiteralPath $legacyShortcut -Force -ErrorAction SilentlyContinue
}

if ($Uninstall) {
    Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue
    Write-Output 'Start Menu shortcut and legacy startup registrations removed. The backend station identity was not revoked.'
    exit 0
}

$source = Join-Path $PSScriptRoot "WaterFlexFactoryHelper-$Environment.exe"
if (-not (Test-Path -LiteralPath $source)) { throw "The $Environment helper executable is missing beside this installer." }
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $programsDirectory -Force | Out-Null
$destination = Join-Path $installDirectory 'WaterFlexFactoryHelper.exe'
Copy-Item -LiteralPath $source -Destination $destination -Force
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $destination
$shortcut.WorkingDirectory = $installDirectory
$shortcut.WindowStyle = 1
$shortcut.Save()
Write-Output "Installed the $Environment helper for the current Windows user. Open '$($shortcutName -replace '\.lnk$', '')' from the Windows Start menu when provisioning sensors."
