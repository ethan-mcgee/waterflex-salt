# WaterFlex Salt Monitoring — Plan C

Monorepo for the Arduino Nano ESP32 salt-sensor solution (see `AI-Plans/plan-c-arduino-nano-esp32.md`).

Field-pilot deployment, restore, acceptance, rollout, and incident procedures are in
[`docs/PILOT_RELEASE_RUNBOOK.md`](docs/PILOT_RELEASE_RUNBOOK.md). Firmware wiring and production-security gates are in
[`firmware/README.md`](firmware/README.md) and [`firmware/PRODUCTION_SECURITY.md`](firmware/PRODUCTION_SECURITY.md).

## Layout

- `firmware/` — Arduino Nano ESP32 firmware (PlatformIO)
- `backend/`  — .NET 10 multi-tenant ingestion, rules, and WaterFlex ticket integration
- `web/`      — React + Vite + TypeScript internal ops console
- `AI-Plans/` — planning documents (Plan A / B / C)

## Finish setup

Run the CLI steps noted in each folder's README to restore dependencies and start each component:

- `firmware/` → `pio run`
- `backend/`  → `dotnet tool restore`, apply the EF migration, then `dotnet build`
- `web/`      → `npm install`, then `npm run dev`

## Continuous integration and staging delivery

GitHub Actions builds and tests the backend, web console, firmware, and deployable containers. Successful changes
on `main` can publish full-commit-tagged images to ECR and deploy through AWS Systems Manager
after approval in the protected `staging` environment. See [`backend/CI_CD_RUNBOOK.md`](backend/CI_CD_RUNBOOK.md).
