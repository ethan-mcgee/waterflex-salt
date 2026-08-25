[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UserPoolId,

    [Parameter(Mandatory = $true)]
    [string]$ClientId,

    [string]$Region = "us-east-2",

    [string]$ExpectedCallbackUrl = "https://broad-mountain-76be.cloudflareaccess.com/cdn-cgi/access/callback",

    [string]$SnapshotPath = ""
)

$ErrorActionPreference = "Stop"

$awsCommand = Get-Command aws -ErrorAction SilentlyContinue
if (-not $awsCommand) {
    throw "AWS CLI was not found. Install it and authenticate before retrying."
}

$poolJson = & $awsCommand.Source cognito-idp describe-user-pool `
    --user-pool-id $UserPoolId `
    --region $Region `
    --output json
if ($LASTEXITCODE -ne 0) {
    throw "Unable to describe Cognito user pool $UserPoolId in $Region."
}

$clientJson = & $awsCommand.Source cognito-idp describe-user-pool-client `
    --user-pool-id $UserPoolId `
    --client-id $ClientId `
    --region $Region `
    --output json
if ($LASTEXITCODE -ne 0) {
    throw "Unable to describe Cognito app client $ClientId in $Region."
}

$pool = (($poolJson -join [Environment]::NewLine) | ConvertFrom-Json).UserPool
$client = (($clientJson -join [Environment]::NewLine) | ConvertFrom-Json).UserPoolClient
$flows = @($client.ExplicitAuthFlows)
$requiredFlows = @(
    "ALLOW_USER_PASSWORD_AUTH",
    "ALLOW_USER_SRP_AUTH",
    "ALLOW_REFRESH_TOKEN_AUTH"
)

$errors = [System.Collections.Generic.List[string]]::new()

foreach ($flow in $requiredFlows) {
    if ($flow -notin $flows) {
        $errors.Add("Required authentication flow is missing: $flow")
    }
}

if ("ALLOW_USER_AUTH" -in $flows) {
    $errors.Add("Choice-based authentication is enabled: ALLOW_USER_AUTH")
}

if ("email" -notin @($pool.UsernameAttributes)) {
    $errors.Add("The user pool does not use email as its sign-in identifier.")
}

if ("COGNITO" -notin @($client.SupportedIdentityProviders)) {
    $errors.Add("The Cognito user pool directory is not enabled for the app client.")
}

if ("code" -notin @($client.AllowedOAuthFlows)) {
    $errors.Add("The authorization-code OAuth grant is not enabled.")
}

if ($ExpectedCallbackUrl -notin @($client.CallbackURLs)) {
    $errors.Add("Expected Cloudflare Access callback URL is missing: $ExpectedCallbackUrl")
}

$snapshot = [ordered]@{
    CapturedAtUtc = [DateTime]::UtcNow.ToString("o")
    Region = $Region
    UserPoolId = $UserPoolId
    UsernameAttributes = @($pool.UsernameAttributes)
    ClientId = $ClientId
    ClientName = $client.ClientName
    ExplicitAuthFlows = $flows
    SupportedIdentityProviders = @($client.SupportedIdentityProviders)
    CallbackURLs = @($client.CallbackURLs)
    LogoutURLs = @($client.LogoutURLs)
    AllowedOAuthFlows = @($client.AllowedOAuthFlows)
    AllowedOAuthScopes = @($client.AllowedOAuthScopes)
}

if (-not [string]::IsNullOrWhiteSpace($SnapshotPath)) {
    $snapshot | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $SnapshotPath -Encoding utf8
    Write-Host "Wrote a sanitized Cognito configuration snapshot to $SnapshotPath."
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    throw "Cognito login configuration validation failed."
}

Write-Host "Cognito login configuration is valid."
Write-Host "Email and password use the non-choice app-client flow; Cloudflare OAuth settings are preserved."
