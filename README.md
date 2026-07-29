# WaterFlex Salt Monitoring — Plan C

Monorepo for the Arduino Nano ESP32 salt-sensor solution (see `AI-Plans/plan-c-arduino-nano-esp32.md`).

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


