param(
    [Parameter(Mandatory = $true)][ValidateSet('staging', 'production')][string]$Environment,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:LOCALAPPDATA 'WaterFlex\FactoryHelper\bin'
$startupDirectory = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startupDirectory "WaterFlex Factory Helper ($Environment).lnk"

if ($Uninstall) {
    Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue
    Write-Output 'Startup registration removed. The backend station identity was not revoked.'
    exit 0
}

$source = Join-Path $PSScriptRoot "WaterFlexFactoryHelper-$Environment.exe"
if (-not (Test-Path -LiteralPath $source)) { throw "The $Environment helper executable is missing beside this installer." }
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
$destination = Join-Path $installDirectory 'WaterFlexFactoryHelper.exe'
Copy-Item -LiteralPath $source -Destination $destination -Force
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $destination
$shortcut.WorkingDirectory = $installDirectory
$shortcut.WindowStyle = 7
$shortcut.Save()
Write-Output "Installed the $Environment helper for the current Windows user and registered hidden startup at sign-in."
