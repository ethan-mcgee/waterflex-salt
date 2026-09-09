param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Installer', 'PackagedSmoke', 'CorruptIdentity', 'LiveStagingProbe')]
    [string]$Mode,
    [string]$StagingExecutable = 'dist/WaterFlexFactoryHelper-staging.exe',
    [string]$InstallerPath = 'dist/Install-WaterFlexFactoryHelper.ps1',
    [string]$BundleDirectory = 'firmware/factory-release',
    [string]$ApiBaseUrl,
    [string]$DiagnosticsDirectory
)

$ErrorActionPreference = 'Stop'
$originalAppData = $env:APPDATA
$originalLocalAppData = $env:LOCALAPPDATA
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("waterflex-helper-test-{0}" -f [Guid]::NewGuid().ToString('N'))
$profileRoot = Join-Path $testRoot 'profile'
$env:APPDATA = Join-Path $profileRoot 'Roaming'
$env:LOCALAPPDATA = Join-Path $profileRoot 'Local'
$dataDirectory = Join-Path $testRoot 'helper-data'
$stdoutPath = Join-Path $testRoot 'helper.stdout.log'
$stderrPath = Join-Path $testRoot 'helper.stderr.log'
$startupLogPath = Join-Path $dataDirectory 'factory-helper.log'
$helperProcess = $null

if ([string]::IsNullOrWhiteSpace($DiagnosticsDirectory)) {
    $diagnosticsRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { [IO.Path]::GetTempPath() } else { $env:RUNNER_TEMP }
    $DiagnosticsDirectory = Join-Path $diagnosticsRoot ("factory-helper-diagnostics-{0}" -f $Mode.ToLowerInvariant())
}
$DiagnosticsDirectory = [IO.Path]::GetFullPath($DiagnosticsDirectory)

function Get-AvailableLoopbackPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return ([Net.IPEndPoint]$listener.LocalEndpoint).Port }
    finally { $listener.Stop() }
}

function Write-DiagnosticFile([string]$Label, [string]$Path) {
    Write-Output "===== $Label ($Path) ====="
    if (Test-Path -LiteralPath $Path) { Get-Content -LiteralPath $Path }
    else { Write-Output '<not created>' }
}

function Stop-IsolatedHelperProcesses {
    $matches = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -eq 'WaterFlexFactoryHelper-staging.exe' -and
        $_.CommandLine -like "*$dataDirectory*"
    })
    foreach ($process in $matches) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }
    if ($matches.Count -gt 0) {
        Write-Output "Stopped $($matches.Count) isolated helper process(es): $($matches.ProcessId -join ', ')."
    }
}

function Start-IsolatedHelper {
    param([switch]$RequireLiveApi)
    if (-not (Test-Path -LiteralPath $StagingExecutable)) { throw "Staging helper executable not found: $StagingExecutable" }
    if (-not (Test-Path -LiteralPath $BundleDirectory)) { throw "Factory bundle directory not found: $BundleDirectory" }
    $script:port = Get-AvailableLoopbackPort
    $arguments = @(
        '--bundle-dir', [IO.Path]::GetFullPath($BundleDirectory),
        '--data-dir', $dataDirectory,
        '--noninteractive',
        '--port', $script:port.ToString()
    )
    if ($RequireLiveApi) {
        if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) { throw 'LiveStagingProbe requires -ApiBaseUrl.' }
        $arguments += @('--api-base-url', $ApiBaseUrl)
    }
    $script:helperProcess = Start-Process -FilePath ([IO.Path]::GetFullPath($StagingExecutable)) `
        -ArgumentList $arguments -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
}

function Wait-ForHelperReady {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    $lastProbeFailure = 'No health probe was attempted.'
    $healthUri = "http://127.0.0.1:$port/v1/health"
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($helperProcess.HasExited) {
            throw "The packaged helper exited before becoming ready (exit code $($helperProcess.ExitCode)). Last probe failure: $lastProbeFailure"
        }
        $health = $null
        try {
            $health = Invoke-RestMethod -Method Get -Uri $healthUri `
                -Headers @{ Origin = 'https://console-staging.saltmonitor.dev' } -TimeoutSec 2
        }
        catch {
            $lastProbeFailure = $_.Exception.Message
            Start-Sleep -Milliseconds 500
            continue
        }
        if ($health.status -ne 'ready' -or $health.protocolVersion -ne '4') {
            throw "The packaged helper returned an invalid health response: $($health | ConvertTo-Json -Compress)"
        }
        return
    }
    $state = if ($helperProcess.HasExited) { "exited with code $($helperProcess.ExitCode)" } else { "running with PID $($helperProcess.Id)" }
    throw "The packaged helper did not report ready before the 60 second deadline. Process state: $state. Last probe failure: $lastProbeFailure"
}

function Test-DeviceResponse {
    $devices = Invoke-RestMethod -Method Get -Uri "http://127.0.0.1:$port/v1/devices" `
        -Headers @{ Origin = 'https://console-staging.saltmonitor.dev' } -TimeoutSec 2
    if ($devices.status -notin @('none', 'detected', 'multiple') -or $null -eq $devices.devices) {
        throw "The packaged helper returned an invalid device-detection response: $($devices | ConvertTo-Json -Compress)"
    }
}

function Test-Installer {
    if (-not (Test-Path -LiteralPath $InstallerPath)) { throw "Installer not found: $InstallerPath" }
    $programsDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    $startupDirectory = Join-Path $programsDirectory 'Startup'
    $shortcutPath = Join-Path $programsDirectory 'WaterFlex Factory Helper (staging).lnk'
    $legacyStagingShortcut = Join-Path $startupDirectory 'WaterFlex Factory Helper (staging).lnk'
    $legacyProductionShortcut = Join-Path $startupDirectory 'WaterFlex Factory Helper (production).lnk'
    $identityPath = Join-Path $env:LOCALAPPDATA 'WaterFlex\FactoryHelper\station.identity'
    New-Item -ItemType Directory -Path $startupDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path $identityPath) -Force | Out-Null
    Set-Content -LiteralPath $legacyStagingShortcut -Value 'simulated legacy staging shortcut'
    Set-Content -LiteralPath $legacyProductionShortcut -Value 'simulated legacy production shortcut'
    $identityBytes = [byte[]]::new(257)
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($identityBytes) }
    finally { $random.Dispose() }
    [IO.File]::WriteAllBytes($identityPath, $identityBytes)
    $identityHash = (Get-FileHash -LiteralPath $identityPath -Algorithm SHA256).Hash

    & $InstallerPath -Environment staging
    if (-not (Test-Path -LiteralPath $shortcutPath)) { throw 'The staging Start Menu shortcut was not created.' }
    if (Test-Path -LiteralPath $legacyStagingShortcut) { throw 'The legacy staging Startup shortcut was not removed.' }
    if (Test-Path -LiteralPath $legacyProductionShortcut) { throw 'The legacy production Startup shortcut was not removed.' }
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
    $expectedTarget = Join-Path $env:LOCALAPPDATA 'WaterFlex\FactoryHelper\bin\WaterFlexFactoryHelper.exe'
    if ($shortcut.TargetPath -ne $expectedTarget) { throw "The Start Menu shortcut target is '$($shortcut.TargetPath)', expected '$expectedTarget'." }
    if ($shortcut.WindowStyle -ne 1) { throw 'The Start Menu shortcut does not open the helper console normally.' }
    if ((Get-FileHash -LiteralPath $identityPath -Algorithm SHA256).Hash -ne $identityHash) { throw 'Installation changed the station identity bytes.' }

    & $InstallerPath -Environment staging
    if ((Get-FileHash -LiteralPath $identityPath -Algorithm SHA256).Hash -ne $identityHash) { throw 'Idempotent reinstall changed the station identity bytes.' }
    if ((Get-FileHash -LiteralPath $expectedTarget -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $StagingExecutable -Algorithm SHA256).Hash) {
        throw 'Idempotent reinstall did not preserve the expected helper executable.'
    }

    & $InstallerPath -Environment staging -Uninstall
    if (Test-Path -LiteralPath $shortcutPath) { throw 'Uninstall did not remove the staging Start Menu shortcut.' }
    if (-not (Test-Path -LiteralPath $identityPath)) { throw 'Uninstall removed the station identity.' }
    if ((Get-FileHash -LiteralPath $identityPath -Algorithm SHA256).Hash -ne $identityHash) { throw 'Uninstall changed the station identity bytes.' }
    Write-Output 'Installer validation passed with byte-for-byte identity preservation.'
}

New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
New-Item -ItemType Directory -Path $DiagnosticsDirectory -Force | Out-Null
try {
    switch ($Mode) {
        'Installer' { Test-Installer }
        'PackagedSmoke' {
            Start-IsolatedHelper
            Wait-ForHelperReady
            Test-DeviceResponse
            Write-Output 'Packaged helper local smoke test passed.'
        }
        'CorruptIdentity' {
            New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
            $identityPath = Join-Path $dataDirectory 'station.identity'
            $identityBytes = [Text.Encoding]::UTF8.GetBytes('deliberately malformed identity')
            [IO.File]::WriteAllBytes($identityPath, $identityBytes)
            $identityHash = (Get-FileHash -LiteralPath $identityPath -Algorithm SHA256).Hash
            Start-IsolatedHelper
            if (-not $helperProcess.WaitForExit(15000)) {
                throw 'The noninteractive helper did not exit promptly for a corrupt identity.'
            }
            if ($helperProcess.ExitCode -eq 0) { throw 'The helper accepted a corrupt station identity.' }
            $diagnostics = @($stderrPath, $startupLogPath) | ForEach-Object {
                if (Test-Path -LiteralPath $_) { Get-Content -LiteralPath $_ -Raw }
            }
            $diagnosticText = $diagnostics -join "`n"
            if ($diagnosticText -notmatch [Regex]::Escape($identityPath) -or $diagnosticText -notmatch 'Operator intervention') {
                throw 'Corrupt-identity diagnostics did not identify the identity path and required operator intervention.'
            }
            if ((Get-FileHash -LiteralPath $identityPath -Algorithm SHA256).Hash -ne $identityHash) {
                throw 'The helper changed or replaced the corrupt station identity.'
            }
            Write-Output 'Noninteractive corrupt-identity validation passed without changing the identity.'
        }
        'LiveStagingProbe' {
            Start-IsolatedHelper -RequireLiveApi
            Wait-ForHelperReady
            Test-DeviceResponse
            $probe = Invoke-WebRequest -Method Post -Uri "http://127.0.0.1:$port/v1/station/enroll" `
                -Headers @{ Origin = 'https://console-staging.saltmonitor.dev' } `
                -ContentType 'application/json' -Body '{"grantToken":"","displayName":"release-smoke"}' `
                -SkipHttpErrorCheck -TimeoutSec 20
            if ($probe.StatusCode -ne 409 -or $probe.Content -notmatch 'invalid|expired') {
                throw "The live staging enrollment contract probe failed (HTTP $($probe.StatusCode)): $($probe.Content)"
            }
            Write-Output 'Live staging enrollment contract probe passed.'
        }
    }
}
finally {
    if ($null -ne $helperProcess) {
        if (-not $helperProcess.HasExited) {
            Stop-Process -Id $helperProcess.Id -Force -ErrorAction SilentlyContinue
            $helperProcess.WaitForExit()
        }
        Stop-IsolatedHelperProcesses
        $exitCode = try { $helperProcess.ExitCode } catch { '<unavailable>' }
        Write-Output "Helper launcher final state: exited=$($helperProcess.HasExited) exitCode=$exitCode"
    }
    Write-DiagnosticFile 'stdout' $stdoutPath
    Write-DiagnosticFile 'stderr' $stderrPath
    Write-DiagnosticFile 'persistent startup log' $startupLogPath
    foreach ($entry in @(
        @{ Source = $stdoutPath; Name = "$($Mode.ToLowerInvariant()).stdout.log" },
        @{ Source = $stderrPath; Name = "$($Mode.ToLowerInvariant()).stderr.log" },
        @{ Source = $startupLogPath; Name = "$($Mode.ToLowerInvariant()).startup.log" }
    )) {
        if (Test-Path -LiteralPath $entry.Source) {
            Copy-Item -LiteralPath $entry.Source -Destination (Join-Path $DiagnosticsDirectory $entry.Name) -Force
        }
    }
    $env:APPDATA = $originalAppData
    $env:LOCALAPPDATA = $originalLocalAppData
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
