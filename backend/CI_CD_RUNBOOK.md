# WaterFlex CI/CD Runbook

## Pipeline behavior

`CI` runs for pull requests and pushes to `main` or `docker/deployment-staging`:

- .NET Release build, migration-model check, PostgreSQL integration tests, and NuGet vulnerability check.
- Web install, tests, production build, and full dependency audit.
- Arduino Nano ESP32 PlatformIO build with downloadable firmware artifacts.
- Local/staging Compose validation, builds of the API, worker, web, and migration images, and a failing security
  gate for fixed high or critical image vulnerabilities.

After CI succeeds on `docker/deployment-staging`, `Deploy staging` publishes images tagged with the full Git commit
SHA and scans those exact ECR images again. A fixed high or critical finding stops the release before deployment.
The protected `staging` environment gates the deployment. The deployment uploads a checksum-protected bundle to S3
and invokes the managed EC2 instance through Systems Manager; inbound SSH is not required.

The EC2 deployment applies EF migrations with the dedicated migrator secret, starts the selected immutable image
set, runs container smoke checks, and restores the previous release files and image tag when application startup
fails. EF migrations must remain backward-compatible with the previous image for reliable application rollback.

## Required GitHub configuration

Create these repository variables under **Settings -> Secrets and variables -> Actions -> Variables**:

| Variable | Value |
| --- | --- |
| `AWS_REGION` | `us-east-2` |
| `AWS_STAGING_PUBLISH_ROLE_ARN` | ARN of the GitHub OIDC artifact-publishing role |
| `AWS_STAGING_DEPLOY_ROLE_ARN` | ARN of the GitHub OIDC staging deployment role |
| `STAGING_DEPLOY_BUCKET` | Private versioned S3 bucket used for release bundles |
| `STAGING_INSTANCE_ID` | Managed staging EC2 instance ID |

No long-lived AWS access key or database password belongs in GitHub.

Create a GitHub environment named `staging`. Permit `main` (used by the `workflow_run` orchestration) and
`docker/deployment-staging` (used by manual dispatch), require approval for pilot deployments when the repository
plan supports reviewers, prevent self-approval, and do not allow administrators to bypass the protection rule.
The workflow itself accepts automatic releases only from a successful CI run whose head branch is
`docker/deployment-staging`. Protect that branch with all four CI jobs as required status checks.

## AWS OIDC role

Configure the AWS account to trust `https://token.actions.githubusercontent.com` with audience `sts.amazonaws.com`.
Use two least-privilege roles. A `workflow_run` workflow executes from the default branch even though it checks out
the successful CI run's head SHA. Allow the publishing role's `sub` claim for `main` and, if manual dispatch from the
deployment branch is required, `docker/deployment-staging`:

```text
repo:ethan-mcgee/waterflex-salt:ref:refs/heads/main
repo:ethan-mcgee/waterflex-salt:ref:refs/heads/docker/deployment-staging
```

Also require `token.actions.githubusercontent.com:workflow` to equal `Deploy staging`, along with audience
`sts.amazonaws.com`. AWS exposes both claims as IAM condition keys; this prevents an unrelated workflow on an
allowed branch from using the publishing role.

Restrict the deployment role to this repository and the protected `staging` environment subject:

```text
repo:ethan-mcgee/waterflex-salt:environment:staging
```

The GitHub publishing role needs only:

- ECR authorization plus describe/create/push operations for `waterflex-api`, `waterflex-worker`, `waterflex-web`,
  and `waterflex-migrations`.
- `s3:PutObject` for `<deployment-bucket>/releases/*` and bucket-location access.

The GitHub deployment role needs only `ssm:SendCommand` restricted to the staging instance and
`AWS-RunShellScript` document, plus `ssm:GetCommandInvocation` and command-status reads. It does not need ECR,
S3 write, or Secrets Manager access.

The deployment bucket must block public access, enable versioning, require TLS, and use server-side encryption.
Add a lifecycle policy appropriate for staging releases after rollback-retention requirements are agreed.

## EC2 instance role and host prerequisites

The staging instance role needs:

- Read-only ECR access to the four WaterFlex repositories.
- `s3:GetObject` for `<deployment-bucket>/releases/*`.
- `secretsmanager:GetSecretValue` for `waterflex/staging/database/runtime` and
  `waterflex/staging/database/migrator` only.

The migrator secret is used only by `remote-deploy-staging.sh`, is unset immediately after the migration process,
and must identify the least-privileged PostgreSQL migration role. The normal API and worker continue using the
runtime secret. Review CloudTrail alerts for unexpected migrator-secret reads.

The host must have AWS CLI, Docker with Compose, Python 3, the AWS RDS CA bundle, the Cloudflare origin certificate,
and the existing `waterflex-api.service`. `/etc/waterflex/deployment.env` must be root-readable only and may include:

```text
AWS_REGION=us-east-2
MIGRATION_SECRET_ID=waterflex/staging/database/migrator
```

## First activation and verification

1. Push the workflow files and let CI pass before enabling branch protection.
2. Configure OIDC, GitHub variables, environment protection, S3, and IAM.
3. Manually run `Deploy staging` for a known commit SHA.
4. Approve the `staging` environment job.
5. Verify the workflow reports the SSM command output and public telemetry health response.
6. In EC2 Session Manager, run `docker compose -f /home/ubuntu/waterflex-salt/docker-compose.staging.yml ps`.
7. Confirm the API, worker, and web images all use the same full commit tag.
8. Confirm the migration appears in `__EFMigrationsHistory` using the migrator role, not the runtime role.
9. Exercise one authenticated device-health request and verify it does not create a telemetry reading.

GitHub-hosted CI and AWS staging continue running when the local PC is off. Physical firmware upload and
hardware-in-loop PWM testing still require the Nano and a connected workstation or a dedicated locked-down runner.
