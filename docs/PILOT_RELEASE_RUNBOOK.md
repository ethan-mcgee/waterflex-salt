# WaterFlex field-pilot release runbook

This runbook is the final human-controlled gate after CI passes. Live AWS and Cloudflare changes require separate
authorization. Never paste database passwords, device secrets, JWTs, or Cloudflare tokens into tickets or logs.

## 1. Read-only staging evidence

Record timestamps and command/console evidence for each item before making changes:

- Git commit SHA equals the immutable ECR tag for API, worker, web, and migrator images; record image digests.
- Cloudflare Access protects the console and its application audience matches `CloudflareAccess__Audience`.
- DNS records are proxied, SSL/TLS mode is Full (strict), origin certificates are valid, and direct-origin plus forged
  `Host` requests fail.
- The EC2 security group permits origin HTTPS only from the current Cloudflare published ranges and administrative
  access only through Systems Manager. Re-fetch Cloudflare ranges; do not reuse an old copied list.
- RDS is private, encrypted, deletion-protected, and has at least seven days of automated backups. Record the backup
  window, latest restorable time, storage, connection alarms, and the runtime/migrator database roles.
- Runtime database credentials cannot create or alter schema. Only the migration job can use the migrator secret.
- `https://telemetry-staging.saltmonitor.dev/health/ready` succeeds; unrelated telemetry-host routes return 404.
- A forged `X-WaterFlex-Development-User` header in Staging is rejected and cannot select another staff identity.

Any discrepancy blocks rollout. Record it as a release issue; do not silently remediate during the evidence pass.

## 2. Database migration and restore drill

1. Create and wait for a pre-deployment RDS snapshot.
2. Confirm the migration image digest and source SHA. The deployment workflow stops the service, reads the dedicated
   migrator secret without placing it in a command argument, and runs the bundled migration before application start.
3. Confirm `/health/ready` reports both database connectivity and no pending migrations before traffic verification.
4. Restore the snapshot to an isolated private database with no application traffic. Use a new security group and
   temporary credentials, then verify schema, row counts, latest trustworthy telemetry, alerts, and audit history.
5. Record requested restore time, availability time, validation completion time, recovery time, and latest recovered
   transaction/snapshot time. Delete the isolated restore only under an approved cleanup change.

## 3. Bench acceptance

- With power disconnected, use the A0221AT UART wiring in `firmware/README.md`; verify the sensor label and connector
  order rather than relying on wire colors.
- Confirm valid 9600-baud frames produce `distance=<number> mm`. Then disconnect sensor TX to exercise `readTimeout`,
  inject a bad checksum/partial frame to exercise `invalidSignal`, and inject a checksum-valid out-of-range value to
  exercise `outOfRange`. Confirm only device health changes; latest fill and alert state preserve the last
  trustworthy reading.
- Queue readings with Wi-Fi disabled, power-cycle, reconnect, and confirm exact boot/sequence acknowledgements drain
  the persisted queue without duplicate history or alerts. Verify queue depth and dropped count in the console.
- Attempt an HTTP or non-WaterFlex API destination using the pilot build; provisioning must reject it before saving.
- Verify a unique factory setup secret is required. Do not ship the development firmware environment.
- Exercise low-fill debounce: two trusted readings below 35 percent at least five minutes apart create one alert.
  Confirm 40 percent recovery hysteresis, acknowledge/approve/dismiss audit history, and 24-hour dismissal cooldown.
- Approval records pilot intent only; verify it creates no RouteFlex ticket.

## 4. Rollout and rollback

Advance only after the prior cohort remains healthy for the agreed observation window:

1. Bench devices.
2. One canary field device.
3. 25 percent of pilot devices.
4. 100 percent of pilot devices.

At each stage check API readiness, worker errors/lag, telemetry rejection rate, sensor faults, stale devices, queue depth,
dropped readings, alert age, database latency/storage/connections, authentication failures, and certificate health.
Rollback on data-trust violations, credential exposure, migration/readiness failure, repeated reboot/upload failure, or
material alert duplication. Roll back application images by immutable digest; do not reverse a data migration unless
its tested migration-specific recovery procedure says to do so.

## 5. Incident and credential response

- Lost/tampered device: revoke all operational credentials, quarantine the inventory record, preserve audit evidence,
  and do not reactivate until it is physically recovered and reprovisioned.
- Suspected credential disclosure: revoke/rotate immediately, review authentication failures and last-used timestamps,
  and treat any bearer token in logs or chat as compromised.
- Failed deployment: retain the failed SHA/digests and logs, restore the prior deployment bundle, verify readiness and
  ingestion, then document the cause before retrying.
- Certificate/trust-anchor issue: stop firmware rollout, validate the public chain and embedded root on bench/canary,
  and use a signed firmware release for trust-anchor changes.
- Database incident: stop writers if integrity is at risk, preserve snapshots/logs, restore into isolation, and record
  actual RPO/RTO. Never expose RDS publicly for diagnosis.

## 6. Release sign-off

The release owner records: source SHA, image digests, firmware hashes, migration ID, CI run, staging evidence, restore
drill, bench/canary results, known exceptions, approvers, and rollback owner. Secure-boot/eFuse enablement remains an
explicit exception until `firmware/PRODUCTION_SECURITY.md` is completed and approved.
