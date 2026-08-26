[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UserPoolId,

    [Parameter(Mandatory = $true)]
    [string]$ClientId,

    [string]$Region = "us-east-2",

    [string]$ExpectedCallbackUrl = "https://broad-mountain-76be.cloudflareaccess.com/cdn-cgi/access/callback",

    [Parameter(Mandatory = $true)]
    [string]$DomainPrefix,

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
$domainJson = & $awsCommand.Source cognito-idp describe-user-pool-domain `
    --domain $DomainPrefix `
    --region $Region `
    --output json
if ($LASTEXITCODE -ne 0) {
    throw "Unable to describe Cognito domain $DomainPrefix in $Region."
}

$uiJson = & $awsCommand.Source cognito-idp get-ui-customization `
    --user-pool-id $UserPoolId `
    --client-id $ClientId `
    --region $Region `
    --output json
if ($LASTEXITCODE -ne 0) {
    throw "Unable to read classic Hosted UI customization for app client $ClientId."
}

$domain = (($domainJson -join [Environment]::NewLine) | ConvertFrom-Json).DomainDescription
$ui = (($uiJson -join [Environment]::NewLine) | ConvertFrom-Json).UICustomization
$flows = @($client.ExplicitAuthFlows)
$requiredFlows = @(
    "ALLOW_USER_SRP_AUTH",
    "ALLOW_REFRESH_TOKEN_AUTH"
)

$errors = [System.Collections.Generic.List[string]]::new()

foreach ($flow in $requiredFlows) {
    if ($flow -notin $flows) {
        $errors.Add("Required authentication flow is missing: $flow")
    }
}

if ($domain.ManagedLoginVersion -ne 1) {
    $errors.Add("The Cognito domain does not use Hosted UI (classic) branding version 1.")
}

if ([string]::IsNullOrWhiteSpace($ui.CSSVersion)) {
    $errors.Add("The app client does not have a classic Hosted UI CSS customization.")
}

if ([string]::IsNullOrWhiteSpace($ui.ImageUrl)) {
    $errors.Add("The app client does not have a classic Hosted UI logo customization.")
}

if ("email" -notin @($pool.UsernameAttributes)) {
    $errors.Add("The user pool does not use email as its sign-in identifier.")
}

if ($pool.MfaConfiguration -ne "OFF") {
    $errors.Add("Cognito MFA must remain off because Cloudflare Access provides the role-based TOTP challenge.")
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
    CognitoMfaConfiguration = $pool.MfaConfiguration
    DomainPrefix = $DomainPrefix
    ManagedLoginVersion = $domain.ManagedLoginVersion
    ClientId = $ClientId
    ClientName = $client.ClientName
    ExplicitAuthFlows = $flows
    SupportedIdentityProviders = @($client.SupportedIdentityProviders)
    CallbackURLs = @($client.CallbackURLs)
    LogoutURLs = @($client.LogoutURLs)
    AllowedOAuthFlows = @($client.AllowedOAuthFlows)
    AllowedOAuthScopes = @($client.AllowedOAuthScopes)
    HostedUiCssVersion = $ui.CSSVersion
    HostedUiHasLogo = -not [string]::IsNullOrWhiteSpace($ui.ImageUrl)
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
Write-Host "Hosted UI (classic) branding, WaterFlex customization, and Cloudflare OAuth settings are present."
