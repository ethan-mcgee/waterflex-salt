# WaterFlex Salt Monitor

A fleet-monitoring system for WaterFlex water-softener salt levels. An Arduino Nano ESP32 sensor mounted in each
customer's salt tank measures fill level by ultrasonic distance and reports it over HTTPS; a backend ingests that
telemetry, tracks device and tank health, evaluates low-salt rules, and opens WaterFlex delivery tickets
automatically. WaterFlex staff use an internal ops console to provision new sensors, monitor the fleet, and manage
alerts.

## Layout

- `firmware/` — Arduino Nano ESP32 firmware (PlatformIO). Reads tank distance from an A0221AT / DYP-A02
  controlled-UART ultrasonic sensor, provisions Wi-Fi and device credentials via a captive-portal setup flow, and
  uploads batched, idempotent telemetry to the backend.
- `backend/` — .NET 10 API and worker services: multi-tenant telemetry ingestion, fill/level processing, low-salt
  rule evaluation, device provisioning and commissioning, and WaterFlex delivery-ticket integration.
- `web/` — React + Vite + TypeScript internal ops console (WaterFlex staff only) for fleet health, device
  provisioning, alerts, and operations. Not customer- or dealer-facing.
- `docs/` — cross-cutting operational runbooks (field-pilot release, staff access provisioning).

## Finish setup

Run the CLI steps noted in each folder's README to restore dependencies and start each component:

- `firmware/` → `pio run`
- `backend/`  → `dotnet tool restore`, apply the EF migration, then `dotnet build`
- `web/`      → `npm install`, then `npm run dev`

Or bring up the backend, worker, web console, and a local PostgreSQL database together with Docker Compose — see
[`docker-compose.README.md`](docker-compose.README.md).

## Documentation

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — how firmware, backend, and web fit together, and each
  component's internal structure.
- [`docs/PILOT_RELEASE_RUNBOOK.md`](docs/PILOT_RELEASE_RUNBOOK.md) — field-pilot deployment, restore, acceptance,
  rollout, and incident procedures.
- [`docs/STAFF_ACCESS_PROVISIONING.md`](docs/STAFF_ACCESS_PROVISIONING.md) — provisioning WaterFlex staff access to
  the ops console.
- [`firmware/README.md`](firmware/README.md) and [`firmware/PRODUCTION_SECURITY.md`](firmware/PRODUCTION_SECURITY.md)
  — firmware wiring, provisioning behavior, and production-security gates.
- [`backend/README.md`](backend/README.md) — backend projects, local database setup, and the device/technician APIs.
- [`backend/OPERATIONS_CHECKLIST.md`](backend/OPERATIONS_CHECKLIST.md) — standard commissioning order and
  verification steps.

## Continuous integration and staging delivery

GitHub Actions builds and tests the backend, web console, firmware, and deployable containers. Successful changes
on `main` can publish full-commit-tagged images to ECR and deploy through AWS Systems Manager after approval in the
protected `staging` environment. See [`backend/CI_CD_RUNBOOK.md`](backend/CI_CD_RUNBOOK.md).
