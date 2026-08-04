[CmdletBinding()]
param(
    [string]$Region = "us-east-2",
    [string]$ImageTag = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
Set-Location $repoRoot

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker CLI was not found. Start Docker Desktop and retry."
}

$awsCommand = Get-Command aws -ErrorAction SilentlyContinue
if ($awsCommand) {
    $aws = $awsCommand.Source
} elseif (Test-Path "C:\Program Files\Amazon\AWSCLIV2\aws.exe") {
    $aws = "C:\Program Files\Amazon\AWSCLIV2\aws.exe"
} else {
    throw "AWS CLI was not found. Install it and authenticate before retrying."
}

docker info *> $null
if ($LASTEXITCODE -ne 0) {
    throw "Docker Desktop is not running."
}

$worktreeChanges = git status --porcelain
if ($LASTEXITCODE -ne 0) {
    throw "Unable to inspect the Git worktree."
}

if ($worktreeChanges) {
    throw "Commit or stash all changes before publishing. Image tags must identify the exact Git commit being deployed."
}

if ([string]::IsNullOrWhiteSpace($ImageTag)) {
    $ImageTag = (git rev-parse --short=12 HEAD).Trim()
}

$accountId = (& $aws sts get-caller-identity --query Account --output text --region $Region).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($accountId)) {
    throw "Unable to determine the AWS account ID. Check your AWS credentials."
}

$registry = "$accountId.dkr.ecr.$Region.amazonaws.com"
$repositories = @("waterflex-api", "waterflex-web", "waterflex-worker")
$existingRepositoryOutput = & $aws ecr describe-repositories --query "repositories[].repositoryName" --output text --region $Region
if ($LASTEXITCODE -ne 0) {
    throw "Unable to list ECR repositories in $Region."
}
$existingRepositories = @($existingRepositoryOutput -split "\s+" | Where-Object { $_ })

foreach ($repository in $repositories) {
    if ($repository -notin $existingRepositories) {
        & $aws ecr create-repository --repository-name $repository --region $Region *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to create ECR repository $repository."
        }
    }
}

& $aws ecr get-login-password --region $Region | docker login --username AWS --password-stdin $registry
if ($LASTEXITCODE -ne 0) {
    throw "Unable to authenticate Docker to Amazon ECR."
}

$images = @(
    @{ Name = "waterflex-api"; Dockerfile = "backend/Dockerfile" },
    @{ Name = "waterflex-web"; Dockerfile = "web/Dockerfile" },
    @{ Name = "waterflex-worker"; Dockerfile = "backend/Dockerfile.worker" }
)

foreach ($image in $images) {
    $imageReference = "$registry/$($image.Name):$ImageTag"
    docker build --platform linux/amd64 --file $image.Dockerfile --tag $imageReference .
    if ($LASTEXITCODE -ne 0) {
        throw "Docker build failed for $($image.Name)."
    }

    docker push $imageReference
    if ($LASTEXITCODE -ne 0) {
        throw "Docker push failed for $($image.Name)."
    }
}

Write-Host "Published staging images successfully."
Write-Host "Configure EC2 /etc/waterflex/deployment.env with:"
Write-Host "ECR_REGISTRY=$registry"
Write-Host "IMAGE_TAG=$ImageTag"