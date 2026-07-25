# Ops console (React + Vite + TypeScript)

Internal, WaterFlex-staff-only console for Plan C: fleet health, device/ingestion status,
provisioning, alerts, and OTA. Not customer- or dealer-facing.

The default screen is the internal sensor fleet. The technician provisioning workflow is available at
`/provision`:

1. Search and select the WaterFlex customer.
2. Select the service location and salt tank.
3. Record the enclosure serial, ESP32 hardware ID, and work order. Technician and dealer come from identity.
4. Enter usable tank depth, connect the powered Nano ESP32 over USB, and capture the current sensor distance;
    the console takes five samples and uses the median to preview fill percentage.
5. Review and commission the sensor, then transfer the one-time device token.

USB sensor capture requires a Chromium browser with Web Serial support and firmware that emits
`distance=<millimeters> mm` diagnostics at 115200 baud. Manual distance entry is intentionally unavailable.

## Setup

    npm install
    npm run dev

Open `http://localhost:3000/`.

The Vite server proxies `/api` to `http://localhost:5188`. Start the backend API in Development before using
the workflow. The technician endpoints are intentionally unavailable in production until WaterFlex staff
authentication is configured.

## Build

    npm run build
    npm run preview
