# Architecture

This document describes how the three components in this repository fit together and how each one is
structured internally. See the root [`README.md`](../README.md) for setup instructions and the `Layout` section
for a one-paragraph summary of each component's responsibility.

## System shape

```
 firmware (ESP32)  --HTTPS-->  backend API  <---  worker (background jobs)
                                    |
                                    v
                              PostgreSQL
                                    ^
                                    |
                          web ops console (staff)
```

- **firmware** reads tank level from an ultrasonic sensor, provisions itself onto Wi-Fi and against the backend
  (either via the technician-driven immediate-commissioning demo flow or the factory bootstrap self-activation
  flow), and uploads batched, idempotent telemetry over HTTPS.
- **backend API** authenticates devices (bearer `<credential-id>.<device-secret>` tokens), accepts telemetry and
  health heartbeats, resolves the active installation/calibration server-side, and exposes technician and staff
  operations endpoints. It never trusts firmware to select its own customer/location/tank/tenant.
- **backend worker** runs asynchronously: low-salt alert evaluation, the delivery-ticket outbox, staff
  provisioning, and telemetry-history rollup/retention.
- **web ops console** is the only client of the technician/staff/ops endpoints — an internal, WaterFlex-staff-only
  React app for fleet health, provisioning, and alerts.

## Backend layering

The eight backend projects form a layered dependency graph (from each project's `ProjectReference` entries):

```
Domain (core, no dependencies)
  ^
  |-- Ingestion    (telemetry/commissioning contracts, validation)
  |-- Rules        (low-salt threshold evaluation)
  |-- Provisioning (factory inventory, bootstrap, commissioning-session contracts)
       ^
       |-- Operations (fleet/staff query contracts; depends on Ingestion too)
            ^
            |-- Infrastructure (EF Core persistence; aggregates Ingestion, Operations, Provisioning)
                 ^
                 |-- Api    (composition root: also depends on Rules, Ingestion, Operations, Provisioning directly for endpoint contracts)
                 |-- Worker (composition root: also depends on Rules, Ingestion directly)
```

`Domain` holds models, `FillCalculator`, and delivery-ticket contracts with no dependencies of its own —
everything else builds outward from it. `Infrastructure` is the only project that touches EF Core /
PostgreSQL directly; `Api` and `Worker` are the two runnable composition roots and both sit on top of
`Infrastructure`.

XML doc comments on public backend types are surfaced in the generated OpenAPI document — see
[`backend/README.md`](../backend/README.md#swagger) for how to view them via Swagger UI.

## Web console

- **Routing and roles** (`web/src/App.tsx`): React Router v7, with routes gated by role flags
  (`canOperateFleet` / `canProvision` / `canManageStaff`) derived from the current `DevelopmentIdentity`. A
  dev-only "view as role" override persisted to `sessionStorage` lets developers preview role-gated UI without a
  real staff identity. There is no global state library — state is local `useState`/React context only.
- **Identity** (`web/src/development/DevelopmentIdentity.tsx`): a `DevelopmentIdentityProvider` bootstraps the
  current staff identity (Cloudflare Access in Development/Staging today; see `CloudflareAccessAuthentication.cs`
  on the backend) and exposes it via the `useDevelopmentIdentity` hook. `developmentIdentityHeaders()` is called
  by every API client below to attach the identity to outgoing requests.
- **API client pattern**: `ops/api.ts`, `staff/api.ts`, and `provisioning/bootstrapApi.ts` each independently
  implement the same shape — a private `fetch` wrapper that injects `developmentIdentityHeaders()`, parses
  failures as RFC 7807 problem-details JSON (`title`/`detail`/`errors`) into a module-specific `Error` subclass,
  and returns typed JSON. `ops/api.ts` additionally retries once on a 5xx for the history/readings endpoints. This
  pattern is triplicated rather than shared — worth extracting into one module if a fourth API surface is added.
- **Provisioning workflow** (`web/src/provisioning/ProvisioningWorkflow.tsx`): the most complex piece of the
  console. It runs a two-tier state machine:
  1. Before a backend commissioning session exists, a local `PreSessionStep` (`'workOrder' | 'sensor'`) drives
     the UI directly.
  2. Once `createWorkOrderCommissioningSession` succeeds, control shifts to the server: the workflow polls
     `getCommissioningSession` every 4 seconds and drives the UI from `session.status`
     (`pendingSensor` → `activatedAwaitingHealth` → `awaitingFirstTelemetry` → `completed`, or a terminal
     `expired`/`cancelled`/`failed`).

  A derived `RailStepId` renders the 5-item visual step rail from `(step, session)` — it is computed, not stored,
  so the rail always reflects whichever of the two tiers is currently driving the UI.

## Firmware

`firmware/src/main.cpp` runs a single-threaded, non-blocking Arduino-style `setup()`/`loop()`:

- **`setup()`** initializes serial (USB diagnostics and the sensor UART), the recovery-button pin, then restores
  persisted state from NVS (active Wi-Fi profile, device config, bootstrap token, serial number, telemetry
  queue). Depending on the onboard-reset gesture and what was restored, it branches into either the captive-portal
  setup flow (`captive_portal.h`) or straight to `connectWithSavedProfile()` (`wifi_connection.h`), then generates
  a fresh boot ID (`identity_utils.h`).
- **`loop()`** polls every module once per iteration in a fixed order: serial commands and the onboard-reset
  gesture window (`recovery.h`), the captive portal (`captive_portal.h`), Wi-Fi connection state and
  auto-recovery (`wifi_connection.h`), queued telemetry uploads (`telemetry.h`), then reads the sensor
  (`sensor.h`) and processes telemetry (`telemetry.h`) — nothing in the loop blocks for more than one sensor
  read cycle.

Module responsibilities: `storage.h` is the NVS persistence boundary (profile, device config, telemetry queue);
`state.h` holds the in-memory globals every module reads/writes; `identity_utils.h` has the stateless helpers
(boot ID generation, JSON escaping, URL construction); `device_activation.h` implements the factory bootstrap
self-activation exchange; `a02yyuw_uart_parser.h` is the standalone controlled-UART frame parser for the
A0221AT / DYP-A02 sensor. See [`firmware/README.md`](../firmware/README.md) for the full behavioral
specification (provisioning gestures, telemetry batching/retry, TLS trust anchor) and
[`firmware/PRODUCTION_SECURITY.md`](../firmware/PRODUCTION_SECURITY.md) for the secure-boot/flash-encryption
rollout gates.
