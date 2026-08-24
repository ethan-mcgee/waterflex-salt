# AWS RDS PostgreSQL Staging Runbook

This is the active staging topology:

```text
Devices and web clients -> API on EC2 -> private Amazon RDS for PostgreSQL
```

PostgreSQL does not run on the application EC2 instance. RDS must not connect to a developer workstation.
The database starts empty and receives the schema through the checked-in EF Core migration.

## Deployed staging inventory

The following resources were provisioned in `us-east-2` on 2026-07-31:

- EC2 API: `WaterFlex Salt Monitor` (`i-0cc13f9f412cffe70`) in VPC `vpc-0e51113c474c6f1ee`, with application
  security group `sg-058771441e15f0d04`.
- RDS PostgreSQL: `waterflex-salt-staging`, PostgreSQL 17.7, `db.t4g.micro`, 20 GiB gp3, encrypted,
  non-public, and deletion-protected.
- Database: `waterflex_salt_staging`.
- DB subnet group: `waterflex-rds-staging`, spanning the default VPC subnets in `us-east-2a`,
  `us-east-2b`, and `us-east-2c`.
- DB security group: `waterflex-rds-staging-sg` (`sg-0dfef0b55f480da0c`), with PostgreSQL ingress only
  from the EC2 application security group.
- EC2 management role/profile: `waterflex-ec2-staging-role` and `waterflex-ec2-staging-profile`, with the
  AWS-managed `AmazonSSMManagedInstanceCore` policy and an inline policy that can read only the runtime database
  secret.
- Secrets Manager credentials: `waterflex/staging/database/migrator` and
  `waterflex/staging/database/runtime`. EC2 cannot read the migrator or RDS master secrets after deployment.
- API service: `waterflex-api.service`, enabled under systemd and running as `ubuntu` with
  `ASPNETCORE_ENVIRONMENT=Staging`. Its root-owned pre-start loader writes the runtime connection to
  `/run/waterflex-api/environment` with mode `0600`.
- RDS CA bundle: `/etc/ssl/certs/aws-rds-global-bundle.pem`.
- Post-cutover snapshot: `waterflex-salt-staging-post-cutover-20260731`.
- The former EC2-local `postgresql.service` is disabled and stopped. Its files have not been deleted.

The AWS free plan currently limits automated RDS backup retention to one day. Staging uses that maximum and
must take a manual snapshot before risky changes. Upgrade the account and raise retention to at least seven days
before treating this database as production-ready.

The pre-existing `database-1` resource is Aurora PostgreSQL Serverless with internet-gateway mode, no VPC
networking, no storage encryption, one-day backups, and no deletion protection. It is retained only until the new
RDS database and API are validated; it is not the application target.

## 1. Inventory AWS resources

Record the following before changing networking:

- EC2 instance ID, operating system, VPC, subnet, private IP, application security group, and service name.
- RDS identifier, PostgreSQL version, status, endpoint, port, VPC, subnet group, security group, encryption,
  backup retention, deletion protection, and public-access setting.
- The EC2 and RDS VPCs must be the same or have an explicitly routed private connection with working DNS.

Substitute the real AWS region and identifiers in a trusted administrator shell:

```powershell
$env:AWS_REGION = '<region>'
aws ec2 describe-instances --instance-ids '<ec2-instance-id>' --region $env:AWS_REGION
aws rds describe-db-instances --db-instance-identifier '<rds-identifier>' --region $env:AWS_REGION
```

## 2. Restrict database networking

RDS must have `PubliclyAccessible` set to `false`. Add one inbound rule to the RDS security group:

- Protocol: TCP
- Port: 5432
- Source: the EC2 application security-group ID

Do not permit `0.0.0.0/0`, `::/0`, a home IP address, or the general VPC CIDR when a security-group reference
can identify the application. Confirm network ACLs, routes, and VPC DNS permit the private connection.

From EC2, verify DNS and TCP reachability before handling credentials:

```bash
getent hosts <rds-endpoint>
timeout 5 bash -c '</dev/tcp/<rds-endpoint>/5432'
```

## 3. Create staging identities and database

Use the RDS administrator only for initial setup. The deployed instance uses an RDS-managed master password in
AWS Secrets Manager. Create separate migration and runtime logins; never run the API as the RDS administrator.

Connect to the default `postgres` database with `psql`, then run the following after substituting generated role
names. Enter passwords through an interactive prompt or a protected temporary password file, not command history.

```sql
CREATE ROLE waterflex_staging_migrator LOGIN;
\password waterflex_staging_migrator

CREATE ROLE waterflex_staging_runtime LOGIN;
\password waterflex_staging_runtime

CREATE DATABASE waterflex_salt_staging
  OWNER waterflex_staging_migrator;

REVOKE CONNECT, TEMPORARY ON DATABASE waterflex_salt_staging FROM PUBLIC;
GRANT CONNECT ON DATABASE waterflex_salt_staging TO waterflex_staging_runtime;
```

Reconnect to `waterflex_salt_staging` as the migration role and prepare runtime access:

```sql
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO waterflex_staging_runtime;

ALTER DEFAULT PRIVILEGES FOR ROLE waterflex_staging_migrator IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO waterflex_staging_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE waterflex_staging_migrator IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO waterflex_staging_runtime;
```

The default privileges apply to tables created by the migration role. Keep the migration credential separate from
the API runtime credential.

## 4. Require verified TLS

Install the current Amazon RDS CA bundle on EC2 from the official AWS trust-store distribution. Record its path,
for example `/etc/ssl/certs/aws-rds-global-bundle.pem`, and restrict modification to administrators.

Use certificate verification in both connection strings:

```text
Host=<rds-endpoint>;Port=5432;Database=waterflex_salt_staging;Username=<role>;Password=<secret>;SSL Mode=VerifyFull;Root Certificate=/etc/ssl/certs/aws-rds-global-bundle.pem
```

Test the migration login from EC2 without placing its password on the command line:

```bash
PGSSLMODE=verify-full \
PGSSLROOTCERT=/etc/ssl/certs/aws-rds-global-bundle.pem \
psql "host=<rds-endpoint> port=5432 dbname=waterflex_salt_staging user=<migration-role>" \
  -c 'select current_database(), current_user, version();'
```

## 5. Apply the empty schema

From a checkout on EC2 or a controlled deployment runner with private RDS access, restore tools and set the
migration connection string only for the migration process:

```bash
dotnet tool restore
read -rsp 'Migration database password: ' DB_PASSWORD; echo
export ConnectionStrings__SaltMonitor="Host=<rds-endpoint>;Port=5432;Database=waterflex_salt_staging;Username=<migration-role>;Password=${DB_PASSWORD};SSL Mode=VerifyFull;Root Certificate=/etc/ssl/certs/aws-rds-global-bundle.pem"
dotnet tool run dotnet-ef database update \
  --project backend/src/WaterFlex.SaltMonitor.Infrastructure \
  --startup-project backend/src/WaterFlex.SaltMonitor.Infrastructure \
  --context SaltMonitorDbContext
unset ConnectionStrings__SaltMonitor DB_PASSWORD
```

Do not run `tools/copy_local_postgres_to_rds.ps1` for this setup. That utility drops and replaces its target
database and is reserved for an explicitly approved local-data migration.

Verify the migration and empty staging state:

```sql
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId";

SELECT COUNT(*) AS device_count FROM "Devices";
SELECT COUNT(*) AS telemetry_count FROM "TelemetryReadings";
SELECT COUNT(*) AS hourly_summary_count FROM "TelemetryHourlySummaries";
SELECT COUNT(*) AS daily_summary_count FROM "TelemetryDailySummaries";
```

The newest expected migration ID is `20260814031421_AddTelemetryHistoryRetention`; all counts should initially
be zero for a new environment. On an existing environment, the worker backfills summaries before deleting any
raw reading.

## 6. Configure the EC2 API

Store the runtime connection string in AWS Secrets Manager. Integrate secret retrieval with the existing EC2
service deployment mechanism and expose it to the API process as `ConnectionStrings__SaltMonitor`. Do not write
the value to the repository, AMI, EC2 user data, deployment logs, or a world-readable environment file.

The repository includes an EC2 bootstrap helper at `backend/tools/ec2-staging-start.sh` and a systemd unit at
`backend/tools/waterflex-api.service`. Staging images are built on a developer workstation, pushed to private
Amazon ECR repositories, and pulled by EC2. The EC2 host does not build the application.

Add these ECR read actions to the EC2 instance role in addition to its existing Secrets Manager permission:

```json
{
  "Effect": "Allow",
  "Action": [
    "ecr:GetAuthorizationToken",
    "ecr:BatchCheckLayerAvailability",
    "ecr:GetDownloadUrlForLayer",
    "ecr:BatchGetImage"
  ],
  "Resource": "*"
}
```

From PowerShell on the build workstation, publish images to ECR:

```powershell
.\backend\tools\build-and-push-staging-images.ps1
```

Use the `ECR_REGISTRY` and `IMAGE_TAG` values printed by that command to create the root-owned deployment file on
EC2. This file selects images and contains no database credential:

```bash
sudo install -d -m 0755 /etc/waterflex
sudo tee /etc/waterflex/deployment.env >/dev/null <<'EOF'
ECR_REGISTRY=<aws-account-id>.dkr.ecr.us-east-2.amazonaws.com
IMAGE_TAG=<git-commit>
EOF
sudo chmod 0644 /etc/waterflex/deployment.env
```

Copy the service file to `/etc/systemd/system/waterflex-api.service`, make the script executable, and install the
repository on EC2 before enabling the service.

Example EC2 commands:

```bash
sudo mkdir -p /home/ubuntu/waterflex-salt
sudo cp -R . /home/ubuntu/waterflex-salt/
sudo chown -R ubuntu:ubuntu /home/ubuntu/waterflex-salt
sudo chmod +x /home/ubuntu/waterflex-salt/backend/tools/ec2-staging-start.sh
sudo cp /home/ubuntu/waterflex-salt/backend/tools/waterflex-api.service /etc/systemd/system/waterflex-api.service
sudo systemctl daemon-reload
sudo systemctl enable --now waterflex-api.service
```

Set the application environment to `Staging` (or another established non-Development value). The API deliberately
fails startup outside Development when `ConnectionStrings__SaltMonitor` is absent.

The deployed service retrieves `waterflex/staging/database/runtime` through the EC2 instance role before each
start, logs Docker into ECR, pulls the configured image tag, and starts Compose without building. To deploy a new
release, publish a new tag locally, update `IMAGE_TAG` in `/etc/waterflex/deployment.env`, and restart the service:

```bash
sudo systemctl restart waterflex-api.service
sudo systemctl status waterflex-api.service --no-pager
sudo journalctl -u waterflex-api.service -n 100 --no-pager
```

Inspect logs for startup, DNS, authentication, TLS, and PostgreSQL errors. Never paste the connection string into
logs or support output.

The staging Compose deployment also runs `waterflex-worker`. Verify its backfill and retention cycle without
exposing the database connection string:

```bash
cd /home/ubuntu/waterflex-salt
sudo docker compose --env-file /etc/waterflex/deployment.env -f docker-compose.staging.yml ps worker
sudo docker compose --env-file /etc/waterflex/deployment.env -f docker-compose.staging.yml logs --tail 100 worker
```

Expect a maintenance completion log containing bucket and deletion counts, duration, and the oldest remaining
raw, hourly, and daily timestamps. A second cycle must not create duplicate summary buckets.

## 7. Validate staging

1. Call `/health` to verify process liveness. This endpoint does not test the database.
2. Execute a controlled API workflow that reads and writes persistence.
3. Query the resulting row directly in RDS, restart the API, and confirm the row remains accessible.
4. Confirm the runtime role can select, insert, update, and delete required rows.
5. Confirm the runtime role cannot run `CREATE TABLE`, `DROP TABLE`, or alter the schema.
6. Inspect RDS connection, CPU, storage, and error metrics during the test.
7. Confirm the composite history index and retention ages:

```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT "Id"
FROM "TelemetryReadings"
WHERE "DeviceId" = '<device-id>'
  AND "ReceivedAtUtc" >= now() - interval '24 hours'
ORDER BY "ReceivedAtUtc" DESC, "Id" DESC
LIMIT 50;

SELECT min("ReceivedAtUtc") AS oldest_raw FROM "TelemetryReadings";
SELECT min("BucketStartUtc") AS oldest_hourly FROM "TelemetryHourlySummaries";
SELECT min("BucketStartUtc") AS oldest_daily FROM "TelemetryDailySummaries";
```

The query plan should use `IX_TelemetryReadings_DeviceId_ReceivedAtUtc_Id` once enough readings exist for an
index scan to be cheaper than a sequential scan.

Before pilot use, configure storage and connection alarms, maintenance windows, PostgreSQL log exports,
credential rotation ownership, and a restore drill to a separate temporary database. Deletion protection and
one-day automated backups are enabled. Upgrade the AWS account and raise retention to at least seven days before
production use.

## 8. Rollback

If validation fails, stop API writes, preserve logs, and correct networking, certificates, privileges, migration,
or the runtime secret. If data must be recovered, restore an RDS snapshot to a separate instance and validate it
before changing the API connection. Do not point staging at a developer workstation database as a fallback.
