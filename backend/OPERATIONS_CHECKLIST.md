# WaterFlex Sensor Provisioning Checklist

## Preconditions

- Backend API is running in Development on port 5188.
- `FactoryProvisioning__DevelopmentKey` is set in the shell that runs the API.
- SQL LocalDB migrations are applied.
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
