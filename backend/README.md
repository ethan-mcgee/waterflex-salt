# Backend (.NET 10)

Multi-tenant ingestion, level processing, rules, and WaterFlex ticket integration for Plan C.

## Projects

- `src/WaterFlex.SaltMonitor.Domain` — models, `FillCalculator`, and delivery-ticket contracts.
- `src/WaterFlex.SaltMonitor.Ingestion` — typed REST telemetry contracts, validation, and application services.
- `src/WaterFlex.SaltMonitor.Rules` — low-salt threshold / trigger evaluation.
- `src/WaterFlex.SaltMonitor.Infrastructure` — gateway implementations (WaterFlex stub) and persistence.
- `src/WaterFlex.SaltMonitor.Api` — ASP.NET Core REST API for device telemetry and operations.
- `src/WaterFlex.SaltMonitor.Operations` — internal fleet query and device-operation contracts.
- `src/WaterFlex.SaltMonitor.Provisioning` — factory inventory, bootstrap, activation, and commissioning-session contracts.
- `src/WaterFlex.SaltMonitor.Worker` — asynchronous low-salt alert evaluation and delivery-ticket outbox processing.
- `tests/WaterFlex.SaltMonitor.Tests` — unit tests.

## Create the solution file

Run from this folder:

    dotnet new sln -n WaterFlex.SaltMonitor
    dotnet sln add src/WaterFlex.SaltMonitor.Domain
    dotnet sln add src/WaterFlex.SaltMonitor.Ingestion
    dotnet sln add src/WaterFlex.SaltMonitor.Rules
    dotnet sln add src/WaterFlex.SaltMonitor.Infrastructure
    dotnet sln add src/WaterFlex.SaltMonitor.Api
    dotnet sln add src/WaterFlex.SaltMonitor.Operations
    dotnet sln add src/WaterFlex.SaltMonitor.Provisioning
    dotnet sln add src/WaterFlex.SaltMonitor.Worker
    dotnet sln add tests/WaterFlex.SaltMonitor.Tests

  ## Package source

  Restore uses the repository-level `../NuGet.Config`, which points to Microsoft's public `dotnet-public`
  feed. This avoids `NU1301` failures on networks where `https://api.nuget.org` returns HTTP 403.

  ## Local database

  Development targets PostgreSQL for the database layer and the AWS deployment target is RDS PostgreSQL. From the repository root:

    dotnet tool restore
    dotnet tool run dotnet-ef database update --project backend/src/WaterFlex.SaltMonitor.Infrastructure

  Set `ConnectionStrings__SaltMonitor` to override the database in another environment. Production startup
  requires that setting; the development fallback assumes a local PostgreSQL instance on `localhost:5432`.

  AWS staging runs the API on EC2 and PostgreSQL on private Amazon RDS. Do not install PostgreSQL on the
  application EC2 instance or connect AWS to a developer workstation database. Follow
  [`AWS_RDS_STAGING_RUNBOOK.md`](AWS_RDS_STAGING_RUNBOOK.md) to configure networking, TLS, roles, migrations,
  and the EC2 service. The local-to-RDS copy script is only for an intentional full data migration; it must not
  be used when creating an empty staging database.

  Set `Monitoring__TelemetryIntervalSeconds` to control the expected sensor reporting interval. It defaults to
  60 seconds and accepts 1 through 86,400. A sensor is reporting until it misses three expected reports, stale
  after three misses, and offline after five. Successful telemetry acknowledgements return the configured interval
  so firmware can adopt changes without being reflashed.

  ## Telemetry history retention

  The worker rolls completed raw readings into hourly and daily summaries every 15 minutes. Raw readings are
  retained for 30 days, hourly summaries for 13 months, and daily summaries for 3 years. Cleanup is batched and
  a raw row is deleted only after its hourly and daily summaries exist. Configure the policy with
  `TelemetryHistory__RawRetentionDays`, `TelemetryHistory__HourlyRetentionMonths`,
  `TelemetryHistory__DailyRetentionYears`, `TelemetryHistory__DeleteBatchSize`, and
  `TelemetryHistory__MaintenanceIntervalMinutes`.

  The operations console uses `/readings` for bounded 24-hour raw diagnostics and
  `/history?range=7d&resolution=auto` for completed hourly or daily buckets. History responses include ETags,
  a private 60-second cache policy, and gzip compression when the client requests it.

  ## Device telemetry API

  Authenticated devices submit versioned JSON batches to:

    POST /api/v1/device/telemetry

  Use `Authorization: Bearer <credential-id>.<device-secret>`. The API resolves the active installation and
  calibration server-side, so firmware cannot select its WaterFlex customer, location, tank, or tenant. Duplicate
  `(device, bootId, sequenceNumber)` uploads return successful duplicate acknowledgements.

  Authenticated devices report sensor and controller health separately to:

    POST /api/v1/device/health

  Health heartbeats record sensor status, sensor fault, Wi-Fi RSSI, clock synchronization, queue depth, firmware,
  and uptime without creating a distance or changing fill. Operational telemetry requires quality of at least 70
  and no error flags; faulted samples must use the health endpoint instead. Fleet views retain the last trustworthy
  fill while showing the latest sensor fault, and history rollups exclude historical fault-tagged readings.

  ## Swagger

  Swagger UI and the OpenAPI 3.1 document are available in Development only. Start the API, then open:

    http://localhost:5188/swagger

  The generated document is available at `http://localhost:5188/openapi/v1.json`. To test a protected device
  endpoint, select **Authorize** and enter only `<credential-id>.<device-secret>`; Swagger adds the `Bearer`
  prefix. Device credentials will be issued by the commissioning workflow. The device-health endpoint requires
  that bearer token; only the service liveness endpoint at `GET /health` is anonymous.

  ## Technician provisioning

  The technician UI uses temporary Development/Staging endpoints until WaterFlex staff authentication is connected:

    GET  /api/v1/technician/customers
    POST /api/v1/technician/commission

  These routes remain unavailable in Production. Customer search currently uses a deterministic WaterFlex directory adapter. Commissioning resolves the selected
  customer, location, and tank server-side, then creates the device, installation, calibration, and hashed device
  credential in one serializable transaction. The plaintext device token is returned once. These routes are not
  mapped outside Development; production deployment must replace the directory adapter and protect the route group
  with WaterFlex Entra ID.

  Technician calibration requests use centimeters with one decimal place (`tankDepthCm` and
  `currentDistanceCm`). The commissioning service converts those values to integer millimeters before persistence;
  raw ultrasonic telemetry remains in millimeters.

  ## Bootstrap provisioning foundation

  Bootstrap inventory and pending commissioning are available in Development without changing the existing
  immediate commissioning demo:

    POST /api/v1/factory/devices
    POST /api/v1/device/activate
    POST /api/v1/technician/commissioning-sessions
    GET  /api/v1/technician/commissioning-sessions/{sessionId}
    POST /api/v1/technician/commissioning-sessions/{sessionId}/cancel

  Configure the development factory identity before starting the API:

    $env:FactoryProvisioning__DevelopmentKey = '<local-development-key>'

  Factory registration requires `X-WaterFlex-Factory-Key` and `X-WaterFlex-Factory-Operator` headers. It accepts
  only a Base64-encoded 32-byte SHA-256 bootstrap hash; plaintext bootstrap secrets are generated and retained by
  the controlled factory workstation and are never accepted or returned by the API.

  A technician commissioning session reserves one factory-registered sensor and one WaterFlex tank for 30 minutes.
  It moves the inventory device from `Registered` to `Commissioning` but deliberately creates no installation,
  calibration, or operational credential. Cancellation or pending-session expiry releases the device back to
  `Registered`. The `/api/v1/device/activate` endpoint accepts a bootstrap bearer token and activation payload,
  then creates installation + calibration + operational credential hash and sets the device `Active`.

  Activation requires `Authorization: Bearer <bootstrap-credential-id>.<bootstrap-secret>`. The activation
  payload includes an `activationAttemptId` UUID and a device-generated operational credential hash; plaintext
  operational secrets are never returned by the API.

  Batch factory registration is available via:

    python tools/register_factory_devices.py --csv <path-to-csv> --factory-key <key> --factory-operator <id>

  See `OPERATIONS_CHECKLIST.md` for the standard commissioning order and verification steps.

  ## Work-order commissioning

  A technician can also start commissioning from a WaterFlex work order instead of hand-picking a customer,
  location, and tank:

    GET  /api/v1/technician/installation-work-orders/{workOrderNumber}
    POST /api/v1/technician/work-order-commissioning-sessions

  Looking up a work order resolves its customer, location, and tank together; creating the session reserves a
  factory-registered sensor against that resolved tank the same way `/api/v1/technician/commissioning-sessions`
  does.

  ## Internal operations API

  WaterFlex staff with fleet-operations capability (dealer administrators scoped to their own dealer) use:

    GET  /api/v1/ops/alerts
    GET  /api/v1/ops/alerts/{alertId}
    POST /api/v1/ops/alerts/{alertId}/acknowledge
    POST /api/v1/ops/alerts/{alertId}/approve
    POST /api/v1/ops/alerts/{alertId}/dismiss
    GET  /api/v1/ops/dealers
    GET  /api/v1/ops/fleet/summary
    GET  /api/v1/ops/devices
    GET  /api/v1/ops/devices/{deviceId}
    GET  /api/v1/ops/devices/{deviceId}/readings
    GET  /api/v1/ops/devices/{deviceId}/history

  These back the ops console's fleet, device-detail, and alerts views. Alert transitions
  (acknowledge/approve/dismiss) each accept an `ExpectedRowVersion` to guard against acting on a stale view of
  the alert.

  ## Staff administration API

  WaterFlex staff with staff-management capability manage other staff accounts via:

    GET  /api/v1/staff-admin/staff
    GET  /api/v1/staff-admin/invitations
    POST /api/v1/staff-admin/invitations
    PUT  /api/v1/staff-admin/staff/{staffId}/role
    POST /api/v1/staff-admin/staff/{staffId}/suspend
    POST /api/v1/staff-admin/staff/{staffId}/reactivate

  Role changes and state transitions accept a `RowVersion` to guard against concurrent edits.

## Build, test, run

    dotnet build
    dotnet test
    dotnet run --project src/WaterFlex.SaltMonitor.Api -- --environment Development --urls http://localhost:5188
    dotnet run --project src/WaterFlex.SaltMonitor.Worker
