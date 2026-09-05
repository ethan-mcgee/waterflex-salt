param(
    [Parameter(Mandatory = $true)][string]$Bucket,
    [Parameter(Mandatory = $true)][string]$BundleDirectory,
    [Parameter(Mandatory = $true)][string]$HelperCommit,
    [string]$EvidencePath = 'factory-release-evidence.json',
    [scriptblock]$AwsInvoker
)

$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $BundleDirectory 'factory-bundle.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$binaryPath = Join-Path $BundleDirectory $manifest.mergedImage.file
$binarySha = (Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestSha = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($binarySha -ne $manifest.mergedImage.sha256.ToLowerInvariant()) { throw 'The local binary does not match the manifest SHA-256.' }
$prefix = "factory-bundles/$($manifest.firmwareVersion)/$($manifest.configurationVersion)"

function Invoke-Aws([string[]]$Arguments) {
    if ($null -ne $AwsInvoker) {
        $response = & $AwsInvoker $Arguments
        if ($null -eq $response -or $null -eq $response.ExitCode) { throw 'The AWS test invoker returned an invalid response.' }
        return $response
    }
    $output = & aws @Arguments 2>&1
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join [Environment]::NewLine) }
}

function Assert-ExistingObject([string]$Key, [string]$Path, [string]$Sha256, [switch]$IsManifest) {
    $headResult = Invoke-Aws @('s3api', 'head-object', '--bucket', $Bucket, '--key', $Key)
    if ($headResult.ExitCode -ne 0) {
        if ($headResult.Output -match '404|Not Found|NoSuchKey') { return $false }
        throw "Could not inspect s3://$Bucket/$Key."
    }
    $head = $headResult.Output | ConvertFrom-Json
    if ($head.Metadata.sha256 -ne $Sha256) { throw "Immutable S3 key conflict at s3://$Bucket/${Key}: digest metadata differs or is missing." }
    $temporaryRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { [IO.Path]::GetTempPath() } else { $env:RUNNER_TEMP }
    $temporary = Join-Path $temporaryRoot ([IO.Path]::GetRandomFileName())
    try {
        $getResult = Invoke-Aws @('s3api', 'get-object', '--bucket', $Bucket, '--key', $Key, $temporary)
        if ($getResult.ExitCode -ne 0 -or (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Sha256) {
            throw "Immutable S3 key conflict at s3://$Bucket/${Key}: content differs."
        }
        if ($IsManifest) {
            $remote = Get-Content -LiteralPath $temporary -Raw | ConvertFrom-Json
            if ($remote.firmwareVersion -ne $manifest.firmwareVersion -or $remote.configurationVersion -ne $manifest.configurationVersion -or $remote.mergedImage.sha256 -ne $manifest.mergedImage.sha256) {
                throw "Immutable S3 manifest at s3://$Bucket/$Key is incompatible."
            }
        }
    } finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
    return $true
}

function Publish-Object([string]$Key, [string]$Path, [string]$Sha256, [switch]$IsManifest) {
    if (Assert-ExistingObject $Key $Path $Sha256 -IsManifest:$IsManifest) { return }
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $putResult = Invoke-Aws @('s3api', 'put-object', '--bucket', $Bucket, '--key', $Key, '--body', $Path, '--metadata', "sha256=$Sha256", '--if-none-match', '*')
        if ($putResult.ExitCode -eq 0) { break }
        if ($putResult.Output -match 'PreconditionFailed|412') {
            if (Assert-ExistingObject $Key $Path $Sha256 -IsManifest:$IsManifest) { return }
            throw "A competing publisher created conflicting content at s3://$Bucket/$Key."
        }
        if ($putResult.Output -match 'ConditionalRequestConflict|409' -and $attempt -lt 3) {
            if ($null -eq $AwsInvoker) { Start-Sleep -Seconds $attempt }
            continue
        }
        throw "Conditional publication failed for s3://$Bucket/$Key."
    }
    if (-not (Assert-ExistingObject $Key $Path $Sha256 -IsManifest:$IsManifest)) { throw "Published S3 object could not be verified: s3://$Bucket/$Key." }
}

$binaryKey = "$prefix/waterflex-factory.bin"
$manifestKey = "$prefix/factory-bundle.json"
Publish-Object $binaryKey $binaryPath $binarySha
Publish-Object $manifestKey $manifestPath $manifestSha -IsManifest

[ordered]@{
    firmwareVersion = $manifest.firmwareVersion
    configurationVersion = $manifest.configurationVersion
    binarySha256 = $binarySha
    manifestSha256 = $manifestSha
    s3Bucket = $Bucket
    binaryKey = $binaryKey
    manifestKey = $manifestKey
    helperCommit = $HelperCommit
    verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
} | ConvertTo-Json | Set-Content -LiteralPath $EvidencePath -Encoding utf8
