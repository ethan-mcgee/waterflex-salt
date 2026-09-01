# Ops console (React + Vite + TypeScript)

Internal, WaterFlex-staff-only console for Plan C: fleet health, device/ingestion status,
provisioning, alerts, and OTA. Not customer- or dealer-facing.

The default screen is the internal sensor fleet. The technician provisioning workflow is available at
`/provision`:

1. Look up the installation work order by number. This is a single lookup, not a separate
    customer/location/tank search-and-select UI — one work order number resolves the customer,
    service location, and salt tank together. Technician and dealer come from identity.
2. Enter the sensor's serial number and the tank's usable depth (cm), then reserve the sensor.
    This creates a time-limited commissioning session for that exact serial and tank.
3. Join the sensor's own Wi-Fi access point (named for its serial number) and enter the site's
    2.4 GHz Wi-Fi credentials on the sensor's own captive portal page. This is the only step done
    outside the console, and the only manual input the sensor itself needs.
4. Wait. The console polls the commissioning session automatically while the sensor joins the
    site network, activates, and reports its first health check-in — there is nothing further to
    enter here.
5. Confirmation, once the sensor's first trustworthy telemetry reading arrives.

There is no USB or Web Serial step anywhere in this flow — serial number and tank depth are plain
text/number fields, and no token or credential is ever shown to or handled by the technician.

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
