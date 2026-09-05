$ErrorActionPreference = 'Stop'
$publisher = Join-Path $PSScriptRoot 'Publish-ImmutableFactoryBundle.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("waterflex-s3-publisher-test-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

function New-Bundle([string]$Directory) {
    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    $binary = Join-Path $Directory 'waterflex-factory.bin'
    [IO.File]::WriteAllBytes($binary, [byte[]](1, 2, 3, 4, 5))
    $sha = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{
        firmwareVersion = 'wf-test-1'
        configurationVersion = 'factory-test-1'
        mergedImage = [ordered]@{ file = 'waterflex-factory.bin'; sha256 = $sha }
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Directory 'factory-bundle.json') -Encoding utf8
}

function New-FakeAws([hashtable]$State) {
    return {
        param([string[]]$Arguments)
        $operation = $Arguments[1]
        $keyIndex = [Array]::IndexOf($Arguments, '--key')
        $key = $Arguments[$keyIndex + 1]
        if ($operation -eq 'head-object') {
            if (-not $State.Objects.ContainsKey($key)) { return [pscustomobject]@{ ExitCode = 255; Output = $State.MissingOutput } }
            return [pscustomobject]@{ ExitCode = 0; Output = (@{ Metadata = @{ sha256 = $State.Objects[$key].Sha256 } } | ConvertTo-Json -Compress) }
        }
        if ($operation -eq 'get-object') {
            $destination = $Arguments[-1]
            [IO.File]::WriteAllBytes($destination, $State.Objects[$key].Bytes)
            return [pscustomobject]@{ ExitCode = 0; Output = '{}' }
        }
        if ($operation -eq 'put-object') {
            $State.PutKeys.Add($key)
            if ($State.PutOutcomes.Count -gt 0) {
                $outcome = $State.PutOutcomes.Dequeue()
                if ($outcome -eq '409') { return [pscustomobject]@{ ExitCode = 255; Output = 'ConditionalRequestConflict 409' } }
                if ($outcome -eq '412') {
                    $body = $Arguments[[Array]::IndexOf($Arguments, '--body') + 1]
                    $metadata = $Arguments[[Array]::IndexOf($Arguments, '--metadata') + 1]
                    $State.Objects[$key] = @{ Bytes = [IO.File]::ReadAllBytes($body); Sha256 = $metadata.Substring(7) }
                    return [pscustomobject]@{ ExitCode = 255; Output = 'PreconditionFailed 412' }
                }
            }
            if ($State.Objects.ContainsKey($key)) { return [pscustomobject]@{ ExitCode = 255; Output = 'PreconditionFailed 412' } }
            $path = $Arguments[[Array]::IndexOf($Arguments, '--body') + 1]
            $metadataValue = $Arguments[[Array]::IndexOf($Arguments, '--metadata') + 1]
            $State.Objects[$key] = @{ Bytes = [IO.File]::ReadAllBytes($path); Sha256 = $metadataValue.Substring(7) }
            return [pscustomobject]@{ ExitCode = 0; Output = '{}' }
        }
        throw "Unexpected fake AWS operation: $operation"
    }.GetNewClosure()
}

function New-State([string]$MissingOutput = '404 Not Found') {
    return @{ Objects = @{}; PutKeys = [Collections.Generic.List[string]]::new(); PutOutcomes = [Collections.Generic.Queue[string]]::new(); MissingOutput = $MissingOutput }
}

function Invoke-Publisher([hashtable]$State, [string]$Bundle, [string]$Evidence) {
    & $publisher -Bucket 'test-bucket' -BundleDirectory $Bundle -HelperCommit ('a' * 40) -EvidencePath $Evidence -AwsInvoker (New-FakeAws $State)
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) { throw "$Message Expected '$Expected', got '$Actual'." }
}

try {
    $bundle = Join-Path $testRoot 'bundle'
    $evidence = Join-Path $testRoot 'evidence.json'
    New-Bundle $bundle
    $binaryKey = 'factory-bundles/wf-test-1/factory-test-1/waterflex-factory.bin'
    $manifestKey = 'factory-bundles/wf-test-1/factory-test-1/factory-bundle.json'

    $state = New-State
    Invoke-Publisher $state $bundle $evidence
    Assert-Equal "$binaryKey,$manifestKey" ($state.PutKeys -join ',') 'Missing objects were not published binary first and manifest last.'
    Assert-Equal 2 $state.Objects.Count 'Missing-object publication did not create both objects.'

    $forbiddenHead = New-State '403 AccessDenied'
    Invoke-Publisher $forbiddenHead $bundle $evidence
    Assert-Equal 2 $forbiddenHead.Objects.Count 'A missing object reported as 403 was not conditionally created.'

    $state.PutKeys.Clear()
    Invoke-Publisher $state $bundle $evidence
    Assert-Equal 0 $state.PutKeys.Count 'A matching retry attempted another upload.'

    $partial = New-State
    $partial.Objects[$binaryKey] = $state.Objects[$binaryKey]
    Invoke-Publisher $partial $bundle $evidence
    Assert-Equal $manifestKey ($partial.PutKeys -join ',') 'A matching partial publication did not upload only the manifest.'

    $conflict = New-State
    $conflict.Objects[$binaryKey] = @{ Bytes = [byte[]](9); Sha256 = '0' * 64 }
    try { Invoke-Publisher $conflict $bundle $evidence; throw 'Expected an immutable-key conflict.' } catch { if ($_.Exception.Message -notmatch 'digest metadata differs') { throw } }

    $race = New-State
    $race.PutOutcomes.Enqueue('412')
    Invoke-Publisher $race $bundle $evidence
    Assert-Equal 2 $race.Objects.Count 'A matching 412 race was not accepted.'

    $retry = New-State
    $retry.PutOutcomes.Enqueue('409'); $retry.PutOutcomes.Enqueue('409')
    Invoke-Publisher $retry $bundle $evidence
    Assert-Equal 2 $retry.Objects.Count 'A retryable 409 did not eventually publish.'

    $failClosed = New-State
    $failClosed.PutOutcomes.Enqueue('409'); $failClosed.PutOutcomes.Enqueue('409'); $failClosed.PutOutcomes.Enqueue('409')
    try { Invoke-Publisher $failClosed $bundle $evidence; throw 'Expected three 409 responses to fail closed.' } catch { if ($_.Exception.Message -notmatch 'Conditional publication failed') { throw } }

    $malformed = Join-Path $testRoot 'malformed'
    New-Item -ItemType Directory -Path $malformed | Out-Null
    Set-Content -LiteralPath (Join-Path $malformed 'factory-bundle.json') -Value '{bad json' -Encoding utf8
    try { Invoke-Publisher (New-State) $malformed $evidence; throw 'Expected malformed manifest rejection.' } catch { if ($_.Exception.Message -match 'Expected malformed') { throw } }

    Write-Output 'Immutable S3 publisher tests passed.'
} finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
