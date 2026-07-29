# Firmware (Arduino Nano ESP32)

PlatformIO project for the Plan C salt sensor. Production firmware sends bounded,
idempotent telemetry batches to the WaterFlex REST API over HTTPS.

## Current provisioning status

The firmware now includes an initial provisioning scaffold:

- Persistent active Wi-Fi profile in NVS (`Preferences`).
- First-boot provisioning entry when no active profile is present.
- Recovery setup input on `D2` (`INPUT_PULLUP`):
	- Hold 5 seconds to open the setup portal.
	- Hold 15 seconds to clear stored Wi-Fi and restart.
- SoftAP captive portal skeleton with wildcard DNS redirect behavior.
- Provisioning routes:
	- `GET /`
	- `GET /api/v1/networks`
	- `POST /api/v1/configure`
	- `GET /api/v1/status`
	- `POST /api/v1/restart`
- Provisioning payload now stores telemetry destination settings in NVS:
	- `apiUrl`
	- `deviceToken`
- Telemetry defaults to a 60-second interval and adopts the API's `nextReportIntervalSeconds` value after every
  successful upload. The API controls this with `Monitoring__TelemetryIntervalSeconds`.
- Wi-Fi, API URL, and token updates are staged and committed together only after Wi-Fi connects. Recovery setup
	can retain the stored token by leaving the token field blank.
- `/api/v1/status` exposes only non-secret persistence diagnostics: `configured`, `hardwareId`,
	`hasDeviceToken`, and `telemetryIntervalSeconds`.
- Automatic recovery portal reopening after prolonged Wi-Fi outage.

Still pending for full production flow:

- Candidate Wi-Fi commit only after full DHCP/DNS/SNTP/API verification.
- Bootstrap activation handshake and idempotent activation attempts.
- Provisional-to-operational credential transition on first telemetry.
- Secure telemetry batching, queue durability, OTA, and rollback flow.

## Prerequisites

- PlatformIO Core (`pip install platformio`) or the PlatformIO VS Code extension.

## Commands

- Build:   `pio run`
- Upload:  `pio run -t upload`
- Monitor: `pio device monitor`

See `../AI-Plans/plan-c-arduino-nano-esp32.md` for wiring and firmware requirements.
