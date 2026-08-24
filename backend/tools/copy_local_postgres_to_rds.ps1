[CmdletBinding()]
param(
    [string]$SourceHost = "localhost",
    [int]$SourcePort = 5432,
    [string]$SourceDatabase = "WaterFlexSaltMonitor",
    [string]$SourceUser = "postgres",

    [Parameter(Mandatory = $true)]
    [string]$TargetHost,

    [int]$TargetPort = 5432,

    [Parameter(Mandatory = $true)]
    [string]$TargetDatabase,

    [Parameter(Mandatory = $true)]
    [string]$TargetUser,

    [string]$TargetSslMode = "require",
    [string]$BackupDirectory = "./db-backups",
    [switch]$SkipTargetBackup,
    [switch]$SkipRowCountValidation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "`n=== $Message ===" -ForegroundColor Cyan
}

function Assert-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH. Install PostgreSQL client tools and retry."
    }
}

function Invoke-PgCommand {
    param(
        [string]$Tool,
        [string[]]$Arguments,
        [string]$Password
    )

    if ($Password) {
        $old = $env:PGPASSWORD
        try {
            $env:PGPASSWORD = $Password
            & $Tool @Arguments
            if ($LASTEXITCODE -ne 0) {
                throw "$Tool failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            $env:PGPASSWORD = $old
        }
    }
    else {
        & $Tool @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$Tool failed with exit code $LASTEXITCODE"
        }
    }
}

function Assert-DbIdentifier {
    param(
        [string]$Label,
        [string]$Value
    )

    if ($Value -notmatch '^[A-Za-z0-9_]+$') {
        throw "$Label '$Value' is not allowed. Use only letters, numbers, and underscore."
    }
}

Assert-Command -Name "pg_dump"
Assert-Command -Name "pg_restore"
Assert-Command -Name "psql"

Assert-DbIdentifier -Label "Source database" -Value $SourceDatabase
Assert-DbIdentifier -Label "Target database" -Value $TargetDatabase

$sourcePassword = $env:SOURCE_PGPASSWORD
$targetPassword = $env:TARGET_PGPASSWORD

if (-not $sourcePassword) {
    Write-Warning "SOURCE_PGPASSWORD is not set. pg_dump/psql may prompt for source password."
}
if (-not $targetPassword) {
    Write-Warning "TARGET_PGPASSWORD is not set. pg_dump/psql/pg_restore may prompt for target password."
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupRoot = Resolve-Path -LiteralPath "."
$backupPath = Join-Path $backupRoot $BackupDirectory
if (-not (Test-Path -LiteralPath $backupPath)) {
    New-Item -Path $backupPath -ItemType Directory | Out-Null
}

$sourceDump = Join-Path $backupPath "source-${SourceDatabase}-${timestamp}.dump"
$targetDump = Join-Path $backupPath "target-precopy-${TargetDatabase}-${timestamp}.dump"
$targetAdminConn = "host=$TargetHost port=$TargetPort dbname=postgres user=$TargetUser sslmode=$TargetSslMode"
$targetDbConn = "host=$TargetHost port=$TargetPort dbname=$TargetDatabase user=$TargetUser sslmode=$TargetSslMode"

Write-Step "Preflight connectivity checks"
Invoke-PgCommand -Tool "psql" -Password $sourcePassword -Arguments @(
    "-h", $SourceHost,
    "-p", "$SourcePort",
    "-U", $SourceUser,
    "-d", $SourceDatabase,
    "-v", "ON_ERROR_STOP=1",
    "-c", "SELECT current_database(), current_user;"
)

Invoke-PgCommand -Tool "psql" -Password $targetPassword -Arguments @(
    "-d", $targetAdminConn,
    "-v", "ON_ERROR_STOP=1",
    "-c", "SELECT version();"
)

Write-Step "Backup source database"
Invoke-PgCommand -Tool "pg_dump" -Password $sourcePassword -Arguments @(
    "-Fc",
    "--no-owner",
    "--no-privileges",
    "-h", $SourceHost,
    "-p", "$SourcePort",
    "-U", $SourceUser,
    "-d", $SourceDatabase,
    "-f", $sourceDump
)

if (-not $SkipTargetBackup) {
    Write-Step "Backup target database before replacement"
    Invoke-PgCommand -Tool "pg_dump" -Password $targetPassword -Arguments @(
        "-Fc",
        "--no-owner",
        "--no-privileges",
        "--dbname=$targetDbConn",
        "-f", $targetDump
    )
}
else {
    Write-Warning "Skipping target pre-copy backup at user request."
}

Write-Step "Drop and recreate target database"
$dropCreateSql = @"
SELECT pg_terminate_backend(pid)
FROM pg_stat_activity
WHERE datname = '$TargetDatabase' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS "$TargetDatabase";
CREATE DATABASE "$TargetDatabase";
"@

Invoke-PgCommand -Tool "psql" -Password $targetPassword -Arguments @(
    "-d", $targetAdminConn,
    "-v", "ON_ERROR_STOP=1",
    "-c", $dropCreateSql
)

Write-Step "Restore source dump into target"
Invoke-PgCommand -Tool "pg_restore" -Password $targetPassword -Arguments @(
    "--clean",
    "--if-exists",
    "--no-owner",
    "--no-privileges",
    "--dbname=$targetDbConn",
    $sourceDump
)

Write-Step "Validate EF migrations"
Invoke-PgCommand -Tool "psql" -Password $targetPassword -Arguments @(
    "-d", $targetDbConn,
    "-v", "ON_ERROR_STOP=1",
    "-c", 'SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'
)

if (-not $SkipRowCountValidation) {
    Write-Step "Compare critical table counts (source vs target)"

    $countSql = @"
SELECT 'CustomerAccounts', COUNT(*) FROM "CustomerAccounts"
UNION ALL SELECT 'Dealers', COUNT(*) FROM "Dealers"
UNION ALL SELECT 'Devices', COUNT(*) FROM "Devices"
UNION ALL SELECT 'ServiceLocations', COUNT(*) FROM "ServiceLocations"
UNION ALL SELECT 'Tanks', COUNT(*) FROM "Tanks"
UNION ALL SELECT 'DeviceInstallations', COUNT(*) FROM "DeviceInstallations"
UNION ALL SELECT 'DeviceCredentials', COUNT(*) FROM "DeviceCredentials"
UNION ALL SELECT 'DeviceBootstrapCredentials', COUNT(*) FROM "DeviceBootstrapCredentials"
UNION ALL SELECT 'CommissioningSessions', COUNT(*) FROM "CommissioningSessions"
UNION ALL SELECT 'TelemetryReadings', COUNT(*) FROM "TelemetryReadings"
ORDER BY 1;
"@

    $sourceCounts = & psql -h $SourceHost -p $SourcePort -U $SourceUser -d $SourceDatabase -At -F "," -v ON_ERROR_STOP=1 -c $countSql
    if ($LASTEXITCODE -ne 0) { throw "Failed to read source row counts." }

    $old = $env:PGPASSWORD
    try {
        if ($targetPassword) { $env:PGPASSWORD = $targetPassword }
        $targetCounts = & psql -d $targetDbConn -At -F "," -v ON_ERROR_STOP=1 -c $countSql
        if ($LASTEXITCODE -ne 0) { throw "Failed to read target row counts." }
    }
    finally {
        $env:PGPASSWORD = $old
    }

    $sourceMap = @{}
    foreach ($line in $sourceCounts) {
        $parts = $line.Split(",")
        $sourceMap[$parts[0]] = [int64]$parts[1]
    }

    $targetMap = @{}
    foreach ($line in $targetCounts) {
        $parts = $line.Split(",")
        $targetMap[$parts[0]] = [int64]$parts[1]
    }

    $mismatch = $false
    foreach ($table in $sourceMap.Keys | Sort-Object) {
        $s = $sourceMap[$table]
        $t = $targetMap[$table]
        $ok = ($s -eq $t)
        if (-not $ok) { $mismatch = $true }
        Write-Host ("{0,-28} source={1,-8} target={2,-8} {3}" -f $table, $s, $t, ($(if ($ok) { "OK" } else { "MISMATCH" })))
    }

    if ($mismatch) {
        throw "Row-count validation failed. Target counts do not match source."
    }
}

Write-Step "Completed"
Write-Host "Source dump:  $sourceDump"
if (-not $SkipTargetBackup) {
    Write-Host "Target backup: $targetDump"
}
Write-Host "Target database '$TargetDatabase' was replaced successfully." -ForegroundColor Green
Write-Host "Next: point staging ConnectionStrings__SaltMonitor at RDS and run API smoke checks." -ForegroundColor Yellow
