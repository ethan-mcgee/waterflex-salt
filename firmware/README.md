# Firmware (Arduino Nano ESP32)

PlatformIO project for the Plan C salt sensor. Production firmware sends bounded,
idempotent telemetry batches to the WaterFlex REST API over HTTPS.

COM ports are local workstation settings and are not tracked in `platformio.ini`.
PlatformIO can auto-detect the board, or preserve this workstation's current COM4
selection explicitly:

```powershell
pio run -t upload --upload-port COM4
pio device monitor --port COM4 --baud 115200
```

## Pilot firmware behavior

The firmware now includes an initial provisioning scaffold:

- Persistent active Wi-Fi profile in NVS (`Preferences`).
- First-boot provisioning entry when no active profile is present.
- Recovery setup input on `D2` (`INPUT_PULLUP`):
	- Hold 5 seconds to open the setup portal.
	- Hold 15 seconds to clear stored Wi-Fi and restart.
- Onboard RESET recovery gesture (no external switch required):
	- Press RESET once, wait for the built-in boot LED pulse to finish, then press RESET again within 10 seconds.
	- Do not press twice rapidly; the Nano reserves a rapid double-tap for Arduino bootloader recovery.
	- The second reset clears stored Wi-Fi, API URL, and device token settings.
	- The firmware immediately broadcasts the visible `WaterFlex-XXXXXX` setup network and serves the setup portal at `http://192.168.4.1/`.
- SoftAP captive portal skeleton with wildcard DNS redirect behavior.
- Provisioning routes:
	- `GET /`
	- `GET /api/v1/networks`
	- `POST /api/v1/configure`
	- `GET /api/v1/status`
	- `POST /api/v1/restart`
- Factory NVS contains the unique setup passphrase, serial number, and one-time bootstrap credential. After a pending
  commissioning session exists, firmware generates a random operational secret and stable activation-attempt ID,
  exchanges the bootstrap token idempotently, and stores the operational token only after the API accepts it.
- The pilot build accepts only the approved WaterFlex staging HTTPS telemetry URL. Arbitrary HTTP/HTTPS destinations
  are available only in the separately named `arduino_nano_esp32_development` environment.
- The pilot build requires a unique setup passphrase injected into the `wf_prov/setup_pass` NVS key at the factory.
  It refuses to start the setup AP if that secret is absent. The MAC-derived password exists only in the development
  environment and emits a serial warning when used.
- Candidate Wi-Fi and credentials are committed only after DHCP, SNTP, certificate validation, DNS resolution, and
  an authenticated `/api/v1/device/health` request all succeed.
- Telemetry defaults to a 60-second interval and adopts the API's `nextReportIntervalSeconds` value after every
  successful upload. Up to 24 trustworthy readings are persisted in an NVS circular queue, uploaded in batches of
  eight, and removed only after their exact boot ID and sequence number are acknowledged as accepted or duplicate.
  Retries use capped exponential backoff with jitter. Queue depth and lifetime dropped-reading count are reported in
  device health.
- Invalid or timed-out sensor reads never create a replacement distance. The firmware reports a fault heartbeat to
  `/api/v1/device/health`; only valid measurements are sent to `/api/v1/device/telemetry` and allowed to change fill.
- The selected sensor is the A0221AT / DYP-A02 controlled-UART variant connected through the ASX00061 Nano
  Connector Carrier. Power off before connecting or changing sensor wiring, and set the carrier selector to `3V3`:
	- Sensor black `GND` -> carrier UART `GND`.
	- Sensor red `VCC` -> carrier UART `VCC`.
	- Sensor yellow `RX` -> carrier UART `TX` / Nano `D1`.
	- Sensor white `TX` -> carrier UART `RX` / Nano `D0`.
	- Do not insert loose bare strands directly into a Grove socket. Use a terminated Grove lead, screw-terminal
	  adapter, or another mechanically secure and insulated connection before applying power.
- `Serial1` sends a trigger byte and receives one 9600-baud 8N1 response frame: `0xFF`, distance high byte,
  distance low byte, checksum. The checksum must equal `(0xFF + high + low) & 0xFF`; distance is
  `(high << 8) | low` millimeters and must be within 30-4,500 mm. The parser searches for the header, tolerates noise
  and partial frames, validates checksums, and resynchronizes after a bad frame. No bytes produce `readTimeout`,
  corrupt/partial data produces `invalidSignal`, and a checksum-valid value outside the supported range produces
  `outOfRange`. None become telemetry readings.
- USB serial diagnostics remain 115200 baud. A working sensor prints `distance=<number> mm`; a wiring, protocol, or
  range problem prints `sensor read error fault=<faultCode>`.
- The default telemetry destination is
  `https://telemetry-staging.saltmonitor.dev/api/v1/device/telemetry`. HTTPS validates Cloudflare's edge
  certificate against the embedded Google Trust Services GTS Root R4 trust anchor after SNTP synchronization;
  TLS verification is never disabled. Review the embedded trust anchor before its 2036-06-22 expiration and
  whenever Cloudflare changes the edge certificate issuer.
- Wi-Fi, API URL, and token updates are staged and committed together only after full network/API verification.
  Recovery setup can retain the stored token by leaving the token field blank.
- `/api/v1/status` exposes only non-secret persistence diagnostics: `configured`, `hardwareId`,
	`hasDeviceToken`, and `telemetryIntervalSeconds`.
- Automatic recovery portal opening immediately after a saved Wi-Fi connection times out.

Remaining hardware/manufacturing gates:

- Signed A/B OTA and rollback implementation.
- Enable secure boot, flash/NVS encryption, and debug-port protection only after the dry-run checklist in
  `PRODUCTION_SECURITY.md` succeeds on sacrificial and canary devices. These eFuse changes are intentionally not
  performed by a normal build or upload.

## Prerequisites

- PlatformIO Core (`pip install platformio`) or the PlatformIO VS Code extension.

## Commands

- Build both profiles: `pio run`
- Build pilot only: `pio run -e arduino_nano_esp32`
- Build development only: `pio run -e arduino_nano_esp32_development`
- Upload:  `pio run -t upload`
- Monitor: `pio device monitor`

See `../AI-Plans/plan-c-arduino-nano-esp32.md` for wiring and firmware requirements.
