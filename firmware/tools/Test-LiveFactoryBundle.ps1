param(
    [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
    [Parameter(Mandatory = $true)][string]$BundleDirectory,
    [Parameter(Mandatory = $true)][string]$EvidencePath
)

$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $BundleDirectory 'factory-bundle.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$imagePath = Join-Path $BundleDirectory $manifest.mergedImage.file
$localSha = (Get-FileHash -LiteralPath $imagePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($localSha -ne $manifest.mergedImage.sha256.ToLowerInvariant()) {
    throw 'Local factory image SHA-256 does not match the local bundle manifest.'
}
if ($manifest.configurationVersion -ne 'factory-v2' -or $manifest.helperProtocolVersion -ne '4') {
    throw 'The local bundle must use configuration version factory-v2 and helper protocol version 4.'
}

$metadataUri = "$($ApiBaseUrl.TrimEnd('/'))/api/v1/factory/bundle"
try {
    $metadata = Invoke-RestMethod -Method Get -Uri $metadataUri -MaximumRedirection 0 -TimeoutSec 30
}
catch {
    throw "Transient network failure while reading live staging bundle metadata from $metadataUri`: $($_.Exception.Message)"
}

$contractMismatches = @()
if ($metadata.firmwareVersion -cne $manifest.firmwareVersion) { $contractMismatches += "firmwareVersion '$($metadata.firmwareVersion)' != '$($manifest.firmwareVersion)'" }
if ($metadata.configurationVersion -cne 'factory-v2') { $contractMismatches += "configurationVersion '$($metadata.configurationVersion)' != 'factory-v2'" }
if ($metadata.helperProtocolVersion -cne '4') { $contractMismatches += "helperProtocolVersion '$($metadata.helperProtocolVersion)' != '4'" }
if ($metadata.sha256 -cne $localSha) { $contractMismatches += "sha256 '$($metadata.sha256)' != '$localSha'" }
if ([string]::IsNullOrWhiteSpace($metadata.downloadUrl)) { $contractMismatches += 'downloadUrl is missing' }
if ($contractMismatches.Count -gt 0) {
    throw "Live staging bundle contract failure: $($contractMismatches -join '; ')."
}

$downloadRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { [IO.Path]::GetTempPath() } else { $env:RUNNER_TEMP }
$downloadPath = Join-Path $downloadRoot ("waterflex-factory-live-{0}.bin" -f [Guid]::NewGuid().ToString('N'))
try {
    try {
        Invoke-WebRequest -Method Get -Uri $metadata.downloadUrl -OutFile $downloadPath -MaximumRedirection 5 -TimeoutSec 120
    }
    catch {
        throw "Transient network failure while downloading the live staging presigned image: $($_.Exception.Message)"
    }
    $downloadSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $downloadPath).Hash.ToLowerInvariant()
    if ($downloadSha -cne $localSha) {
        throw "Live staging presigned image contract failure: downloaded SHA-256 '$downloadSha' != '$localSha'."
    }
}
finally {
    Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
}

$evidence = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json
$evidence | Add-Member -NotePropertyName liveBundle -NotePropertyValue ([ordered]@{
    verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    firmwareVersion = $metadata.firmwareVersion
    configurationVersion = $metadata.configurationVersion
    helperProtocolVersion = $metadata.helperProtocolVersion
    sha256 = $metadata.sha256
    presignedImageSha256 = $localSha
}) -Force
$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $EvidencePath -Encoding utf8
Write-Output "Live staging metadata and presigned image exactly match firmware $($manifest.firmwareVersion) ($localSha)."
