# WaterFlex Sensor Provisioning Checklist

## Persistent Bench Path

Use this path for the current ESP32 firmware. Automatic bootstrap activation remains a future firmware step.

1. Start the API on a LAN-reachable address, not only localhost. Set
  `Monitoring__TelemetryIntervalSeconds` to the desired cadence.
2. Before changing the board, open its serial monitor at 115200 and reboot it. If it reports
  `wifiConfigured=true`, `tokenConfigured=true`, and telemetry status 200, reuse the existing NVS identity.
3. To configure or recover without erasing identity, hold D2 for about 5 seconds. Join
  `WaterFlex-<hardwareId>` and open `http://192.168.4.1/`.
4. Submit a 2.4 GHz SSID/password, `http://<server-LAN-IP>:5188/api/v1/device/telemetry`, and a valid
  operational token. During recovery, leave the token blank to retain the stored token.
5. Configuration commits only after the candidate Wi-Fi connection succeeds. Check
  `/api/v1/status` for `status=connected`, `configured=true`, and `hasDeviceToken=true`, then restart.
6. Confirm serial telemetry status 200 and the expected `next=<seconds>s`, then confirm the fleet last-report
  timestamp advances.
7. Power-cycle the board and verify that it reconnects without opening setup. NVS also survives an ordinary
  PlatformIO firmware upload that does not erase flash.
8. To intentionally deprovision the board, hold D2 continuously for 15 seconds. This clears its Wi-Fi, API URL,
  and token. It does not revoke or delete the corresponding backend credential.

The backend stores only the operational token hash. Do not factory-reset a working board unless the plaintext
token is still available or a replacement device credential will be issued.

## Preconditions

- Backend API is running in Development on port 5188.
- `FactoryProvisioning__DevelopmentKey` is set in the shell that runs the API.
- PostgreSQL is reachable and the EF Core migrations are applied. Development defaults to
  `localhost:5432`; AWS staging uses the private RDS endpoint configured by
  `ConnectionStrings__SaltMonitor`.
- Technician and factory operators know their required header values.

## 1. Register Factory Inventory

- Capture per-device tuple in factory records:
  - serialNumber
  - hardwareId (12-char ESP32 hex)
  - bootstrapSecretPlaintext (factory record only)
- Compute `bootstrapSecretHash` as Base64(SHA-256(bootstrapSecretPlaintext)).
- Register using `POST /api/v1/factory/devices` with headers:
  - `X-WaterFlex-Factory-Key`
  - `X-WaterFlex-Factory-Operator`
- Confirm no plaintext secret appears in backend responses or logs.

## 2. Reserve Tank Assignment

- Technician selects customer/location/tank.
- Create session with `POST /api/v1/technician/commissioning-sessions`.
- Confirm session status is `PendingSensor`.

## 3. Write Credentials To Device

- Open setup portal on device.
- Submit `ssid`, `password`, `apiUrl`, and `deviceToken` to `/api/v1/configure`.
- Verify `/api/v1/status` reports `connected` and `hasDeviceToken=true`.

## 4. Activate Bootstrap Device (Optional Flow)

- Device sends `POST /api/v1/device/activate` with:
  - `Authorization: Bearer <bootstrapCredentialId>.<bootstrapSecret>`
  - activation payload including serial/hardware and operational credential hash.
- Confirm activation response status is `activated` or `already_activated`.

## 5. Verify First Telemetry

- Device posts to `POST /api/v1/device/telemetry`.
- Confirm API returns reading status `accepted`.
- Verify operations history:
  - `GET /api/v1/ops/devices/{deviceId}/readings?range=24h`

## 6. Verify Persistence

- Confirm telemetry table mapping in [backend/src/WaterFlex.SaltMonitor.Infrastructure/Persistence/SaltMonitorDbContext.cs](backend/src/WaterFlex.SaltMonitor.Infrastructure/Persistence/SaltMonitorDbContext.cs#L216).
- Confirm reading count increases and duplicate key replays return duplicate acknowledgement instead of new rows.

## 7. Batch Factory Registration Tool

- Use [backend/tools/register_factory_devices.py](backend/tools/register_factory_devices.py) with CSV input.
- Include at minimum these CSV columns:
  - `serialNumber`
  - `hardwareId`
  - `bootstrapSecretPlaintext` or `bootstrapSecretHash`
- Review generated audit CSV for `registered`, `conflict`, and `failed` rows before releasing boards.

## 8. Release a New Factory Firmware Version

Factory workstations no longer keep a local copy of the bundle — the local helper fetches it from
`GET https://telemetry-staging.saltmonitor.dev/api/v1/factory/bundle`, which resolves to whatever `FactoryProvisioning__ApprovedFirmwareVersion`
and `FactoryProvisioning__ConfigurationVersion` the API is currently configured with. Releasing a new
firmware version is a two-step procedure:

1. **Upload the new bundle.** Run the `factory-release` GitHub Actions workflow (`workflow_dispatch`).
   It builds the firmware, creates `waterflex-factory.bin` + `factory-bundle.json`, and uploads them to
   `s3://<STAGING_DEPLOY_BUCKET>/factory-bundles/<firmwareVersion>/<configurationVersion>/`. It also
   publishes a GitHub Release with the staging and production helper `.exe` files attached.
   The workflow verifies the live presigned image checksum and starts the packaged staging EXE before publishing.
   The production EXE remains attached but unavailable until production DNS/ingress and factory provisioning exist.
2. **Redeploy the API** with matching `FactoryProvisioning__ApprovedFirmwareVersion` /
   `FactoryProvisioning__ConfigurationVersion` values (docker-compose env vars for the `api` service).

The order is safe either way: until the redeploy happens, `/configuration` and `/bundle` both keep
serving the previous, still-matching version — no workstation is ever pointed at a firmware version that
hasn't been uploaded yet.

`FACTORY_HELPER_STAGING_API_URL` must be `https://telemetry-staging.saltmonitor.dev`. Updating that GitHub
variable changes only future executable builds; previously published or downloaded EXEs keep their compiled URL.

The API's runtime AWS role needs `s3:GetObject` on `factory-bundles/*` in the deploy bucket to presign
download URLs; the CI publish role needs `s3:PutObject` on the same prefix to upload new bundles.
