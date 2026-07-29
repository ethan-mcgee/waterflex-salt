# PROJECT_RECREATION_GUIDE

This document describes the repository as implemented on 2026-07-24. It intentionally distinguishes working code from design-only material. Anything labeled **missing**, **planned**, or **not implemented** must not be treated as available behavior.

## 1. Project Overview

### Business purpose

WaterFlex Salt Monitor is a pilot system for monitoring salt levels in water-softener brine tanks. A top-mounted ultrasonic sensor measures the distance from the sensor face to the salt or water surface. WaterFlex receives the measurement, resolves which customer, site, and tank own the sensor, calculates a fill percentage, persists telemetry, and exposes fleet health to internal staff.

The intended business outcome is proactive salt-delivery operations: identify tanks approaching depletion, eventually create a WaterFlex/RouteFlex delivery ticket after a sustained low condition, and let support staff diagnose sensor, Wi-Fi, firmware, calibration, and installation issues. Automatic delivery creation is designed but not wired into telemetry today.

### Problem being solved

Manual salt checks are periodic, labor-intensive, and easy to miss. A customer-facing application would add onboarding and support cost. This system instead uses an unattended sensor and an internal operations console. The design keeps ownership and calibration on the server so firmware cannot claim another customer's tank.

### Implemented major features

1. **Internal fleet console**: summary counts, dealer/search/status/fill filters, sorting, paging, latest level, reporting state, signal quality, firmware, and errors.
2. **Device detail**: installation mapping, latest level and diagnostics, calibration metadata, credential status, and bounded reading history.
3. **Legacy immediate commissioning**: a Development-only dealer-technician workflow that selects fixture customer data, records sensor identity and tank depth, captures a bench reading over Web Serial, creates the complete installation, and returns an operational token to the browser once.
4. **Factory bootstrap foundation**: Development-only registration of factory inventory with a hash-only bootstrap credential.
5. **Pending commissioning sessions**: a factory-registered device and tank can be reserved for 30 minutes; get and cancel operations are dealer-scoped. This currently stops at `PendingSensor`.
6. **Authenticated telemetry ingestion**: strict JSON validation, device bearer authentication, rate limiting, server-side ownership/calibration resolution, fill calculation, SQL persistence, and duplicate acknowledgement.
7. **Development API documentation**: OpenAPI 3.1 and Swagger UI.
8. **Firmware UART skeleton**: reads the DFRobot A02YYUW frame, validates its checksum, and writes `distance=<millimeters> mm` to USB serial.

### Designed but not implemented

- Bootstrap bearer authentication and `POST /api/v1/device/activate`.
- Activation idempotency, provisional operational credentials, first-telemetry completion, and bootstrap consumption.
- Android SoftAP/captive-portal setup, production Wi-Fi/TLS, durable telemetry queue, and OTA.
- Production WaterFlex/Entra ID authentication and real WaterFlex customer directory.
- Recalibrate, replace, and retire operations.
- Trigger debounce, ticket/outbox persistence, delivery processing, firmware campaigns, notifications, maps, and customer/dealer-facing portals.
- Containers, CI/CD, infrastructure-as-code, production hosting manifests, distributed monitoring, and production deployment automation.

### User workflows

#### WaterFlex employee fleet workflow

1. Select the seeded WaterFlex employee identity in Development.
2. Open `/fleet`.
3. Filter active installations by dealer, reporting state, or low fill; search by customer, address, tank, serial, or hardware ID.
4. Open `/fleet/{deviceId}` to inspect mapping, calibration, credential state, latest diagnostics, and history.

#### Dealer technician legacy workflow

1. Select a seeded dealer-technician identity.
2. Open `/provision`.
3. Select fixture account, service location, and tank.
4. Enter serial, hardware ID, model, and optional work order.
5. Enter tank depth and read five USB serial samples from the Nano; use the median if spread is at most 100 mm.
6. Review and call `POST /api/v1/technician/commission`.
7. Copy the one-time operational token returned to the browser.

This is a transitional Development workflow. It bypasses bootstrap inventory and is not available outside Development.

#### Bootstrap foundation workflow

1. A factory client sends sensor identity plus a SHA-256 bootstrap-secret hash to `POST /api/v1/factory/devices`.
2. The API creates a `Registered` inventory device and bootstrap credential. It never receives the plaintext bootstrap secret.
3. A dealer technician calls `POST /api/v1/technician/commissioning-sessions` with customer/tank/depth and the registered serial.
4. The API moves the device to `Commissioning` and reserves device and tank for 30 minutes without creating an installation, calibration, or operational credential.
5. The technician can query or cancel the session. Expiry is lazy on access and releases a still-pending device.
6. Activation beyond this point is not implemented.

#### Device telemetry workflow

1. Active device posts a batch to `/api/v1/device/telemetry` with a unique bearer token.
2. Authentication hashes and fixed-time compares the secret, confirms the device is Active, and updates credential last use.
3. The validator rejects malformed ownership fields, unsupported schema, invalid ranges, future time, and duplicate keys inside a batch.
4. A serializable transaction resolves current installation and calibration, deduplicates persisted keys, calculates fill, and inserts new readings.
5. The response marks each reading `accepted` or `duplicate` and returns a 3,600-second reporting interval.

### User and machine roles

| Role | Implemented identity | Permissions | Limitations |
|---|---|---|---|
| WaterFlex employee | Development user `wf-ops-alex`; role `waterFlexEmployee` | Fleet/dealer/detail/history routes | Header filter only; not production authentication |
| Dealer technician | Development users `north-star-jordan` and `lakes-water-sam`; role `dealerTechnician` | Fixture customer search, legacy commission, create/get/cancel own dealer sessions | No production identity; no cross-dealer access |
| Factory operator/workstation | `X-WaterFlex-Factory-Key` plus `X-WaterFlex-Factory-Operator` | Development factory device registration | Static configured key; no machine certificate, rotation, or production policy |
| Active sensor | `Authorization: Bearer <credential-id>.<device-secret>` | Telemetry only | Requires Active device and current install/calibration |
| Bootstrap sensor | Database model only | Intended activation only | Authentication handler and endpoint are missing |
| Customer/homeowner | None | None | Deliberately no application |

### Technical goals

- Per-device credentials rather than a fleet-wide shared secret.
- Server-owned customer, tank, installation, and calibration mapping.
- Idempotent telemetry using `(DeviceId, BootId, SequenceNumber)`.
- Integer millimeters for sensor/calibration persistence.
- Transactional commissioning and telemetry writes.
- Explicit device, credential, installation, and calibration lifecycle records.
- Internal operational visibility without exposing secrets.
- Offline-capable firmware and retry-safe bootstrap as future work.

### Architecture pattern

The backend is a layered modular monolith using ASP.NET Core Minimal APIs and EF Core. Domain is package-free. Contract projects (`Ingestion`, `Operations`, `Provisioning`) depend only on Domain. Infrastructure implements all contracts and owns SQL persistence. API composes middleware and endpoints. Worker is a separate host but currently only logs heartbeats. The React SPA is a separate Vite application. Firmware is a PlatformIO project.


### Core technologies

- C# and .NET 10 / ASP.NET Core Minimal APIs.
- EF Core 10 with SQL Server.
- React 18, React Router 7, Radix Select, Lucide icons.
- TypeScript and Vite 5.
- Arduino framework on Arduino Nano ESP32 through PlatformIO.
- xUnit and ASP.NET Core `WebApplicationFactory` integration testing.

### External dependencies

- SQL Server is the only implemented runtime service dependency.
- Microsoft public `dotnet-public` Azure DevOps NuGet feed is the only configured NuGet source.
- The checked-in NPM lock resolves from the public npm registry (`https://registry.npmjs.org/`).
- A02YYUW ultrasonic sensor and Arduino Nano ESP32 are physical dependencies.
- Browser Web Serial is used only by the current bench calibration path.
- WaterFlex customer lookup is a fixture, not a live integration.
- RouteFlex delivery is a non-registered, non-invoked stub.

---

## 2. Technology Stack

### Frontend

| Technology | Version | Purpose and rationale | Important configuration |
|---|---:|---|---|
| React | manifest `^18.3.1`; lock `18.3.1` | Component UI and local state. Chosen for a small interactive operations SPA. | `StrictMode` root; no React compiler configuration |
| React DOM | `18.3.1` | Browser rendering through `createRoot`. | Mounts to `#root` in `web/index.html` |
| React Router DOM | manifest `^7.18.1`; lock `7.18.1` | Client routes and URL-backed fleet filters. | `BrowserRouter`; server hosting must rewrite unknown paths to `index.html` |
| Radix UI Select | `2.3.5` | Accessible, themeable dropdowns in header, fleet, and provisioning. | Portaled popper content; empty string mapped to an internal sentinel |
| Lucide React | `1.25.0` | All interface icons. | No bitmap/image asset pipeline exists |
| TypeScript | manifest `^5.6.3`; lock `5.9.3` | Source typing. | No `tsconfig.json`; Vite/esbuild transpiles but does not perform a standalone typecheck |
| Vite | manifest `^5.4.9`; lock `5.4.21` | Dev server and production bundle. | Port 3000, `strictPort`, `/api` proxy to `http://localhost:5188` |
| `@vitejs/plugin-react` | manifest `^4.3.2`; lock `4.7.0` | React JSX and Fast Refresh. | Only Vite plugin configured |
| CSS | native CSS, 2,543 lines | One global design system and all component/responsive styles. | Breakpoints 1180, 820, 580 px; WaterFlex blue variables; no CSS modules/preprocessor |
| State management | React state/context only | Avoids dependency overhead for current scope. | Identity context + local component state + URL search params |
| Package manager | npm; current machine 11.16.0 | Lockfile-based install. | Run from `web`; root lock is empty |

Vite 5.4 requires Node `^18.0.0 || >=20.0.0`. The observed machine uses Node `24.18.0`; use Node 20 LTS or newer for reconstruction.

### Backend

| Technology/library | Version | Purpose and rationale | Important configuration |
|---|---:|---|---|
| .NET SDK | `10.0.300`, latest patch; observed `10.0.302` | Build/runtime baseline. | `global.json` disables prerelease and rolls to latest patch only |
| Target framework | `net10.0` | All backend and tests. | Inherited from `backend/Directory.Build.props` |
| C# | `LangVersion=latest` | Records, collection expressions, primary constructors, Minimal APIs. | Nullable and implicit usings enabled; warnings are not errors |
| ASP.NET Core Web SDK | .NET 10 | Minimal API host, middleware, auth, rate limiting, Problem Details. | No MVC controllers |
| Microsoft.AspNetCore.OpenApi | `10.0.10` | OpenAPI 3.1 generation. | Development only; custom document and operation transformers |
| Swashbuckle Swagger UI | `10.2.3` | Interactive Development docs. | `/swagger`, document `/openapi/v1.json` |
| EF Core SQL Server | `10.0.10` | ORM, migrations, transactions, execution retries. | SQL Server provider with `EnableRetryOnFailure`; migrations are manual |
| EF Core Design | `10.0.10`, private assets | Migration generation/design-time factory. | Local tool `dotnet-ef 10.0.10` |
| Microsoft.Extensions.Hosting | `10.0.10` | Worker host. | Worker registers only heartbeat service |
| ASP.NET Core rate limiting | framework | Fixed-window device telemetry protection. | 10 requests/device/minute; no queue; in-process only |
| System.Text.Json | framework | Strict body binding and persisted error flags/audit details. | Camel-case enum strings; unknown JSON members rejected |
| xUnit | `2.9.2` | Unit/integration tests. | Test parallelization disabled because LocalDB migration locks race |
| Microsoft.NET.Test.Sdk | `17.11.1` | Test discovery/execution. | 35 discovered cases |
| Microsoft.AspNetCore.Mvc.Testing | `10.0.10` | In-process API integration tests. | Per-test GUID LocalDB databases |
| xUnit VS runner | `2.8.2` | IDE/VSTest adapter. | Test-only |

### Database

| Technology | Version | Purpose | Important configuration |
|---|---:|---|---|
| SQL Server Express LocalDB | observed `17.0.4025.3 RTM`, 64-bit Express | Local development and all integration tests. | Instance `(localdb)\MSSQLLocalDB`; auto-created; Windows only |
| Microsoft.Data.SqlClient | transitive through EF Core | SQL protocol driver. | Integrated Windows authentication locally |
| EF migrations | `10.0.10` | Versioned schema creation. | Four migrations; `__EFMigrationsHistory` |
| SQLCMD | observed `15.0.1300.359` | Optional inspection/troubleshooting. | Not used by application startup |

No database seed migration exists. Development fixture customers live in C# and are persisted only when commissioning/session creation upserts selected records.

### Infrastructure

| Area | Implemented state |
|---|---|
| Hosting | Local Kestrel API and Vite dev server only. A WaterFlex-hosted public environment is an agreed target but has no repository definition. |
| Containers | None. No Dockerfile or Compose file. |
| Storage | SQL Server; no object storage/cache/queue. |
| Monitoring | Default ASP.NET/EF logs and worker heartbeat only. |
| Authentication provider | Device bearer scheme implemented; staff/factory are Development endpoint filters. No Entra ID/OIDC. |
| Networking | Vite proxy in development; no reverse proxy, load balancer, CORS policy, DNS, or certificate manifests. |
| Secrets | Environment/configuration only; no vault integration. |

---

## 3. Development Environment Setup

### Operating System Requirements

The exact repository and test suite require **64-bit Windows** because every EF/API integration test hard-codes SQL Server LocalDB. The observed environment is Windows. The API itself can run anywhere supported by .NET 10 if `ConnectionStrings__SaltMonitor` points to a reachable SQL Server, but tests must be rewritten to use a portable SQL fixture before Linux/macOS can reproduce them unchanged.

Use PowerShell 5.1 or newer for the commands below. Paths containing spaces must be quoted.

### Required Software

#### Git

Observed version: `2.55.0.windows.3`.


Git is required only for repository transport/version control, not runtime.

#### Node.js and npm

Observed versions: Node `24.18.0`, npm `11.16.0`. Minimum dictated by Vite: Node 18 or 20+; use Node 20/22/24 LTS-current rather than old Node 18.


If the lock file needs to be regenerated (e.g. after adding or upgrading packages), run `npm install` inside `web/` against the public npm registry, then commit the updated `package-lock.json`.

Regenerating changes transitive lock versions and should be reviewed.

#### Yarn and PNPM

Neither is used. Do not install them for exact recreation. If an organizational policy requires one, convert the lockfile intentionally; do not mix package managers. Optional verification:


#### .NET SDK

Required baseline: SDK `10.0.300`; observed patch `10.0.302`.


The root `global.json` selects `10.0.300`, allows latest patch, and rejects prerelease SDKs.

#### SQL Server Express LocalDB

Install a SQL Server Express release that includes LocalDB. The observed instance is `17.0.4025.3`. Use the Microsoft SQL Server Express installer, select **LocalDB**, and verify:


Expected connection name: `(localdb)\MSSQLLocalDB`.

Install SQLCMD optionally for inspection:


#### Python and PlatformIO

Python is not used by web/backend. It is needed only to install PlatformIO Core. Observed Python is `3.14.6`, but PlatformIO was not installed on the observed machine. Use a PlatformIO-supported Python release, preferably 3.11-3.13 if compatibility with 3.14 is uncertain.


Alternatively install the PlatformIO VS Code extension. Firmware dependencies are not pinned, so first resolution may differ over time.

#### Java

Not used by any project, build, or tool. The observed machine has Amazon Corretto 21, but reconstruction does not require Java.


#### Docker and Docker Compose

Not used and not installed in the observed environment. There are no Dockerfiles or Compose definitions. Docker cannot run this repository without first authoring container assets. Optional installation for future work:


#### Browsers

Use current Microsoft Edge or Google Chrome. Chromium is required for the Development bench Web Serial path. Firefox and Safari do not implement Web Serial. Production Android SoftAP setup is planned but not implemented.


Web Serial works only in a secure context (`https://` or localhost) and requires an explicit user gesture to choose a port.

### Initial verification


---

## 4. Repository Structure

### Actual tree

Generated `bin`, `obj`, `dist`, `.pio`, and `node_modules` directories are omitted.


### Folder relationships

- `AI-Plans` is design history. Plan C drives intent, but only source code defines current behavior.
- `backend/src` is a modular monolith. Domain is at the center; contract projects point inward; Infrastructure points to contracts; API points to all; Worker points to core projects.
- `backend/tests` tests domain, Infrastructure, and API using LocalDB and in-process hosting.
- `web` is independently restored and built with npm. It talks to API through relative `/api` URLs.
- `firmware` is independent from .NET/web builds. It currently provides only sensor UART/USB diagnostics.

### Important root files

- `global.json`: exact SDK baseline and roll-forward policy.
- `dotnet-tools.json`: local EF CLI pin.
- `NuGet.Config`: clears all sources and uses Microsoft `dotnet-public` only.
- `backend/Directory.Build.props`: target framework and compiler defaults inherited by backend/tests.
- Root `WaterFlex.SaltMonitor.slnx`: empty artifact; do not use for builds.
- Root `package-lock.json`: empty artifact; do not run root `npm ci` expecting the web app.
- `.gitignore`: excludes all generated output and all `.env`/`*.env`/`*.local` secrets.

---

## 5. Project Creation Sequence

The commands below recreate project scaffolding and dependencies. Source behavior still has to be implemented according to Sections 7-15.

### 1. Create repository and root files


Create `NuGet.Config` with only:


Create the root `.gitignore` from Section 16 before generating builds.

### 2. Create the authoritative backend solution


Delete generated `Class1.cs`, generated Worker, generated test, and generated API sample content before implementing source.

Create `backend/Directory.Build.props`:


### 3. Add internal references


### 4. Add NuGet dependencies


Set `PrivateAssets=all` and the Design `IncludeAssets` in the Infrastructure project. Set test project `IsPackable=false` and `IsTestProject=true`.

### 5. Implement backend in dependency order

1. Domain models, fill calculation, monitoring policy, staff actor, ticket abstraction.
2. Ingestion DTOs, validator, result interfaces.
3. Operations read contracts.
4. Provisioning factory/session contracts.
5. EF entities and `SaltMonitorDbContext` mappings.
6. Infrastructure services and development fixtures.
7. API authentication handlers/filters, endpoint modules, middleware, OpenAPI.
8. Worker heartbeat/stub.
9. Tests.

The detailed behavior is in Sections 7, 9, 10, and 13.

### 6. Create database migrations in order

After implementing each schema stage:


Use Infrastructure as startup because it contains `IDesignTimeDbContextFactory` and EF Design. Using API as startup currently fails unless API directly references EF Design.

### 7. Create frontend

To reproduce exact dependencies without relying on a modern Vite template:


Edit `package.json` to set `private`, `type: module`, and scripts `dev`, `build`, `preview`. Create `index.html`, `vite.config.ts`, and source hierarchy from Section 4. Do not assume `npm run build` typechecks; add a `tsconfig` and `tsc --noEmit` only if intentionally improving the reconstructed project.

### 8. Create firmware project


Edit `platformio.ini` to use environment `arduino_nano_esp32`, platform `espressif32`, board `arduino_nano_esp32`, framework `arduino`, and `monitor_speed=115200`. Implement `src/main.cpp` wiring and parser from Section 11/15. No libraries are pinned today.

### 9. Restore, migrate, build, and run


Start API before web because Vite proxies API requests. Startup order appears in Section 23.

---

## 6. Dependency Analysis

### NuGet dependencies

| Project | Dependency/version | Purpose and use | Failure if removed |
|---|---|---|---|
| Infrastructure | `Microsoft.EntityFrameworkCore.SqlServer 10.0.10` | SQL Server provider, mappings, migrations, transactions, retry strategy | DbContext cannot configure SQL Server; persistence and tests fail |
| Infrastructure | `Microsoft.EntityFrameworkCore.Design 10.0.10` | Migration generation/design-time services | `dotnet-ef` migration commands fail |
| Infrastructure | `Microsoft.Extensions.Hosting.Abstractions 10.0.10` | `IHostEnvironment` and DI abstractions | persistence registration cannot select Development fallback |
| API | `Microsoft.AspNetCore.OpenApi 10.0.10` | `MapOpenApi`, document/operation transformers | OpenAPI route and custom security documentation fail |
| API | `Swashbuckle.AspNetCore.SwaggerUI 10.2.3` | Swagger static UI middleware | `/swagger` disappears; OpenAPI JSON still could exist |
| Worker | `Microsoft.Extensions.Hosting 10.0.10` | Worker SDK hosting and `BackgroundService` | worker cannot build/run |
| Tests | `Microsoft.AspNetCore.Mvc.Testing 10.0.10` | `WebApplicationFactory<Program>` API tests | HTTP integration tests fail |
| Tests | `Microsoft.NET.Test.Sdk 17.11.1` | VSTest integration/discovery | `dotnet test` cannot discover/execute normally |
| Tests | `xunit 2.9.2` | facts, theories, assertions | all tests fail to compile |
| Tests | `xunit.runner.visualstudio 2.8.2` | runner adapter | IDE/VSTest discovery fails |

### Direct NPM dependencies

| Dependency/version | Purpose and use | Failure if removed |
|---|---|---|
| `react 18.3.1` | Components, state, effects, transitions/context | Entire SPA fails |
| `react-dom 18.3.1` | `createRoot` browser renderer | SPA cannot mount |
| `react-router-dom 7.18.1` | BrowserRouter, routes, links, params/search params | Navigation, deep links, filters, detail routing fail |
| `@radix-ui/react-select 2.3.5` | Accessible custom selects | Identity, fleet filters, model selector fail |
| `lucide-react 1.25.0` | All icons | Imports fail and most controls lose iconography |
| `vite 5.4.21` (dev) | Dev server/build | No frontend build or dev server |
| `@vitejs/plugin-react 4.7.0` (dev) | JSX transform/Fast Refresh | Vite React source processing fails |
| `typescript 5.9.3` (dev lock) | TypeScript dependency/tooling | Editor/tooling types degrade; Vite uses esbuild for transpile |
| `@types/react 18.3.31` (dev) | React declarations | TS React source loses type definitions |
| `@types/react-dom 18.3.7` (dev) | React DOM declarations | `createRoot` typing fails |

### Locked transitive NPM inventory

These packages are not imported by application source. They implement Babel/Vite transforms, browser target data, Radix focus/portal/popper behavior, React scheduling/scroll locking, CSS processing, and platform-specific binaries. Removing an arbitrary transitive package corrupts `npm ci` or the dependent direct package. Recreate them from the lock rather than managing them independently.


### Maven and Java dependencies

None. There is no Maven/Gradle project and Java is not required.

### Python dependencies

No `requirements.txt` or Python source exists. PlatformIO is an external Python-distributed tool used to build firmware and must be installed separately. No PlatformIO version is pinned.

### Firmware dependencies

`platformio.ini` selects unversioned `espressif32`, board `arduino_nano_esp32`, and Arduino framework. There are no active `lib_deps`. Reproducibility risk: a fresh resolve may select a newer platform/core.

### Internal dependency graph


---

## 7. Backend Architecture

### Application layers

#### Domain

`WaterFlex.SaltMonitor.Domain` is package-free and owns stable business concepts:

- `SensorReading`, `TankCalibration`, delivery request/result records.
- `FillCalculator`: $fill=clamp((tankDepth-measuredDistance)/tankDepth*100,0,100)$; throws when depth is not positive.
- `MonitoringPolicy`: strict `<35%`; Reporting through 2 hours, Stale after 2 through 6 hours, Offline after 6, NeverReported when null.
- `StaffActor` and `StaffRole`.
- `IDeliveryTicketGateway` abstraction.

It depends on nothing internal or external.

#### Ingestion contracts/application boundary

- Telemetry request/acknowledgement records.
- `TelemetryBatchValidator`.
- `ITelemetryIngestionService` and result/failure types.
- `IDeviceTokenValidator` and token result/failure types.
- Fixture directory and legacy immediate commissioning contracts.

It depends only on Domain.

#### Operations contracts

Defines fleet filters, sorts, summaries, list/detail/history DTOs, and `IFleetQueryService`. It depends only on Domain monitoring types.

#### Provisioning contracts

Defines factory registration and commissioning-session states, requests, views, failures, validation errors, and service interfaces. It depends only on Domain staff identity.

#### Rules

Contains only `LowSaltEvaluator`, a thin wrapper around Domain's threshold. It is not called by telemetry or Worker.

#### Infrastructure

Owns all concrete services and the database:

- `SaltMonitorDbContext` and design-time factory.
- EF entities/migrations.
- Development customer and identity fixtures.
- Credential validation.
- Telemetry ingestion.
- Legacy immediate commissioning.
- Fleet read model.
- Factory inventory registration.
- Commissioning-session reservation/status/cancellation.
- Delivery gateway stub.

No separate repository classes exist; services query DbSets directly.

#### API

Uses Minimal APIs, not controllers. `Program.cs` composes:

1. Kestrel 64 KiB body limit.
2. Problem Details.
3. OpenAPI transformers.
4. camel-case enum/unknown-member JSON policy.
5. persistence DI.
6. device authentication scheme.
7. authorization services.
8. telemetry rate limiter.
9. exception handler.
10. authentication, authorization, rate limiter.
11. endpoint maps.

Development additionally maps Swagger, identities, technician, factory, and operations routes. Outside Development, only health and telemetry are mapped.

#### Worker

Separate Generic Host. Registers only `DeliveryOutboxWorker`, which logs every 30 seconds. It does not register DbContext or ticket gateway and cannot process an outbox.

### Controllers and endpoints

There are no controllers. Endpoint ownership:

- `Program.cs`: health, development users, customer search, legacy commission, telemetry.
- `ProvisioningEndpoints.cs`: factory and commissioning-session routes.
- `OpsEndpoints.cs`: fleet routes.

### Middleware execution flow


### Telemetry request flow


### Immediate commissioning flow

A serializable transaction:

1. Validates trusted technician role/dealer.
2. Normalizes IDs/serial/hardware/model/work order.
3. Resolves fixture customer selection.
4. Rejects duplicate device and active tank.
5. Upserts dealer, customer, location, and tank.
6. Creates Active device, installation, calibration v1, and credential.
7. Generates random 12-byte credential ID suffix and 32-byte secret.
8. Stores SHA-256 secret hash.
9. Returns plaintext `credentialId.base64url(secret)` once.

### Bootstrap foundation flow

Factory registration and pending session are implemented. Activation is not.


### Utilities and validators

- Normalization is implemented privately inside services rather than shared utilities.
- Unique SQL violations 2601/2627 are translated into domain conflicts.
- Telemetry retries once after a uniqueness race by clearing the change tracker.
- `TimeProvider` is injected for deterministic tests.
- No event bus or event handlers exist.

---

## 8. Database Architecture

### Engine and model conventions

- SQL Server via EF Core 10.
- Local development database: `WaterFlexSaltMonitor` on `(localdb)\MSSQLLocalDB`.
- All FKs use `ON DELETE NO ACTION`/Restrict.
- No cascade delete, triggers, check constraints, temporal tables, or explicit SQL defaults.
- IDs are generated in application code with `Guid.NewGuid()` except bigint identity audit/reading IDs.
- `rowversion` columns are SQL-generated concurrency tokens.
- Enum columns are strings to retain readable lifecycle values.

### Schema

Important distinction: the current codebase has no `WorkOrders` table. Work-order references are stored only as nullable external identifiers on `DeviceInstallations` and `CommissioningSessions`. Add a first-class `WorkOrders` table separately if the provisioning design needs independent work-order validation.

#### `Dealers`

Purpose: stable dealer ownership used by installations and commissioning sessions.

| Column | SQL type | Null | Constraints |
|---|---|---:|---|
| Id | uniqueidentifier | No | PK |
| ExternalId | nvarchar(64) | No | Unique index |
| DisplayName | nvarchar(200) | No | None |
| IsActive | bit | No | None |

Relationships: one dealer to many optional `DeviceInstallations`; one dealer to many `CommissioningSessions`.

#### `CustomerAccounts`

| Column | SQL type | Null | Constraints |
|---|---|---:|---|
| Id | uniqueidentifier | No | PK |
| WaterFlexCustomerId | nvarchar(128) | No | Unique |
| AccountNumber | nvarchar(64) | Yes | None |
| DisplayName | nvarchar(200) | No | None |
| IsActive | bit | No | None |
| LastSyncedAtUtc | datetimeoffset | No | None |

One account has many service locations.

#### `ServiceLocations`

| Column | SQL type | Null | Constraints |
|---|---|---:|---|
| Id | uniqueidentifier | No | PK |
| CustomerAccountId | uniqueidentifier | No | FK to CustomerAccounts |
| WaterFlexLocationId | nvarchar(128) | No | Composite unique with CustomerAccountId |
| DisplayName | nvarchar(200) | No | None |
| AddressSummary | nvarchar(500) | Yes | None |
| IsActive | bit | No | None |
| LastSyncedAtUtc | datetimeoffset | No | None |

Indexes: unique `(CustomerAccountId, WaterFlexLocationId)`. One location has many tanks.

#### `Tanks`

| Column | SQL type | Null | Constraints |
|---|---|---:|---|
| Id | uniqueidentifier | No | PK |
| ServiceLocationId | uniqueidentifier | No | FK/index |
| WaterFlexAssetId | nvarchar(128) | Yes | No uniqueness constraint |
| Label | nvarchar(100) | No | None |
| CapacityPounds | int | Yes | None |
| IsActive | bit | No | None |

Risk: service code assumes `(ServiceLocationId, WaterFlexAssetId)` identifies at most one row, but the database does not enforce it.

#### `Devices`

| Column | SQL type | Null | Constraints |
|---|---|---:|---|
| Id | uniqueidentifier | No | PK |
| SerialNumber | nvarchar(64) | No | Unique |
| HardwareId | nvarchar(32) | No | Unique |
| Model | nvarchar(100) | No | None |
| Status | nvarchar(32) | No | `Registered`, `Commissioning`, `Active`, `Retired` |
| RegisteredAtUtc | datetimeoffset | No | None |
| CommissionedAtUtc | datetimeoffset | Yes | None |
| RetiredAtUtc | datetimeoffset | Yes | None |
| FactoryFirmwareVersion | nvarchar(64) | Yes | None |
| FactoryConfigurationVersion | nvarchar(64) | Yes | None |
| FactoryProvisionedBy | nvarchar(200) | Yes | None |

One device has many operational credentials, bootstrap credentials, installations, and sessions.

#### `DeviceCredentials`

Purpose: operational telemetry bearer credentials.

| Column | SQL type | Null | Constraints |
|---|---|---:|---|
| Id | uniqueidentifier | No | PK |
| DeviceId | uniqueidentifier | No | FK/index |
| CredentialId | nvarchar(64) | No | Unique |
| SecretHash | varbinary(32) | No | SHA-256 hash |
| ValidFromUtc | datetimeoffset | No | None |
| ExpiresAtUtc | datetimeoffset | Yes | None |
| RevokedAtUtc | datetimeoffset | Yes | None |
| LastUsedAtUtc | datetimeoffset | Yes | None |

Multiple credentials per device allow rotation, although rotation APIs are missing.

#### `DeviceBootstrapCredentials`

Purpose: factory identity for future activation.

| Column | SQL type | Null | Constraints |
|---|---|---:|---|
| Id | uniqueidentifier | No | PK |
| DeviceId | uniqueidentifier | No | FK |
| CredentialId | nvarchar(64) | No | Unique |
| SecretHash | varbinary(32) | No | Exactly 32 bytes in EF model |
| ValidFromUtc | datetimeoffset | No | None |
| ExpiresAtUtc | datetimeoffset | Yes | None |
| RevokedAtUtc | datetimeoffset | Yes | None |
| ConsumedAtUtc | datetimeoffset | Yes | None |
| LastUsedAtUtc | datetimeoffset | Yes | None |
| FailedAttemptCount | int | No | Application default 0 |
| RowVersion | rowversion | No | Concurrency token |

Filtered unique index: one unrevoked/unconsumed bootstrap credential per device.

#### `DeviceInstallations`

| Column | SQL type | Null | Constraints |
|---|---|---:|---|
| Id | uniqueidentifier | No | PK |
| DeviceId | uniqueidentifier | No | FK |
| TankId | uniqueidentifier | No | FK |
| DealerId | uniqueidentifier | Yes | FK/index; legacy rows can be unassigned |
| InstalledAtUtc | datetimeoffset | No | None |
| RemovedAtUtc | datetimeoffset | Yes | Active when null |
| InstalledBy | nvarchar(200) | Yes | None |
| WaterFlexWorkOrderId | nvarchar(128) | Yes | None |
| RowVersion | rowversion | No | Concurrency token |

`WaterFlexWorkOrderId` is only an external reference; there is no foreign key or lookup table for work orders in the current model.

Filtered unique indexes enforce one active installation per device and per tank where `RemovedAtUtc IS NULL`.

#### `TankCalibrations`

| Column | SQL type | Null | Constraints |
|---|---|---:|---|
| Id | uniqueidentifier | No | PK |
| DeviceInstallationId | uniqueidentifier | No | FK |
| Version | int | No | Unique with installation |
| TankDepthMm | int | No | None |
| CommissioningDistanceMm | int | No | Stored audit/preview value |
| EffectiveFromUtc | datetimeoffset | No | None |
| EffectiveToUtc | datetimeoffset | Yes | Active when null |
| CreatedBy | nvarchar(200) | No | None |
| CreatedAtUtc | datetimeoffset | No | None |

Indexes: unique `(DeviceInstallationId, Version)` and one active calibration per installation where `EffectiveToUtc IS NULL`.

#### `TelemetryReadings`

| Column | SQL type | Null | Constraints |
|---|---|---:|---|
| Id | bigint IDENTITY | No | PK |
| DeviceId | uniqueidentifier | No | FK |
| DeviceInstallationId | uniqueidentifier | No | FK |
| TankCalibrationRecordId | uniqueidentifier | No | FK |
| BootId | uniqueidentifier | No | Dedupe key component |
| SequenceNumber | bigint | No | Dedupe key component |
| ObservedAtUtc | datetimeoffset | Yes | Sensor time |
| ReceivedAtUtc | datetimeoffset | No | Server time |
| UptimeMilliseconds | bigint | No | None |
| RawDistanceMm | int | No | None |
| FillPercent | float | No | Server-computed |
| Quality | int | No | None |
| SampleCount | int | No | None |
| WifiRssiDbm | int | No | None |
| FirmwareVersion | nvarchar(64) | No | None |
| ErrorFlagsJson | nvarchar(2048) | No | JSON string array |

Indexes: unique `(DeviceId, BootId, SequenceNumber)`, `(DeviceInstallationId, ReceivedAtUtc)`, and calibration FK.

#### `CommissioningSessions`

| Column | SQL type | Null | Constraints |
|---|---|---:|---|
| Id | uniqueidentifier | No | PK |
| DeviceId | uniqueidentifier | No | FK |
| DealerId | uniqueidentifier | No | FK/index |
| TankId | uniqueidentifier | No | FK |
| ProvisionalCredentialId | uniqueidentifier | Yes | FK to DeviceCredentials |
| Status | nvarchar(32) | No | pending/activation lifecycle |
| TankDepthMm | int | No | None |
| WaterFlexWorkOrderId | nvarchar(128) | Yes | None |
| CreatedByActorId | nvarchar(128) | No | None |
| CreatedByDisplayName | nvarchar(200) | No | None |
| CreatedAtUtc | datetimeoffset | No | None |
| ExpiresAtUtc | datetimeoffset | No | 30 minutes from create in service |
| ActivatedAtUtc | datetimeoffset | Yes | Future activation |
| CompletedAtUtc | datetimeoffset | Yes | Future first telemetry |
| CancelledAtUtc | datetimeoffset | Yes | None |
| ActivationAttemptId | uniqueidentifier | Yes | Unique when non-null |
| FailureCode | nvarchar(64) | Yes | None |
| RowVersion | rowversion | No | Concurrency token |

`WaterFlexWorkOrderId` follows the same pattern here: it is a nullable external identifier, not a normalized work-order entity.

Filtered unique live session per device and tank for `PendingSensor` and `AwaitingFirstTelemetry`. Index `(Status, ExpiresAtUtc)` supports cleanup.

#### `ProvisioningAuditEvents`

| Column | SQL type | Null | Constraints |
|---|---|---:|---|
| Id | bigint IDENTITY | No | PK |
| DeviceId | uniqueidentifier | Yes | FK |
| CommissioningSessionId | uniqueidentifier | Yes | FK |
| EventType | nvarchar(64) | No | None |
| ActorType | nvarchar(32) | No | None |
| ActorId | nvarchar(128) | No | None |
| DetailsJson | nvarchar(2048) | No | Non-secret structured detail |
| OccurredAtUtc | datetimeoffset | No | None |

Indexes: `(DeviceId, OccurredAtUtc)` and `(CommissioningSessionId, OccurredAtUtc)`. No check requires at least one FK.

#### `__EFMigrationsHistory`

EF-owned table with `MigrationId nvarchar(150)` primary key and `ProductVersion nvarchar(32)`.

### Relationships


### Migrations

1. `20260723192309_InitialCreate`: customers, locations, tanks, devices, operational credentials, installations, original calibrations, telemetry, uniqueness/indexes.
2. `20260724141544_UseTankDepthCalibration`: renames `EmptyDistanceMm` to `TankDepthMm` and `FullDistanceMm` to `CommissioningDistanceMm`.
3. `20260724152140_AddDealerOwnership`: creates Dealers and nullable installation dealer FK/index.
4. `20260724164603_AddBootstrapProvisioning`: adds factory metadata and creates bootstrap credentials, commissioning sessions, provisioning audits, filtered reservation indexes.

Commands:


### Seed Data

There is no EF seed. `DevelopmentWaterFlexCustomerDirectory` contains three in-memory fixture accounts:

- North Ridge Apartments, account 10482: two locations and three tanks (600, 350, 600 lb).
- Baker Family Residence, account 22017: one location and one 300 lb tank.
- Lakeside Dental Group, account 31804: one location and two 450 lb tanks.

The records enter SQL only when a legacy commission or pending session upserts the selected branch.

Development identities are also in-memory:

- Alex Morgan (`wf-ops-alex`), WaterFlex employee.
- Jordan Lee (`north-star-jordan`), North Star Water Systems technician.
- Sam Rivera (`lakes-water-sam`), Lakes Water Conditioning technician.

---

## 9. API Documentation

### Common API behavior

- Local base URL: `http://localhost:5188`.
- OpenAPI is Development-only at `/openapi/v1.json`; Swagger is at `/swagger`.
- Kestrel rejects request bodies over 64 KiB globally.
- JSON property naming is camel case. Enum values are camel-case strings.
- Unknown JSON members are rejected (`UnmappedMemberHandling.Disallow`).
- Malformed binding generally yields ASP.NET Problem Details. `BadHttpRequestException` preserves its HTTP status; unhandled exceptions become 500.
- Outside Development, technician, factory, operations, development-user, OpenAPI, and Swagger routes are not mapped.
- Examples below use Development-only local identities and placeholder secrets. Never use example secrets in a deployed environment.

### Generic error representation


Validation errors use:


### `GET /health`

**Purpose:** process liveness only. It does not query SQL or dependencies.

**Authentication:** anonymous. **Headers/body/query/route parameters:** none.


`200 OK`:


**Dependencies/business logic:** none beyond the running ASP.NET process. No modeled application error.

### `GET /api/v1/development/users`

**Purpose:** populate the Development identity selector.

**Authentication:** none, but route exists only in Development.


`200 OK`:


**Dependencies:** singleton `IDevelopmentIdentityDirectory` / `DevelopmentIdentityDirectory`. No persistence.

### `GET /api/v1/technician/customers`

**Purpose:** fixture WaterFlex customer/location/tank search for provisioning.

**Authentication:** Development role filter requiring header:


WaterFlex employee or unknown user returns 403 or 401 respectively. **Query:** optional `search`; trimmed, case-insensitive search across customer name/account and location name/address.


`200 OK` example:


No match returns `200 []`. **Dependency:** `IWaterFlexCustomerDirectory`; current implementation is an in-memory fixture.

### `POST /api/v1/technician/commission`

**Purpose:** legacy Development commissioning. Creates an immediately Active device and exposes an operational token once.

**Authentication:** dealer-technician Development identity header. **Content-Type:** `application/json`.

**Request body:**



**Validation:** customer/location/asset required and at most 128 characters; serial normalized uppercase and must be 4-64 ASCII letters/digits/hyphens; hardware strips whitespace, `:` and `-`, uppercases, and must be 12 hex characters; model required/max 100; work order optional/max 128; depth 10-450 cm; distance 3-450 cm and no greater than depth; both numeric values support one decimal place. Explicit null required strings can currently throw during normalization instead of returning 400.

`200 OK`:


**Errors:** 400 validation; 401 missing/unknown development user; 403 wrong role; 404 directory selection; 409 serial/hardware duplicate, active tank, or unique race.

**Dependencies/business logic:** `ISensorCommissioningService`, fixture directory, `TimeProvider`, SQL execution strategy and serializable transaction. Upserts dealer/customer/location/tank; creates Active device, installation, calibration, and operational credential; stores only SHA-256 secret hash.

### `POST /api/v1/factory/devices`

**Purpose:** Development factory inventory registration with hash-only bootstrap identity.

**Authentication headers:**


The configured/presented key is SHA-256 hashed and fixed-time compared. Missing server configuration returns 503. Missing/wrong key or operator outside 1-200 chars returns 401.

**Request:** `bootstrapSecretHash` is Base64 for exactly 32 SHA-256 bytes. The all-zero sample is syntactically valid but insecure and is only an example.



**Validation:** serial rules as above; hardware 12 hex; model max 100; credential ID required/max 64, starts `wf_boot_`, and only letters/digits/underscore/hyphen; firmware/config required/max 64; hash valid Base64 and 32 decoded bytes; operator required/max 200.

`201 Created`, `Location: /api/v1/factory/devices/{deviceId}`:


**Errors:** 400 validation; 401 identity; 503 server key absent; 409 duplicate serial/hardware, duplicate bootstrap ID, or race.

**Dependencies:** `IFactoryDeviceRegistrationService`. Serializable transaction creates Registered device, bootstrap hash, and `factory_device_registered` audit. Plaintext bootstrap secret is never accepted or returned.

### `POST /api/v1/technician/commissioning-sessions`

**Purpose:** reserve one factory-registered device and one tank for future sensor activation.

**Authentication:** dealer technician Development header. **Body:**



**Validation:** IDs required/max 128; serial 4-64 ASCII alphanumeric/hyphen; work order optional/max 128; depth 10-450 with one decimal. Device must be Registered with a currently valid, unrevoked, unconsumed bootstrap credential. Tank/device must have no live reservation; tank must have no active installation.

`201 Created`, session expires after 30 minutes:


**Errors:** 400 validation; 401/403 identity; 404 directory or factory sensor; 409 unavailable device, live reservation, occupied tank, or race.

**Dependencies/business logic:** `ICommissioningSessionService`. Lazily expires old conflicts, upserts mapping records, changes device Registered -> Commissioning, inserts PendingSensor session/audit. It creates no installation, calibration, or operational token.

### `GET /api/v1/technician/commissioning-sessions/{sessionId}`

**Purpose:** dealer-scoped activation status polling.

**Authentication:** dealer technician header. **Route parameter:** UUID `sessionId`.


`200` returns `CommissioningSessionView` shown above. If a live session is past expiry, lookup sets it `expired`, revokes a provisional credential if present or resets a still-pending Commissioning device to Registered, writes an audit, and returns the expired view.

Missing/cross-dealer session returns 404, deliberately avoiding existence disclosure. Wrong identity returns 401/403.

### `POST /api/v1/technician/commissioning-sessions/{sessionId}/cancel`

**Purpose:** cancel a still-pending reservation.

**Authentication/parameter:** same as GET. **Body:** none.


`200` returns the session with `status: "cancelled"` and resets the device to Registered. Only `PendingSensor` is cancellable. Missing/cross-dealer returns 404; expired/non-pending returns 409.

### `GET /api/v1/ops/dealers`

**Purpose:** populate fleet dealer filter.

**Authentication:** Development WaterFlex employee header:



`200`:


Returns only active dealers sorted by display name. Factory inventory without installations does not create a fleet row.

### `GET /api/v1/ops/fleet/summary`

**Purpose:** aggregate current-installation fleet counts under the same filters as the table.

**Authentication:** WaterFlex employee header.

**Query parameters:** optional `search`, `reportingStatus` (`reporting`, `stale`, `offline`, `neverReported`), `belowThreshold` boolean, `lifecycleStatus`, `firmwareVersion`, `dealerId`. Special `dealerId=unassigned` means null dealer. Invalid reporting status returns 400.


`200`:


Counts are over non-removed installations after filtering, not factory inventory or historical installations.

### `GET /api/v1/ops/devices`

**Purpose:** paged current-installation fleet list.

**Authentication:** WaterFlex employee header.

**Query:** summary filters plus `sort` (`attention`, `lastReported`, `fillAscending`, `fillDescending`, `customer`), `page` default 1, `pageSize` default 50/range 1-100. Invalid enums/paging return 400.


`200` abbreviated:


Attention sort ranks Offline, NeverReported, Stale, errors, low fill, then healthy. Current implementation loads all current installations and filters/pages in memory.

### `GET /api/v1/ops/devices/{deviceId}`

**Purpose:** detailed current/latest installation information.


`200` contains the full fleet item in `device`, plus:


The actual nested `device` contains all `FleetDeviceListItem` fields. Missing device installation returns 404. Factory-only devices are therefore not visible here.

### `GET /api/v1/ops/devices/{deviceId}/readings`

**Purpose:** bounded telemetry history.

**Query:** `range` defaults `24h`; valid `24h`, `7d`, `30d` only.


`200`:


Cutoff uses `ReceivedAtUtc`. At most newest 2,000 are selected then returned chronological. Missing device returns 404; existing device/no readings returns `200 []`; invalid range returns 400.

### `POST /api/v1/device/telemetry`

**Purpose:** authenticated device telemetry ingestion.

**Authentication header:**


**Body:**



**Validation:** schema exactly 1; firmware nonblank/max 64; 1-50 readings; boot ID nonempty; sequence/uptime nonnegative; observed timestamp no more than five minutes future; distance 30-4500 mm; quality 0-100; samples 1-1024; RSSI -127 through 0; at most 16 flags, each nonblank/max 64; unique boot/sequence inside batch.

`200`:


On replay, `status` becomes `duplicate` and original ID/fill/time are returned. Input order is preserved.

**Errors:** 401 malformed/unknown/wrong/expired token; 403 revoked credential or non-Active device; 400 validation grouped under `readings[index].field`; 409 no current installation or calibration; 413 global body size; 429 10 requests/minute with `Retry-After: 60` and `{"errorCode":"rate_limited","retryAfterSeconds":60}`; 500 unexpected.

**Dependencies:** `DeviceTokenValidator`, `TelemetryBatchValidator`, `EfTelemetryIngestionService`, SQL Server. It does not execute low-salt rules or ticket logic and does not complete bootstrap sessions.

---

## 10. Authentication and Authorization

### Identity provider

There is no production identity provider. Operational sensors use the custom DeviceToken ASP.NET authentication scheme. Development staff/dealer identity and factory identity are endpoint filters backed by headers/configuration, not external identity providers. Entra ID/OIDC is planned but has no tenant, client, authority, callback, or claim configuration.

### Authentication flow

The device authentication sequence below is the only implemented ASP.NET authentication flow. Development staff and factory filters resolve headers before endpoint execution and store their actors in `HttpContext.Items`.


### Token creation

Only the legacy immediate commissioning path creates an operational token today. It generates a random 12-byte credential-ID suffix and random 32-byte secret, stores `SHA256(secret)`, Base64URL-encodes the secret, and returns `credentialId.secret` once to the browser. Factory provisioning accepts a hash generated outside the API and creates no plaintext bootstrap token. Refresh-token creation and bootstrap activation token exchange do not exist.

### Token validation

Operational token validation is described below. Bootstrap credential validation is not implemented.

### Implemented identity mechanisms

#### Operational device bearer scheme

- ASP.NET scheme name: `DeviceToken`.
- Token format: `<credentialId>.<base64url-32-byte-secret>`.
- Credential ID max 64; encoded secret length 42-44; decoded exactly 32 bytes.
- API SHA-256 hashes presented secret and fixed-time compares against `DeviceCredentials.SecretHash`.
- Rejects revoked, expired/not-yet-valid, and non-Active devices.
- Produces `NameIdentifier` and `device_id` claims with Device GUID.
- Updates `LastUsedAtUtc` during authentication, even if later body validation fails.
- Invalid/expired -> 401 `{"errorCode":"invalid_device_token"}`.
- Revoked/non-Active -> 403 `{"errorCode":"device_unavailable"}`.


No refresh token exists. Operational credentials can have optional expiry but no rotation/recovery endpoint is implemented.

#### Development staff/dealer filter

- Header `X-WaterFlex-Development-User` contains an opaque fixture user ID.
- `DevelopmentIdentityDirectory` resolves the ID to `StaffActor`.
- Group filter requires exact role: DealerTechnician or WaterFlexEmployee.
- Missing/unknown -> 401; wrong role -> 403.
- Actor is stored in `HttpContext.Items`, not an authenticated `ClaimsPrincipal`.
- Browser persists selected user ID in localStorage key `waterflex-development-user`.

This is not production security. A user can choose any seeded identity.

#### Development factory filter

- Headers: `X-WaterFlex-Factory-Key`, `X-WaterFlex-Factory-Operator`.
- Server key comes from `FactoryProvisioning:DevelopmentKey`.
- Both presented/configured strings are SHA-256 hashed and fixed-time compared.
- Operator length must be 1-200.
- Missing server config -> 503; invalid identity -> 401.
- Operator is stored in `HttpContext.Items`.

No machine certificate, IP allow-list, rotation, failed-auth audit, or production policy exists.

### Roles and permissions

| Route group | Required identity |
|---|---|
| `/api/v1/device/telemetry` | DeviceToken authentication |
| `/api/v1/technician/*` | Development DealerTechnician |
| `/api/v1/ops/*` | Development WaterFlexEmployee |
| `/api/v1/factory/*` | Development factory key/operator |
| `/health`, `/api/v1/development/users` | Anonymous in Development |

### Bootstrap authentication

Database storage exists, but a bootstrap authentication handler and activation endpoint do not. Bootstrap credentials cannot currently authenticate any route.

### Session handling

There is no browser/server login session, cookie, refresh token, or CSRF token. `CommissioningSession` is a business reservation, not an authentication session. It expires after 30 minutes and is dealer-scoped through the Development actor.

### Refresh tokens

No refresh token type, endpoint, persistence table, rotation policy, or browser storage exists. Operational device credentials are long-lived until optional expiry/revocation; no renewal API exists.

### Claims

Successful DeviceToken authentication creates `ClaimTypes.NameIdentifier` and `device_id`, both containing the Device GUID. Development staff and factory identities create no claims principal; their actor/operator values live in `HttpContext.Items`.

### Roles and permissions

Roles are `dealerTechnician` and `waterFlexEmployee` in the Development directory. Permissions are route-group based as listed above. Factory operator is an identity string rather than a role. No production role mapping or granular permission model exists.

### Security decisions

- Store only SHA-256 credential hashes, never operational/bootstrap plaintext server-side.
- Use fixed-time comparisons for device and factory secrets.
- Resolve customer/tank server-side; telemetry contains no ownership fields.
- Return cross-dealer session lookup as 404 to avoid existence disclosure.
- Keep staff/factory routes Development-only until real identity exists.
- Reject unknown JSON members so firmware cannot inject ownership metadata.
- Use serializable transactions and unique indexes for device/tank ownership races.

Missing: production OIDC/Entra ID, authorization policies/claims, CORS, HSTS, antiforgery, CSP/security headers, bootstrap auth, secret vault/rotation, distributed rate limiting, and failed-auth telemetry.

---

## 11. Frontend Architecture

### Framework

The SPA uses React 18 function components, React DOM `createRoot`, TypeScript source, and Vite. There is no server-side rendering, class component, framework meta-layer, or React Server Component.

### Component Hierarchy


`main.tsx` mounts with `createRoot`. `DevelopmentIdentityProvider` wraps `App` inside `BrowserRouter`.

### Routing

| Route | Element | Behavior |
|---|---|---|
| `/` | `Navigate` | Redirect to `/fleet` |
| `/fleet` | `FleetPage` | Internal fleet |
| `/fleet/:deviceId` | `DeviceDetailPage` | Device detail/history |
| `/provision` | `ProvisioningWorkflow` | Legacy Development commissioning |
| `*` | `Navigate` | Redirect to `/fleet` |

Production static hosting needs SPA fallback rewriting to `index.html`; no hosting configuration is supplied.

### State management

- No Redux, Zustand, MobX, React Query, or server cache.
- Development identity: React Context + localStorage.
- Fleet filters/page: URL search params.
- Loading/results/errors/form values: component `useState`.
- Search responsiveness: `useDeferredValue`; URL changes use `startTransition`.
- Requests use `fetch` and `AbortController` in effects.
- No global error boundary.

### Context providers

`DevelopmentIdentityProvider` fetches seeded users, chooses stored/default `wf-ops-alex`, repairs an invalid selection, and exposes users/current user/select function. Every API helper reads localStorage directly when building headers; changing selected ID causes page effects to refetch.

### Custom hooks

The only custom hook is `useDevelopmentIdentity`, which enforces that callers are under the provider and returns the identity context. All other state/effects use React's built-in hooks directly.

### Services

Frontend service modules are plain functions rather than classes or dependency-injected services. `ops/api.ts`, `provisioning/api.ts`, and `sensorSerial.ts` are described below and in Section 13.

### API clients

- `ops/api.ts`: dealers, summary, page, detail, readings. Throws `OpsApiError`; parses title/detail fallback.
- `provisioning/api.ts`: fixture search and legacy commission. Throws `ApiError` and flattens validation arrays.
- All URLs are relative `/api`; Vite proxies in Development.
- No retry, timeout, caching, correlation ID, or production base URL abstraction.
- Factory/session APIs are not consumed by frontend.

### Styling strategy

- Single global `index.css`, 2,543 lines.
- CSS custom properties define ink, canvas, surfaces, borders, WaterFlex primary `#0157b6`, dark `#01336a`, wash, nav, success/warning/danger, shadow.
- Aptos/Corbel, Bahnschrift, and Cascadia Mono/Consolas fallback families; no web fonts.
- No negative letter spacing; responsive breakpoints 1180/820/580 px.
- Dense fleet table uses an internal horizontal scroller; page clips horizontal overflow.
- Radix select menus are portaled, keyboard accessible, and themed globally.
- No CSS modules, Sass, Tailwind, CSS-in-JS, design-system package, or image assets.

---

## 12. Frontend Components

### `App` (`web/src/App.tsx`)

**Purpose:** shared shell, brand/header, primary navigation, resources, route switch.

**Props/state:** no props; reads `useLocation` to label section. **Events:** NavLink navigation and identity selection delegated. **Dependencies:** Lucide, React Router, identity selector, three page components. **Business rule:** unknown routes return to fleet.

### `DevelopmentIdentityProvider` / selector

**Location:** `web/src/development/DevelopmentIdentity.tsx`.

**Props:** provider receives `children`. Selector receives none.

**State:** users, selected ID initialized from localStorage. Derived current user.

**Events/API:** fetches `/api/v1/development/users`; selection writes localStorage. **Business rule:** fallback to first WaterFlex employee or first user. **Risk:** identity is user-selectable and not secure.

### `ThemedSelect`

**Location:** `web/src/components/ThemedSelect.tsx`.

**Props:** `value`, `options`, `onValueChange`, `ariaLabel`, optional `disabled`, optional `placeholder`.

**Behavior:** maps empty application value to sentinel `__waterflex_empty__`; renders Radix trigger, portal, scroll buttons, items, and check indicator. Keyboard/focus/collision handling comes from Radix.

### `FleetPage`

**Location:** `web/src/ops/FleetPage.tsx`.

**State:** summary, page result, dealers, loading, error, refresh counter. URL state: search, dealer, reporting status, fill, sort, page. Identity ID is an effect dependency.

**API:** concurrent `getFleetSummary`, `getFleetDevices`, `getFleetDealers` on filter/identity/refresh change.

**Events:** search/select updates URL and clears page; refresh increments counter; pagination updates URL.

**Business rules:** page size 25; attention default; filters summary and rows identically; display nullable telemetry as unavailable rather than zero.

**Subcomponents:**

- `Metric`: label/value/icon/tone; summary strip.
- `FleetRow`: clamps fill visual to 0-100, shows threshold state, diagnostics, and links.
- `ReportingBadge`: semantic labels/icons for Reporting/Stale/Offline/NeverReported.
- `formatDateTime` and `formatRelative`: browser locale formatting.

Known issue: Updated compares generated timestamp to itself, so it normally says "now" rather than age since refresh.

### `DeviceDetailPage`

**Location:** `web/src/ops/DeviceDetailPage.tsx`.

**State:** detail, readings, range (`7d` default), loading/error. **Route prop:** `deviceId` from URL.

**API:** concurrently loads detail and readings; refetches on device/range/identity.

**UI:** back link, status/fill panel, installation and health panels, 24h/7d/30d selector, last 50 readings reversed newest-first, calibration/credential/installer/hardware/model/commissioning metadata.

**Missing planned behavior:** chart, audit timeline, actions, replacement lineage, calibration transitions.

**Subcomponents:** `DetailRow`, `MetaItem`.

### `ProvisioningWorkflow`

**Location:** `web/src/provisioning/ProvisioningWorkflow.tsx`, 924 lines.

**State:** step/furthest step, customer search/results/loading/error, selected IDs, sensor form, tank depth, captured sensor reading/progress/error/loading, commission loading/error/result, clipboard state.

**Steps and subcomponents:**

1. `StepRail`: enabled/completed/current step navigation.
2. `CustomerStep`: deferred search and fixture customer selection.
3. `LocationStep`: location and tank selection.
4. `SensorStep`: serial, hardware ID, model, optional work order.
5. `CalibrationStep`: tank depth plus Web Serial capture, fill preview, metrics.
6. `CalibrationGraphic`: tank/surface visualization.
7. `ReviewStep`: grouped assignment/sensor/calibration review and submit.
8. `CompletionScreen`: one-time token, copy, details, restart.
9. `JobContext`, `ContextItem`, `ReviewGroup`, `ReviewRow`: supporting context/review UI.

**Business rules:** Continue gated per step; changing customer/location/tank or sensor identity clears captured reading; calibration requires depth 10-450 and captured distance no greater than depth; submit uses legacy immediate endpoint. Identity must be dealerTechnician.

**Mismatch:** production design is Android SoftAP/bootstrap, but this component still uses bench Web Serial and legacy commission.

### `sensorSerial`

**Location:** `web/src/provisioning/sensorSerial.ts`.

Not a React component. Opens selected Web Serial port at 115200; parses `distance=N mm`; accepts 30-4500; collects five samples in 12 seconds; median; rejects spread over 100 mm; closes/cancels/release-lock in finally. Errors: unsupported, cancelled, unavailable, timeout, unstable.

---

## 13. Services Layer

### `TelemetryBatchValidator`

- Input: nullable `TelemetryBatch` and injected clock.
- Output: all validation errors, not first-only.
- Dependencies: `TimeProvider`.
- No I/O/retry.
- Enforces schema, batch size, ranges, future timestamp, flag constraints, and in-batch dedupe.

### `DeviceTokenValidator`

- Input: bearer token string.
- Output: Device ID or Invalid/Expired/Revoked/DeviceUnavailable.
- Dependencies: DbContext, TimeProvider, SHA-256.
- Fixed-time secret compare; saves last use on success.
- No retry wrapper beyond DbContext provider behavior.

### `EfTelemetryIngestionService`

- Input: authenticated Device GUID and batch.
- Output: acknowledgement or typed failure.
- Dependencies: DbContext, validator, clock, FillCalculator.
- Serializable transaction and SQL retry strategy.
- One explicit retry after unique-key race; change tracker cleared.
- Resolves current installation/calibration and persists server-computed fill.

### `EfSensorCommissioningService`

- Input: legacy request and trusted StaffActor.
- Output: one-time token response or typed failure.
- Dependencies: DbContext, fixture directory, clock, cryptographic RNG.
- Serializable transaction; catches unique SQL races.
- Upserts mapping, creates full operational graph.

### `EfFleetQueryService`

- Input: filters/query/device/range.
- Output: dealers, summary, page, detail, or history.
- Dependencies: DbContext, clock, MonitoringPolicy.
- Reads use `AsNoTracking`; details use split query.
- Latest reading is selected by receipt timestamp then ID.
- JSON error flags are deserialized; malformed JSON becomes `invalid_error_flags`.
- Important scalability limitation: current fleet is loaded then filtered/sorted/paged in memory.

### `EfFactoryDeviceRegistrationService`

- Input: normalized factory request and operator ID.
- Output: inventory registration or validation/duplicate/conflict.
- Dependencies: DbContext, clock.
- Accepts a precomputed Base64 SHA-256 hash, not plaintext.
- Serializable transaction creates Registered device, bootstrap credential, non-secret audit.

### `EfCommissioningSessionService`

- Input: create/get/cancel and StaffActor.
- Output: session view or typed failure.
- Dependencies: DbContext, fixture directory, clock.
- Creates 30-minute reservations; dealer scopes get/cancel.
- Lazily expires conflicting/queried sessions.
- Upserts dealer/customer/location/tank but does not create installation/calibration/token.
- Cancellation and pending expiry reset device to Registered.

### `DevelopmentWaterFlexCustomerDirectory`

- Inputs: optional search or exact three IDs.
- Outputs: fixture trees/selection.
- No I/O. Case-insensitive search; exact ordinal resolution.
- Removal breaks all Development customer selection and commissioning/session service tests.

### `DevelopmentIdentityDirectory`

- Resolves three fixed actors.
- No persistence or security.

### `StubDeliveryTicketGateway`

- Input: delivery request.
- Output: `STUB-{IdempotencyKey}`, status Created, current UTC.
- Not registered or called. Removal has no current runtime effect but removes the planned interface implementation.

### Frontend API services

- `ops/api.ts`: GET-only operations fetches and URL query serialization.
- `provisioning/api.ts`: fixture search and legacy commission.
- Both attach selected Development identity, parse Problem Details minimally, and have no retries.

---

## 14. Background Jobs

### `DeliveryOutboxWorker`

**Host:** `WaterFlex.SaltMonitor.Worker` Generic Host.

**Trigger:** continuous process; loop starts with host.

**Schedule:** logs a heartbeat, then `Task.Delay(30 seconds, stoppingToken)`.

**Dependencies:** only `ILogger<DeliveryOutboxWorker>`. Program does not call `AddSaltMonitorPersistence` and does not register `IDeliveryTicketGateway`.

**Failure recovery:** normal host logging and process restart only. Cancellation interrupts delay. No try/catch, lease, retry, dead-letter, queue, or state.

**Reality:** despite its name/comment, it does not process any outbox because no outbox table or logic exists.

No other scheduled jobs, queue consumers, event processors, or batch processes exist. Commissioning expiry is lazy on service access; no cleanup worker exists.

---

## 15. External Integrations

### SQL Server

- Vendor: Microsoft.
- Purpose: all durable application state.
- Auth locally: Windows Integrated Security.
- Configuration: `ConnectionStrings__SaltMonitor` or Development LocalDB fallback.
- Failure handling: EF retry-on-failure; service transactions; no circuit breaker/health check.

### WaterFlex customer directory

- Intended vendor/system: internal WaterFlex.
- Current implementation: in-memory fixture only.
- Auth/API endpoint/configuration: none.
- Failure handling: cancellation only; real adapter missing.

### WaterFlex/RouteFlex ticketing

- Current implementation: deterministic in-process stub only.
- No external endpoint/auth/configuration.
- Stub is not registered/called.
- No outbox schema or ticket state.

### Arduino Nano ESP32 and A02YYUW

- Arduino receives A02YYUW UART frames at 9600 8N1.
- Wiring: A02YYUW VCC -> 3V3, GND -> GND, TX -> D4/Serial1 RX, RX unconnected, D5 assigned TX but physically unused.
- Firmware prints USB diagnostics at 115200.
- No cloud/Wi-Fi integration is implemented.

### Browser Web Serial

- Purpose: Development bench capture of live sensor distance.
- Auth: user explicitly grants serial port.
- Failure handling: typed unsupported/cancel/timeout/unstable errors and cleanup.
- Not available on iOS/Firefox/Safari; not the intended Android production workflow.

### Package feeds

- NuGet: Microsoft public dotnet Azure DevOps feed.
- NPM: public npm registry (`https://registry.npmjs.org/`). Run `npm --prefix .\web ci` to restore from the committed lock.

### Support mail link

Header opens `mailto:support@waterflex.com`. No ticket API integration.

### Cloud vendors

No Azure/AWS/Auth0/Stripe/Twilio/Jira/ServiceNow SDK or deployment integration exists. Plan text references a WaterFlex-hosted environment, but provider, account, region, and resources are absent.

---

## 16. Configuration Management

### Environment variables

| Name | Purpose | Example | Required |
|---|---|---|---|
| `ConnectionStrings__SaltMonitor` | SQL Server connection | `Server=(localdb)\MSSQLLocalDB;Database=WaterFlexSaltMonitor;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True` | Optional in Development; required elsewhere when persistence resolves |
| `FactoryProvisioning__DevelopmentKey` | Development factory endpoint key | `local-factory-dev-key` | Required to use factory endpoint; absent -> 503 |
| `ASPNETCORE_ENVIRONMENT` | ASP.NET environment | `Development` | Optional standard variable; Development required for staff/factory/ops/Swagger routes |
| `DOTNET_ENVIRONMENT` | Generic host environment alternative | `Development` | Optional |
| `ASPNETCORE_URLS` | API binding | `http://localhost:5188` | Optional; command uses `--urls` |

No `VITE_*` variables exist. No environment template exists.

### Config Files

The meaningful checked-in configuration files are documented below. Files commonly expected in larger deployments (`appsettings*.json`, `.env`, launch profiles, Docker, CI/CD, reverse proxy) are absent.

### `global.json`

- SDK 10.0.300.
- `rollForward: latestPatch`.
- prerelease disabled.

### `dotnet-tools.json`

Root local tool manifest; `dotnet-ef 10.0.10`, no roll-forward.

### `NuGet.Config`

Clears inherited feeds, maps all packages to Microsoft `dotnet-public`. This avoids blocked NuGet.org in the observed network but is an availability dependency.

### `backend/Directory.Build.props`

Applies net10.0, latest C#, nullable, implicit usings, and warnings-not-errors to all descendants.

### Project files

Project references and package versions are enumerated in Sections 5 and 6. Infrastructure keeps EF Design private. Tests are non-packable.

### `web/package.json`

- ESM package.
- Scripts: `vite`, `vite build`, `vite preview`.
- No test, lint, format, or typecheck script.

### `web/vite.config.ts`

- React plugin.
- port 3000.
- strict port prevents fallback.
- relative `/api` proxy to localhost:5188.

### `web/index.html`

Minimal UTF-8/viewport document, title `WaterFlex Ops Console`, `#root`, module `/src/main.tsx`.

### PlatformIO

Environment/board `arduino_nano_esp32`, platform `espressif32`, Arduino framework, monitor 115200. Platform and libraries are unpinned.

### Missing configuration files

No `appsettings.json`, `appsettings.Development.json`, `launchSettings.json`, `.env`, `.env.example`, `tsconfig.json`, ESLint, Prettier, Docker, Compose, NGINX, IIS, CI/CD, or production SPA/server config exists.

### Hard-coded operational constants

- API: 64 KiB body, telemetry 10/minute, retry-after 60 sec, max 50 readings, report 3,600 sec.
- Monitoring: low `<35%`, stale >2h, offline >6h.
- Session: 30 minutes.
- Web: API proxy 5188, UI 3000, Swagger link 5188.
- Serial: 115200 USB, 5 samples, 12 sec timeout, 30-4500 mm, max spread 100 mm.
- Firmware: sensor 9600, UART timeout 200 ms, loop one second.

---

## 17. Logging and Monitoring

### Logging framework

ASP.NET Core and Generic Host use built-in `Microsoft.Extensions.Logging`. No provider is explicitly configured, so defaults for the host/environment apply. EF Core command logs are visible in Development/test output. Worker uses structured template:


### Log levels

No repository configuration sets category levels. Defaults depend on host configuration. There is no `appsettings.json` to tune EF/ASP.NET categories.

### Monitoring, metrics, tracing

- `/health` is process-only liveness.
- No readiness/dependency health check.
- No OpenTelemetry, Application Insights, Prometheus, tracing, correlation IDs, or custom metrics.
- No dashboards or alert definitions.
- Rate-limit rejection is visible only through HTTP/logs.
- Provisioning audit is database data, not an observability pipeline.

### Required production additions

At minimum add structured centralized logs with authorization-header/secret/Wi-Fi redaction, trace IDs, SQL/API dependency telemetry, activation and telemetry counters, queue depth/offline metrics, readiness checks, alert rules, retention, and dashboards. These are requirements, not existing implementation.

---

## 18. Infrastructure Architecture

### Current local infrastructure


### Hosting

Current: local processes only. API runs Kestrel; frontend runs Vite (development) or produces static `dist`. Worker runs as a console/Windows process. No production host is selected in source.

### Networking

- API local HTTP 5188.
- Vite local HTTP 3000 and proxy.
- HTTPS redirection activates outside Development, but no HTTPS endpoint/certificate config is included.
- No CORS because browser uses same-origin proxy locally.
- No private network, firewall, WAF, service discovery, or ingress definitions.

### Load Balancing

None. No proxy/ingress, sticky-session policy, readiness probe, or replica coordination exists. Rate limiting is memory-local and would differ per replica.

### Caching

None. There is no Redis, CDN configuration, application output cache, query cache, or client data cache.

### Storage

SQL Server only. Firmware durable queue and object storage are absent.

### DNS

None defined. Production sensor and SPA/API hostnames are design requirements only.

### Certificates

None defined. Development uses HTTP and trusts the local SQL certificate. Public API TLS and certificate rotation are missing production decisions.

### Secrets management

Current configuration/environment variables. `.gitignore` excludes env files, but there is no Key Vault or equivalent. Factory key is static Development configuration.

### Target production topology (not provisioned)


Provider, regions, SKUs, scaling, backups, RPO/RTO, network rules, and deployment mechanism are missing and must be decided before this target can be recreated.

---

## 19. Docker and Containers

### Current state

There are no Dockerfiles, Compose files, `.dockerignore` files, container registries, image names, build arguments, container networks, volumes, health checks, or orchestration manifests in the repository. Docker was not installed on the observed machine.

Therefore these commands do **not** work against the current repository:


There is no current container build order or runtime dependency graph.

### Requirements if containers are introduced

To containerize without changing architecture, create three artifacts:

1. API multi-stage Dockerfile: `mcr.microsoft.com/dotnet/sdk:10.0` build -> `mcr.microsoft.com/dotnet/aspnet:10.0` runtime; expose configured HTTP port; run published API DLL.
2. Worker multi-stage Dockerfile using the same SDK/runtime; note that current worker is heartbeat-only and has no persistence registration.
3. Web multi-stage Dockerfile: Node 20+ build -> an HTTP static server configured with SPA fallback and `/api` reverse proxy, or deploy `dist` independently.

Do not place SQL Server LocalDB in a Linux container; LocalDB is Windows-only. Use a real SQL Server container/managed database and set `ConnectionStrings__SaltMonitor`.

Suggested future build order after files exist:


Required Compose services would be SQL Server, API, web, and optionally Worker. Add a named SQL volume, service health checks, SQL startup ordering, environment secrets, and a one-shot migration job. This is proposed work, not recoverable from current files.

---

## 20. CI/CD Pipelines

### Current state

No GitHub Actions, Azure Pipelines, Jenkinsfile, GitLab CI, TeamCity configuration, release script, artifact manifest, security scan, branch condition, deployment stage, trigger, or secret variable definition exists.

### Build pipeline required for recreation

A faithful pipeline should run in this order:

1. Checkout with clean workspace.
2. Install/select .NET 10.0.300 latest patch.
3. Restore local tools.
4. Restore/build authoritative backend solution.
5. Start/provide SQL Server compatible with integration tests; current tests require Windows LocalDB, so use a Windows agent or refactor tests.
6. Run all .NET tests serially.
7. Install Node 20+ and run `npm ci` in `web`.
8. Run frontend build. Add `tsc --noEmit`, lint, and frontend tests only after those configs exist.
9. Install pinned PlatformIO and run firmware build/tests after platform versions are pinned.
10. Publish API/Worker and archive web `dist` plus migration bundle.

Illustrative commands:


The repository has no NuGet lock files, so `--locked-mode` will fail until lock generation is added. Remove that switch for exact current behavior or add lock files intentionally.

### Test pipeline

- Current authoritative automated suite: .NET tests only.
- Frontend: build smoke only; no test framework.
- Firmware: build only; no tests.
- Browser end-to-end checks were performed interactively during development but are not checked in.

### Security scanning

None exists. Add dependency audit/SBOM, secret scanning, SAST, container scanning if containers are added, NuGet/npm license review, and firmware binary/secret inspection. `npm install` currently reports known low/moderate vulnerabilities; exact audit output depends on registry/advisory state.

### Deployment pipeline

No deployable target is defined. A future pipeline needs environment approvals, database migration gate, secret injection, static/API/Worker deployment, smoke tests, telemetry canary, and rollback. It must not map Development factory/staff endpoints in Production until real authentication replaces filters.

### Release Process

No release process, tags, semantic versioning, changelog, approval gate, artifact retention, migration approval, canary policy, or rollback automation is checked in. A future release must version API, web, database migration, factory configuration, and firmware together and record their compatibility.

### Variables and secrets that a future pipeline needs

- SQL connection string per environment.
- Production identity/OIDC settings (not designed in code).
- Factory machine identity settings (not implemented).
- API hostname/TLS settings.
- Static host/API base URL or same-origin reverse-proxy settings.
- Deployment credentials/registry IDs.
- Firmware signing, secure-boot, flash-encryption, and OTA keys (not present).

---

## 21. Testing Strategy

### Test execution model

- Framework: xUnit 2.9.2.
- Parallel test classes disabled assembly-wide because EF migration application locks raced under LocalDB.
- 35 currently discovered cases: 30 test methods plus expanded theory rows.
- EF integration fixtures create a uniquely named LocalDB database, migrate it, execute, then `EnsureDeleted`.
- API tests use `WebApplicationFactory<Program>` and in-memory configuration overrides.
- Fixed/mutable `TimeProvider` implementations make time boundaries deterministic.
- No mocking framework is installed; hand-written fixtures/fakes are used.

### Unit Tests

#### `FillCalculatorTests` - 5 cases

- Surface at sensor -> 100%.
- Surface at tank bottom -> 0%.
- Midpoint -> 50%.
- Beyond bottom clamps to 0%.
- Nonpositive depth throws.

#### `MonitoringPolicyTests` - 8 cases

- 0h and exactly 2h -> Reporting.
- 2.01h and exactly 6h -> Stale.
- 6.01h -> Offline.
- Null -> NeverReported.
- 34.99% is below threshold; exactly 35% is not.

#### `TelemetryBatchValidatorTests` - 3 cases

- Valid batch accepted.
- Distance/quality/future timestamp errors returned.
- Duplicate keys inside a batch rejected.

### Integration Tests

#### `CommissioningServiceTests` - 4 cases

- Complete device/installation/calibration/dealer/credential graph, valid token, and fill/mm conversion.
- Duplicate hardware rejected.
- Occupied tank rejected.
- More than one decimal centimeter precision rejected.

#### `FleetQueryServiceTests` - 2 cases

- Latest reading, low-fill count, dealer, and reporting-state classification.
- Combined free-text and Offline filter.

#### `TelemetryPersistenceTests` - 2 cases

- First insert then duplicate acknowledgement; one persisted row and 50% fill.
- Correct unique secret validates; wrong secret rejects.

#### `BootstrapProvisioningServiceTests` - 4 cases

- Registered factory inventory stores hash only and non-secret audit.
- Pending session creates reservation but zero installation/operational credentials.
- Dealer-scoped lookup and cancellation release.
- Lazy expiry releases pending device.

### API integration tests

#### `TelemetryApiTests` - 5 cases

- Authenticated persistence and retry acknowledgement.
- Unknown token -> 401.
- Customer ownership field rejected by strict JSON.
- OpenAPI bearer scheme/security and anonymous health.
- Swagger UI loads.

#### `CommissioningApiTests` - 1 case

- Fixture search plus legacy commission returns token and persists device/install/dealer.

#### `BootstrapProvisioningApiTests` - 1 case

- Factory request without identity -> 401; with key -> 201; technician session -> 201; status -> 200; exactly one device/bootstrap/session and zero install/operational credential.

### Frontend testing

No Vitest/Jest/React Testing Library/Playwright project or test script exists. Only `npm run build` is automated. Critical missing tests include routing, filters, identity switching, error states, themed selects, Web Serial parsing/workflow, token handling, mobile layout, and bootstrap session UI.

### End-to-End Tests

No checked-in end-to-end suite exists. API tests cover HTTP plus SQL in-process, but no automated browser-to-API-to-database or physical-sensor path is present. Browser/Playwright checks performed interactively during development are not reproducible from the repository.

### Mocking

No mocking library is installed. Tests use real EF SQL Server LocalDB, `WebApplicationFactory`, deterministic in-memory customer/identity implementations, and hand-written fixed/mutable `TimeProvider` classes.

### Fixtures

Each database/API test owns a helper that generates a unique LocalDB database, runs all migrations, seeds only the entities required by that test, and deletes the database during async disposal. Development customer/identity fixtures are production source classes reused by tests.

### Test Data

Test data uses deterministic serials, hardware IDs, GUIDs, credentials, times, distances, and the three Development customer branches. No external fixture files, snapshots, golden JSON, anonymized production extracts, or random-data generator exists.

### Firmware testing

No native tests, hardware-in-loop tests, mocks, or CI fixture exists. Required future coverage: UART resynchronization/checksum/range, median quality, storage/state machine, Wi-Fi portal, TLS, activation, queue/backoff, power loss, and OTA rollback.

### Commands


---

## 22. Security Review

### Authentication

- Operational device auth is the strongest implemented path: high-entropy per-device secret, SHA-256 hash storage, fixed-time comparison, validity/revocation/device-state checks.
- Development staff identity is intentionally insecure and user-selectable.
- Development factory key is static configuration, not machine identity.
- Bootstrap auth is absent despite credential storage.
- No production staff authentication exists; production currently omits internal routes entirely.

### Authorization

- Development endpoint filters enforce exact roles.
- Session reads/cancel filter by dealer external ID and return 404 cross-dealer.
- Device telemetry resolves all ownership server-side.
- No formal ASP.NET authorization policies for staff/factory; no claims-based tenant middleware.

### Input validation

- Strict unknown-member JSON protects telemetry from client-supplied ownership.
- Telemetry range/size/schema validation is comprehensive.
- Legacy/factory/session services normalize and validate most strings/numbers.
- Known gap: normalization calls `.Trim()` on non-nullable runtime strings before null validation; explicit JSON null can become 500.
- Database lacks tank asset uniqueness and check constraints for many numerical/state invariants.

### Output encoding and XSS

- React escapes rendered strings by default.
- No `dangerouslySetInnerHTML` is used.
- API returns serializer-generated JSON/Problem Details.
- No Content Security Policy or security headers are configured.

### Encryption and transport

- Local connection trusts server certificate and uses HTTP; acceptable only for local Development.
- Outside Development, `UseHttpsRedirection` is enabled, but certificate/endpoint/HSTS is absent.
- Secrets are SHA-256 hashes at rest. This is appropriate for random 256-bit secrets; no salt/KDF is necessary for brute-force resistance if generation is correct.
- SQL database encryption, TDE, backup encryption, and column encryption are unspecified.
- Firmware flash encryption, secure boot, encrypted NVS, and TLS are planned only.

### Secrets handling

- Device secret returned once in legacy browser flow and retained in React state until reset/navigation.
- Factory endpoint accepts only bootstrap hash.
- `.gitignore` excludes common env files.
- No vault, rotation, secret scanning, or log-redaction policy.
- Never log Authorization, factory key, bootstrap plaintext, operational plaintext, or customer Wi-Fi.

### API security

- 64 KiB body cap.
- Telemetry fixed rate limit 10/device/minute; no queue.
- No factory/staff/bootstrap rate limits.
- Rate limiter is in process and not replica-consistent.
- EF parameterization protects against SQL injection.
- Problem Details may expose generic titles; no explicit production exception detail configuration in source.
- OpenAPI/Swagger Development-only.

### CSRF

No cookies or ambient browser credentials are used; APIs use explicit headers, so classic CSRF does not currently apply. If OIDC cookies are introduced, add SameSite/antiforgery protections.

### CORS

No CORS middleware. Local SPA uses Vite same-origin proxy. Separate-origin production frontend would fail browser requests unless a reverse proxy preserves same origin or a tightly scoped CORS policy is added.

### Additional findings

1. `LastUsedAtUtc` is written before body validation; valid-token junk requests count as use.
2. Legacy commission bypasses factory/bootstrap controls.
3. Bootstrap sessions cannot activate and can leave device Commissioning until cancel/lazy expiry.
4. Expiry with a provisional credential revokes it but current branch does not also reset device status.
5. No audit on failed development factory authentication.
6. No distributed replay/rate controls beyond telemetry key uniqueness.
7. Worker and ticket plan are nonfunctional.
8. NPM lock depends on internal registry and install reports advisories.

---

## 23. Local Development Guide

### 1. Clone repository


If reconstructing without Git history, create the structure and files using Sections 4-8.

### 2. Install dependencies

Verify the tools from Section 3:


Restore backend and frontend dependencies:


Do not build root `WaterFlex.SaltMonitor.slnx`; it is empty. Frontend packages are restored from the public npm registry via the committed lock file.

### 3. Configure environment variables

Development LocalDB needs no connection variable. To override it:


Factory registration additionally requires a key in the API process:


`ASPNETCORE_ENVIRONMENT` can be set explicitly, but the documented run command passes `--environment Development`.

### 4. Start database


### 5. Run migrations


Use Infrastructure as startup because it owns EF Design and the design-time factory.

### 6. Build backend and frontend


Firmware, if PlatformIO is installed:


### 7. Start backend

Terminal A from repository root:


Verify:


### 8. Start frontend

Terminal B:


Open `http://localhost:3000`. Port 3000 must be free because Vite uses `strictPort: true`.

### 9. Start optional worker

Terminal C:


Expect only a heartbeat every 30 seconds. It does not process deliveries.

### 10. Run tests

Stop the API before Debug builds/tests if Windows locks output assemblies, or keep the API in Release while tests build Debug.


Expected .NET discovery: 35 cases. There are no frontend or firmware test suites.

### 11. Exercise development workflows

- Operations: choose Alex Morgan and open `/fleet`.
- Legacy provisioning: choose Jordan Lee or Sam Rivera and open `/provision`.
- Factory API: use `X-WaterFlex-Factory-Key` matching the environment value and an operator header.
- Pending session: register a factory device first, then use a dealer identity to create/get/cancel a session.
- Device telemetry: use a token returned by legacy commission and Swagger's Authorize control.

### 12. Firmware upload and monitor

Connect the Nano by USB:


Expected output each second: `distance=<n> mm` or `sensor read error`.

### 13. Debug application

- API: use VS Code/Visual Studio with `--environment Development --urls http://localhost:5188`; no checked-in launch profile exists.
- Web: use Vite HMR and browser developer tools.
- SQL: use SQL Server Object Explorer or SQLCMD.
- Firmware: use PlatformIO monitor at 115200.

---

## 24. Production Deployment Guide

### Production readiness warning

The repository cannot deliver the full internal/bootstrap product in Production as-is:

- Staff, factory, operations, and provisioning routes are mapped only in Development.
- No production identity or real customer directory exists.
- Bootstrap activation is incomplete.
- Firmware has no Wi-Fi, TLS, activation, or telemetry client.
- No production infrastructure, SPA server, container, or pipeline configuration exists.
- Worker and delivery-ticket processing are nonfunctional.

The steps below can deploy only the implemented anonymous health and active-device telemetry API unless those blockers are completed first.

### 1. Provision infrastructure

Starting from an empty environment, select and provision:

1. WaterFlex hosting platform, account, subscription/project, region, and resource naming.
2. Managed SQL Server service and database with high availability appropriate to the pilot.
3. Private/public networking, firewall rules, outbound access, and API ingress.
4. Stable sensor API DNS name and publicly trusted TLS certificate.
5. Static SPA hosting with unknown-path rewrite and same-origin `/api` routing.
6. Central secret manager, logs, metrics, traces, dashboards, and alerts.
7. Backup schedule, retention, restore drills, RPO, and RTO.
8. Artifact registry if containers/packages are introduced.

Provider-specific commands cannot be recovered because the repository contains no IaC or hosting choice.

### 2. Complete production code blockers

Before exposing internal/bootstrap workflows:

1. Replace Development staff filters with authenticated claims policies.
2. Replace fixture directory with the real WaterFlex customer/location/tank adapter.
3. Map internal/factory routes in Production only after protecting them.
4. Implement bootstrap authentication, activation, first-telemetry completion, and recovery.
5. Remove or permanently isolate legacy immediate-token commission.
6. Add configurable frontend API/Swagger URLs and production identity UI.
7. Add CORS only if same-origin cannot be maintained.
8. Add forwarded-header trust, HSTS, security headers, readiness checks, redaction, and observability.
9. Implement the Worker/outbox or omit Worker deployment.

### 3. Create database and identities

Create a database named `WaterFlexSaltMonitor` or an environment-specific equivalent. Create separate least-privilege identities:

- Migration identity: schema DDL plus migration-history access.
- Runtime API identity: required SELECT/INSERT/UPDATE and no arbitrary DDL.
- Worker identity: only after Worker persistence is implemented.

Configure encrypted transport, network restrictions, backups, auditing, and monitoring. Example runtime configuration:


Do not use LocalDB or `TrustServerCertificate=True` in Production.

### 4. Build and test immutable artifacts


Archive source revision, locks, checksums, SBOM, test reports, API publish output, web `dist`, and migration bundle.

### 5. Create migration bundle


### 6. Configure secrets and runtime

At minimum:


Do not use `FactoryProvisioning__DevelopmentKey` as production factory authentication. Add and inject the provider-specific production OIDC, factory machine identity, certificate, telemetry, and vault settings after implementing them.

### 7. Apply database migration

Back up and verify restore readiness. Run the bundle once under the migration identity:


Verify the four migration IDs in `__EFMigrationsHistory`. Do not let every API replica race automatic migrations; startup does not auto-migrate today.

### 8. Deploy API

1. Deploy the published API to the selected host.
2. Run `dotnet WaterFlex.SaltMonitor.Api.dll` or the host equivalent.
3. Terminate TLS at trusted ingress or configure Kestrel TLS.
4. Bind health probe to `/health`.
5. Configure trusted forwarded headers before trusting proxy data.
6. Inject runtime SQL and production identity/secrets.
7. Restrict internal/factory routes according to authorization design.
8. Scale beyond one instance only after rate limiting and session behavior are replica-safe.

### 9. Deploy frontend

1. Publish `web/dist` to static hosting.
2. Rewrite every unknown browser path to `index.html`.
3. Prefer same-origin `/api` reverse proxy.
4. Replace the hard-coded localhost Swagger link and Development identity selector.
5. Cache hashed assets immutably; use short/no-cache for `index.html`.
6. Add CSP and security headers once allowed origins/assets are known.

### 10. Deploy Worker only if completed

Current Worker only logs. Do not deploy it expecting ticket creation. A real deployment requires DbContext registration, outbox schema, leasing, bounded retries, dead-lettering, gateway configuration, and health checks.

### 11. Validate production deployment


Then verify:

1. TLS chain and hostname from an external network.
2. Valid operational device telemetry inserts one correctly mapped reading.
3. Replay returns duplicate without a second row.
4. Invalid/revoked token rejection.
5. Database encryption and least privilege.
6. Secret/header/Wi-Fi redaction in logs.
7. Rate limiting and readiness behavior.
8. Real staff/dealer scope, if implemented.
9. Static deep-link rewrite and same-origin API behavior.

Swagger/OpenAPI is intentionally unavailable in current Production code.

### 12. Release process

Use test -> staging physical sensor -> internal pilot -> controlled customer pilot. Require approvals around migration and firmware. Record artifact versions, migration, configuration, firmware, and rollout cohort. Define canary thresholds for telemetry failures, activation latency, offline rate, and support incidents.

### 13. Rollback process

- Application: redeploy previous immutable API/static/Worker artifacts.
- Database: prefer forward fixes. EF down migrations can destroy bootstrap/session data; restore a tested backup only through an approved incident procedure.
- Frontend: restore prior static artifact and invalidate `index.html` cache.
- Firmware: signed rollback/anti-rollback behavior is not implemented and must be designed.
- DNS/TLS: preserve prior endpoints and certificates through rollback window.

---

## 25. Troubleshooting

### Building the root solution does nothing

- **Symptom:** `dotnet build WaterFlex.SaltMonitor.slnx` succeeds without projects.
- **Cause:** root solution is empty.
- **Resolution:** build `backend/WaterFlex.SaltMonitor.slnx`.
- **Prevention:** remove or populate root solution.

### Root npm install does not install frontend

- **Symptom:** root `npm install` has no app dependencies.
- **Cause:** root lock contains zero packages and no root package manifest.
- **Resolution:** use `npm --prefix .\web ci`.
- **Prevention:** remove empty root lock or add workspace configuration.

### .NET DLL/executable copy is locked

- **Symptom:** MSB3026/MSB3027 copying API dependencies/apphost.
- **Cause:** running API holds Debug/Release output.
- **Resolution:** stop listener/process, or build different configuration.


- **Prevention:** use `dotnet watch` or separate publish output; stop host before rebuilding same output.

### EF says startup project lacks Design package

- **Symptom:** migration command with API startup fails.
- **Cause:** EF Design is only in Infrastructure.
- **Resolution:** use Infrastructure for both `--project` and `--startup-project`.

### EF pending model changes after migration generation

- **Symptom:** `database update --no-build` says pending changes.
- **Cause:** generated migration not embedded in stale assembly.
- **Resolution:** rebuild Infrastructure, then rerun, or omit `--no-build`.

### LocalDB migration lock release error in tests

- **Symptom:** cannot release `__EFMigrationsLock` during parallel tests.
- **Cause:** concurrent LocalDB migration fixtures.
- **Resolution:** assembly disables xUnit parallelization.
- **Prevention:** retain `AssemblyInfo.cs` or use a managed fixture strategy.

### LocalDB not found

- **Symptom:** SQL connection/instance errors.
- **Cause:** LocalDB not installed/running.
- **Resolution:** install SQL Express LocalDB and run `sqllocaldb start MSSQLLocalDB`.

### API route returns 404

- **Symptom:** `/swagger`, `/api/v1/ops`, technician, or factory route 404.
- **Cause:** API not running in Development or old binary.
- **Resolution:** start with `--environment Development`, rebuild/restart, inspect OpenAPI.

### Factory route returns 503

- **Cause:** `FactoryProvisioning__DevelopmentKey` absent in API process environment.
- **Resolution:** set it in same terminal before `dotnet run`.

### Internal route returns 401/403

- **Cause:** missing/unknown identity or wrong seeded role.
- **Resolution:** technician routes use `north-star-jordan`/`lakes-water-sam`; operations use `wf-ops-alex`.

### Frontend remains loading or shows API unavailable

- **Cause:** API down/wrong port, Vite proxy target mismatch, or selected wrong role.
- **Resolution:** verify `/health`, API 5188, Vite 3000, identity selector.

### Vite strict port failure

- **Symptom:** dev server exits because 3000 occupied.
- **Cause:** `strictPort: true`.
- **Resolution:** stop existing listener or intentionally change config and links.

### Vite HMR fails after installing dependency

- **Symptom:** first request for newly installed module returns 500/HMR failure.
- **Cause:** stale dependency optimizer.
- **Resolution:** restart Vite; if needed remove `web/node_modules/.vite`.

### Web Serial unsupported

- **Symptom:** calibration says Web Serial unavailable.
- **Cause:** unsupported browser/non-secure origin/device permissions.
- **Resolution:** current Edge/Chrome on localhost/HTTPS; connect USB and grant port.
- **Prevention:** production Android SoftAP flow must replace bench path.

### Sensor capture timeout/unstable

- **Cause:** firmware not emitting expected line, wrong baud, alignment/noisy sensor, fewer than five valid samples, spread >100 mm.
- **Resolution:** monitor at 115200, verify `distance=N mm`, wiring/alignment, retry.

### Commissioning conflict

- **Cause:** duplicate serial/hardware, active tank, pending session reservation, or unique race.
- **Resolution:** inspect Devices, DeviceInstallations, CommissioningSessions; cancel/expire correct pending session; never delete production history casually.

### Device telemetry 401/403/409

- 401: malformed/wrong/expired token.
- 403: revoked credential or device not Active.
- 409: no current installation or calibration.
- Use Swagger only in Development; never log token.

### Firmware build cannot run

- **Cause:** PlatformIO not installed or unpinned platform resolution changed.
- **Resolution:** install PlatformIO and pin tested platform/core versions before production.

---

## 26. Complete Recreation Checklist

1. [ ] Provision a 64-bit Windows machine.
2. [ ] Install Git and verify version.
3. [ ] Install .NET 10 SDK and verify `global.json` resolution.
4. [ ] Install Node 20+ and npm.
5. [ ] Install SQL Server Express LocalDB and start `MSSQLLocalDB`.
6. [ ] Install SQLCMD optionally.
7. [ ] Install Python and PlatformIO for firmware work.
8. [ ] Install current Edge/Chrome for Web Serial.
9. [ ] Create/clone repository and enter root.
10. [ ] Create root `.gitignore`, `global.json`, tool manifest, and NuGet config.
11. [ ] Create `backend/WaterFlex.SaltMonitor.slnx`; do not rely on empty root solution.
12. [ ] Create Domain, Ingestion, Operations, Provisioning, Rules, Infrastructure, API, Worker, and Tests projects.
13. [ ] Add internal project references in dependency order.
14. [ ] Add exact NuGet packages/versions.
15. [ ] Create `Directory.Build.props` compiler defaults.
16. [ ] Implement Domain records, fill formula, monitoring policy, staff identity, ticket abstraction.
17. [ ] Implement telemetry and legacy commissioning contracts/validator.
18. [ ] Implement operations contracts.
19. [ ] Implement bootstrap/factory/session contracts.
20. [ ] Implement all EF entities and mappings.
21. [ ] Implement design-time DbContext factory and DI registration.
22. [ ] Implement fixture customer and identity directories.
23. [ ] Implement device token validation and ASP.NET scheme.
24. [ ] Implement telemetry ingestion transaction/dedupe.
25. [ ] Implement legacy commission transaction.
26. [ ] Implement fleet query service.
27. [ ] Implement factory registration and session services.
28. [ ] Implement API middleware, endpoint modules, OpenAPI transformers, and rate limit.
29. [ ] Implement Worker heartbeat and delivery stub exactly as current behavior.
30. [ ] Create InitialCreate migration.
31. [ ] Create tank-depth rename migration.
32. [ ] Create dealer migration.
33. [ ] Create bootstrap provisioning migration.
34. [ ] Apply migrations to LocalDB.
35. [ ] Create all 10 test classes and nonparallel assembly setting.
36. [ ] Run and pass 35 discovered .NET cases.
37. [ ] Create web manifest and install exact direct dependencies.
38. [ ] Create Vite config, HTML entry, root composition, router, identity context.
39. [ ] Implement themed select.
40. [ ] Implement operations API/types, fleet page, detail page.
41. [ ] Implement provisioning API/types, workflow, and Web Serial reader.
42. [ ] Recreate global CSS variables/layout/responsive behavior.
43. [ ] Run `npm ci` and production build.
44. [ ] Create PlatformIO config and UART firmware skeleton.
45. [ ] Build/upload/monitor firmware where PlatformIO/hardware is available.
46. [ ] Set Development factory key in API terminal.
47. [ ] Start API on port 5188 in Development.
48. [ ] Verify health, OpenAPI, Swagger, and all 14 routes.
49. [ ] Start Vite on strict port 3000.
50. [ ] Verify WaterFlex employee fleet workflow.
51. [ ] Verify dealer legacy provisioning workflow.
52. [ ] Verify factory registration and pending-session create/get/cancel via API.
53. [ ] Confirm factory/session flow creates no installation or operational token.
54. [ ] Decide production hosting, SQL, identity, DNS/TLS, secret vault, observability, and backup architecture.
55. [ ] Implement production staff/dealer/factory identity.
56. [ ] Implement real WaterFlex directory integration.
57. [ ] Implement bootstrap authentication and retry-safe activation.
58. [ ] Implement first-telemetry completion/bootstrap consumption.
59. [ ] Implement factory CLI/NVS/labels and sensor SoftAP portal.
60. [ ] Implement firmware Wi-Fi/TLS/storage/queue/telemetry/OTA security.
61. [ ] Replace field Web Serial/legacy commission with pending-session Android flow.
62. [ ] Add production config, security headers, health checks, redaction, metrics, alerts.
63. [ ] Add frontend/firmware/end-to-end tests.
64. [ ] Add build/security/deployment pipeline.
65. [ ] Provision production SQL and run migration bundle under controlled identity.
66. [ ] Publish and deploy API/static SPA; deploy Worker only after actual implementation.
67. [ ] Run deployment smoke/security/telemetry tests.
68. [ ] Execute physical failure matrix and controlled pilot.
69. [ ] Document rollback and support runbooks.

---

## 27. AI Reconstruction Package

### Dependency Graph


### Build Order

1. Root SDK/tool/feed configuration.
2. Domain.
3. Ingestion, Operations, Provisioning, and Rules in parallel.
4. Infrastructure.
5. API and Worker.
6. Tests.
7. Web dependency restore and Vite build.
8. Firmware PlatformIO build.

### Startup Order

1. SQL Server/LocalDB.
2. Apply migrations.
3. API with Development environment and factory key.
4. Vite dev server.
5. Optional heartbeat Worker.
6. Optional Arduino serial connection.

### Configuration Order

1. Resolve .NET SDK through `global.json`.
2. Restore EF tool and NuGet packages using configured feed.
3. Set SQL connection or accept Development fallback.
4. Set Development factory key if factory API is used.
5. Set API environment/URL.
6. Restore web lock and ensure Vite proxy matches API.
7. Configure and pin PlatformIO before firmware reproducibility claims.

### Database Order

1. `InitialCreate`.
2. `UseTankDepthCalibration`.
3. `AddDealerOwnership`.
4. `AddBootstrapProvisioning`.
5. Fixture data is upserted by business operations, not seeded by migrations.

### Deployment Order

1. Complete missing production authentication, activation, and configuration.
2. Provision SQL, networking, DNS/TLS, secrets, and observability.
3. Build/test immutable artifacts.
4. Back up and migrate SQL once.
5. Deploy API.
6. Deploy same-origin SPA with rewrite/proxy.
7. Deploy Worker only after outbox implementation.
8. Validate health, authentication, telemetry, persistence, and security.
9. Pilot firmware/hardware.

### Service Dependency Tree


### End-to-End Request Flow


### Complete System Architecture


### Missing Knowledge Risks

1. Production hosting provider, account, region, topology, SKUs, and scaling.
2. Production SQL version/SKU, authentication, network, backup/retention, RPO, and RTO.
3. Real WaterFlex customer/location/tank API contracts and credentials.
4. Entra ID/OIDC tenant, applications, claims, dealer mapping, and roles.
5. Factory workstation machine identity, key custody, serial allocation, NVS tooling, label format, and printer.
6. Bootstrap activation request/response details and recovery implementation.
7. Public sensor hostname, certificate lifecycle, and firmware CA strategy.
8. Customer Wi-Fi support matrix and Android captive-portal behavior.
9. Firmware platform/core/library exact versions and production implementation.
10. Hardware manufacturing, approved tank profiles, mounting tolerances, and sensor quality algorithm.
11. Telemetry retention, archival, privacy, data residency, and expected fleet/load.
12. Delivery-ticket API, product/quantity logic, debounce/cooldown, and outbox schema.
13. Worker deployment, lease, retry, and dead-letter behavior.
14. Monitoring vendor, logs/metrics/traces, alert thresholds, and support escalation.
15. CI/CD platform, branch/release/versioning policy, and artifact registry.
16. Container/orchestrator decision.
17. Secrets vault and rotation policy.
18. Firmware signing, secure boot, flash encryption, OTA, rollback, and key custody.
19. Production frontend host, API origin strategy, CSP/CORS, and security headers.
20. Disaster recovery and rollback approvals.

### Assumptions Required

- Recreate the implemented repository rather than every feature described in Plan C.
- Windows LocalDB is acceptable for development/testing.
- Node 20+ is acceptable despite observed Node 24.
- Development identity fixtures remain intentional locally.
- SQL Server remains the persistence engine.
- Legacy commission remains only as transitional Development functionality.
- Factory registration receives a securely generated hash, even though the factory CLI is absent.
- The root empty solution and lock are accidental/non-authoritative artifacts.

### Reconstruction Confidence Score

**78/100.**

The implemented local API, database, tests, React UI, and UART firmware skeleton can be recreated with high confidence from this guide. Perfect full-system recreation is blocked by absent production identity, real WaterFlex integration, hosting/IaC, CI/CD, container definitions, bootstrap activation, factory tooling, production firmware, captive portal, telemetry queue, OTA/security keys, ticketing/outbox behavior, and operations lifecycle actions. Any implementation of those missing areas requires new architectural decisions rather than reconstruction.

---

## 28. Principal Engineering Exact-Recreation Gap Closure

### Purpose, authority, and evidence limits

This section is the normative audit for rebuilding from this document on a blank machine. It was derived only from this guide. It does not claim that a proposed default below exists in the repository.

Every statement in this section uses one of these labels:

- **IMPLEMENTED-BENCH**: behavior the guide says currently exists and must be preserved for a faithful local reconstruction.
- **PROPOSED-RECONSTRUCTION-DEFAULT**: an exact choice added to remove ambiguity when the original value cannot be recovered from this guide. It is a new decision, not evidence of current code.
- **PLANNED-PRODUCTION**: required target behavior that is not implemented today.
- **OWNER-INPUT-REQUIRED**: a business, infrastructure, security, or hardware fact for which inventing a value would be unsafe. The exact default is to fail closed or keep the feature disabled until supplied.

If an earlier descriptive section conflicts with this section, preserve **IMPLEMENTED-BENCH** behavior for compatibility and use the proposed value only in a separately identified reconstruction-baseline change. Never silently present a proposed production behavior as current behavior.

### Exactness finding

The guide is not, by itself, sufficient for byte-for-byte or pixel-for-pixel recreation. It describes file names and behavior but does not contain the complete contents of the C# source, project XML, migrations, TypeScript/TSX, 2,543-line stylesheet, 924-line provisioning component, package lock, or firmware source. Multiple implementations can satisfy the prose while producing different DOM trees, SQL migration identifiers, binaries, CSS layout, error text, accessibility behavior, and race behavior.

The earlier score of 78/100 applies to architecture-level local reconstruction when the repository source is also available. From this guide alone, use these confidence scores:

| Target | Confidence | Reason |
|---|---:|---|
| Byte-identical repository | 10/100 | Full file bodies, generated files, line endings, and lock artifacts are absent |
| Behaviorally compatible local backend | 70/100 | Routes, schema, validation, and transactions are described, but implementation details and complete fixtures are absent |
| Pixel/DOM-identical frontend | 30/100 | Component summaries exist, but exact markup, copy, class names, CSS, focus order, and screenshots are absent |
| Current UART bench firmware | 75/100 | Wiring, frame format intent, baud rates, and output are mostly recoverable; toolchain and parser details are not pinned |
| Planned field bootstrap product | 20/100 | Activation, portal, networking, durable state, OTA, and production hardware are design-only |

Exact recreation from one document requires one of the following additions:

1. Append every authoritative text file verbatim to this guide, including generated migrations and lockfiles.
2. Append a Base64-encoded, SHA-256-identified source archive and an extraction command.
3. Relax the requirement from exact recreation to behaviorally compatible reconstruction and adopt every proposed default in this section.

For option 1 or 2, add a machine-readable manifest with this schema and sort it by path using ordinal comparison:


The manifest must cover hidden configuration, all project files, all four EF migration source/designer files and snapshot, `package-lock.json`, and every test. Do not include `bin`, `obj`, `node_modules`, `.pio`, or `dist`; those must be reproduced from locked inputs.

### Embedded authoritative source snapshot

The exact-source blocker is closed by the payload below. It is a deterministic ZIP containing every authoritative non-generated application file available to this guide's author, excluding only this guide to avoid recursive self-inclusion. The extraction script copies the guide into the reconstructed root after verification.

Snapshot identity:


The archive contains `SOURCE_MANIFEST.json` plus the 104 files. The manifest records each path, byte length, SHA-256, UTF-8/BOM status, and observed line-ending style. It explicitly excludes this guide, generated build directories, Git/editor caches, `*.tsbuildinfo`, and generated `web/vite.config.js`/`.d.ts`. The extraction script requires an empty destination, verifies the ZIP before extraction, verifies every file after extraction, removes the internal manifest, and copies this guide into the destination.

Run from the directory containing this Markdown file:


<!-- SOURCE_ARCHIVE_BASE64_BEGIN -->
<!-- SOURCE_ARCHIVE_BASE64_END -->

### Current bench versus planned production boundary

| Concern | IMPLEMENTED-BENCH now | PLANNED-PRODUCTION target | Do not conflate |
|---|---|---|---|
| Device connection | USB from Nano to a Chromium browser | Device-owned 2.4 GHz Wi-Fi and public HTTPS | The current firmware cannot post telemetry |
| Sensor firmware | One A02YYUW UART frame parser and one diagnostic line per loop | Sampling, quality, persistent identity, setup portal, activation, queue, TLS, telemetry, OTA | A successful UART build is not production firmware validation |
| Technician flow | Browser Web Serial, five samples, median, immediate legacy commission | Technician creates pending session; sensor discovers it and activates itself | Production must never return an operational secret to the technician browser |
| Factory flow | Development API accepts bootstrap hash and creates Registered inventory | Factory station generates/stores device secret, flashes NVS, registers hash, prints non-secret identity label | The Development static factory key is not a production machine identity |
| Activation | Missing | Retry-safe bootstrap-authenticated activation with a provisional operational credential | A PendingSensor session is not an active installation |
| First telemetry | Active legacy devices only | Completes activation, consumes bootstrap, and makes device Active | Current DeviceToken rejects non-Active devices |
| Staff identity | User-selectable Development header | Entra-backed claims and dealer scope | Development identity is not authentication |
| Customer source | Three in-memory fixtures | Authoritative WaterFlex adapter | Fixture data must not appear in Production |
| Delivery | Stub exists but is not registered or invoked | Transactional outbox and RouteFlex adapter after a defined low-salt policy | No current telemetry creates a ticket |
| Hosting | LocalDB, Kestrel HTTP, Vite proxy | Managed SQL, public TLS, same-origin SPA/API, secrets and observability | The repository has no deployable production topology |

The two supported reconstruction profiles are therefore:

- `bench-local`: reproduce only current Development UI/API/LocalDB/UART behavior.
- `field-pilot`: a new implementation that adopts all planned defaults below and must be versioned and tested separately.

### Complete gap register

| ID | Missing or ambiguous item | Why exact recreation fails | Required guide addition or exact default |
|---|---|---|---|
| SRC-01 | Full source bodies | File summaries do not determine code | Add the source manifest and verbatim/archive payload described above |
| SRC-02 | Generated EF migration bodies/model snapshot | Names and schema do not determine generated C# or migration IDs | Include all migration `.cs`, designer files, snapshot, and hashes |
| SRC-03 | Line endings/encoding/executable bits | These affect hashes and some tools | UTF-8 without BOM, LF for source/config/Markdown; record exceptions in manifest |
| TOOL-01 | Frozen OS image | "64-bit Windows" is not an exact runtime | Use Windows 11 24H2 x64 build 26100 as the reconstruction baseline and record the installed build |
| TOOL-02 | SDK roll-forward | `latestPatch` changes over time | For frozen rebuilds use SDK `10.0.302` with `rollForward: disable`; retain `10.0.300/latestPatch` only to mimic current policy |
| TOOL-03 | Node/npm mismatch | Guide permits many Node versions | Freeze Node `24.18.0` and npm `11.16.0` for the source-observed profile |
| TOOL-04 | PlatformIO/Python versions | Neither is locked | Freeze Python `3.13.x` and PlatformIO Core `6.1.18`; record the exact Python patch and wheel hashes in the source manifest |
| TOOL-05 | Browser build | Web Serial and screenshots can vary | Freeze one Microsoft Edge Stable installer and SHA-256 in the artifact manifest; use 100% zoom and default font scaling |
| NPM-01 | Portable lock registry | Lock resolved URLs must be reachable on any build machine | Commit a lockfileVersion 3 lock whose `resolved` URLs point to the public npm registry (`https://registry.npmjs.org/`); if a private mirror is used, update resolved URLs accordingly and never delete the lock in an exact build |
| NPM-02 | Package manager declaration | Global npm can drift | Add `"packageManager": "npm@11.16.0"` and exact `engines` |
| NPM-03 | Frontend typecheck | Vite transpilation accepts type errors | Add the exact `tsconfig.json` and `typecheck` script below |
| NPM-04 | Transitive immutability | A prose inventory is not a lock | Treat `web/package-lock.json` plus its hash as authoritative; run only `npm ci` |
| DOTNET-01 | NuGet transitive lock | Direct versions do not freeze transitives | Set `RestorePackagesWithLockFile=true`, generate and commit `packages.lock.json` for every project |
| DOTNET-02 | Runtime identifiers/publish mode | Published bits vary | Bench default is framework-dependent `win-x64`; no trimming, single-file, ReadyToRun, or self-contained publish |
| WEB-01 | Exact `package.json` | Name/version/scripts/engines are incomplete | Use the manifest below |
| WEB-02 | Exact Vite behavior | Host, preview, proxy flags, output, source maps are omitted | Use the Vite configuration below |
| WEB-03 | Route DOM and copy | Component descriptions do not define markup | Add verbatim TSX/CSS or adopt the DOM contract below |
| WEB-04 | Loading/error/empty/focus behavior | Visual and accessible states are incomplete | Adopt the state rules below and add component/browser tests |
| WEB-05 | CSS values and responsive geometry | A few colors and breakpoints cannot reproduce 2,543 lines | Include the stylesheet verbatim; token/layout defaults below are compatibility defaults only |
| WEB-06 | Static hosting fallback/proxy | Deep links fail without host rules | Rewrite non-file routes to `/index.html`; proxy `/api/*` without stripping `/api` |
| API-01 | Serializer option details | Case handling and number parsing can differ | Adopt the JSON defaults below |
| API-02 | Exact errors | Titles/error codes are not complete for every branch | Add a golden response fixture for every status branch |
| API-03 | Explicit-null behavior | Current normalization can throw 500 | Compatibility profile preserves it; corrected baseline returns validation 400 and adds tests |
| API-04 | Configuration source precedence | No appsettings files exist | Command line > environment > Development fallback; Production must fail startup without SQL configuration |
| API-05 | Bootstrap endpoint/auth | Data model exists but no route can use it | Implement the production activation contract below only in `field-pilot` |
| FIX-01 | Full Development customer fixture | Only summaries and one branch are shown | Include fixture source verbatim or use the canonical fixture below |
| FIX-02 | Test GUIDs, clocks, secrets, database helper | Counts do not determine tests | Add the deterministic fixture contract below and verbatim test source |
| PIO-01 | Platform/core pin | `espressif32` floats | Pin `platformio/espressif32@6.12.0` and record resolved packages |
| PIO-02 | Board manifest | Board defaults can change with platform | Archive the resolved board JSON and use logical `D4`/`D5`, not raw numeric Arduino pins |
| PIO-03 | Partition table/build flags | Neither is stated | Check in the exact partition CSV and flags below |
| FW-01 | UART parser algorithm | "validates checksum" omits resynchronization and timeout details | Adopt the byte state machine below |
| FW-02 | Sensor electrical mode | RX-open behavior and voltage assumptions are absent | Use 3.3 V supply, leave sensor RX open for processed output, verify TX <=3.3 V |
| FW-03 | Production firmware state machine | Entire field behavior is absent | Adopt the proposed field-pilot state machine below |
| FW-04 | Durable queue/backoff/time | Offline operation is only an intent | Adopt the queue/network defaults below |
| PORTAL-01 | SoftAP/portal UX and protocol | No implementation contract exists | Adopt the captive portal contract below |
| SECURITY-01 | TLS trust/time/bootstrap handling | No firmware network security exists | Adopt TLS 1.2+, CA validation, SNTP, encrypted storage, and no-insecure fallback defaults below |
| SECURITY-02 | Secure boot/flash/OTA keys | Key custody is unknown | Keep irreversible fuses disabled on Nano benches; require owner-managed keys before production enablement |
| HW-01 | Exact BOM and power/mounting | Board and sensor names are insufficient for a field installation | Use the bench BOM below; field enclosure/power/certification remains gated |
| TEST-01 | Frontend/firmware/E2E dependencies | No test packages or fixtures exist | Add the proposed test stack and acceptance matrix below |
| START-01 | Serial-port ownership | PlatformIO monitor and browser cannot own the COM port together | Close the monitor before Web Serial capture |
| START-02 | Initial business data | There is no seed | Create data only through legacy commission or factory/session APIs; an empty fleet after migration is expected |
| PROD-01 | Real directory, identity, ticket API, DNS, cloud account, keys | These are organization-owned facts | Fail closed and keep corresponding routes/features disabled until supplied |

### Frozen blank-machine toolchain

The exact proposed reconstruction baseline is:

| Tool/runtime | Exact baseline | Verification |
|---|---|---|
| Windows | Windows 11 24H2 x64, build 26100 | `winver` and `Get-ComputerInfo` |
| PowerShell | Windows PowerShell 5.1 for documented scripts | `$PSVersionTable.PSVersion` |
| Git | `2.55.0.windows.3` | `git --version` |
| .NET SDK | `10.0.302` | `dotnet --version` |
| EF local tool | `10.0.10` | `dotnet tool run dotnet-ef --version` |
| Node | `24.18.0` x64 | `node --version` |
| npm | `11.16.0` | `npm --version` |
| SQL LocalDB | `17.0.4025.3` x64, instance `MSSQLLocalDB` | `sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "SELECT @@VERSION"` |
| Python | latest recorded `3.13.x` patch accepted by the pinned PlatformIO Core | `py -3.13 --version` |
| PlatformIO Core | `6.1.18` | `py -3.13 -m platformio --version` |
| PlatformIO Espressif platform | `platformio/espressif32@6.12.0` | `pio pkg list -d firmware` |
| Browser | one archived Microsoft Edge Stable x64 build | `edge://version` |

`winget install` without a version is convenience setup, not an exact reconstruction mechanism. The artifact manifest must record installer URL, product version, SHA-256, install scope, and architecture. A frozen build must abort when a version differs; it must not silently roll forward.

Use this frozen `global.json` in an exact-build profile:


The current repository policy remains `10.0.300` plus `latestPatch`; preserve that file if the goal is source compatibility rather than a time-frozen build.

For NuGet reproducibility, add this property to `backend/Directory.Build.props` in the reconstruction baseline, regenerate all project locks once, and use `dotnet restore --locked-mode` thereafter:


The package lock files themselves, not a transitive package list in prose, are authoritative.

### Exact frontend build configuration default

The current application has no `tsconfig.json`; that absence is part of **IMPLEMENTED-BENCH**. A blank reconstruction that needs a deterministic standalone typecheck must add this **PROPOSED-RECONSTRUCTION-DEFAULT** manifest:


Do not use caret or tilde ranges in this baseline. Use this `tsconfig.json`:


Use this exact proposed Vite configuration:


Use UTF-8 without BOM for this exact HTML shell:


The lockfile must use `lockfileVersion: 3`, include integrity values, and be generated once with npm 11.16.0. Changing registry URLs while retaining integrity is permitted only as a reviewed lockfile migration. Deleting the lock and accepting newer transitives is not exact recreation.

### Frontend route, API, DOM, and interaction contract

#### Route contract

The canonical Development route table remains:

| Path | Exact route action | Initial data calls | Empty/error behavior |
|---|---|---|---|
| `/` | Replace-navigate to `/fleet` | None before redirect | No intermediate page |
| `/fleet` | Render fleet page | dealers, summary, and devices concurrently | Preserve filters; show one page error region and retry control |
| `/fleet/:deviceId` | Render detail page | detail and `range=7d` readings concurrently | 404 shows not-found state with back link |
| `/provision` | Render legacy five-step workflow | Development users through provider, customers on search | Wrong role shows access state; it must not submit as employee |
| `*` | Replace-navigate to `/fleet` | None before redirect | Unknown path is not a separate 404 page |

Static production hosting must apply this order:

1. Serve an existing asset path unchanged.
2. Proxy `/api` and `/api/*` to the API without path rewriting.
3. Serve `/index.html` for every other GET/HEAD path.
4. Return 404 for unknown non-GET paths; never return SPA HTML to an API request.

#### API client defaults

- Use relative URLs only in the browser.
- Send `Accept: application/json` on every API call.
- Send `Content-Type: application/json` only when a JSON body exists.
- Read `waterflex-development-user` immediately before each request and send it as `X-WaterFlex-Development-User` when nonblank.
- Use `credentials: 'same-origin'`; no cookies are currently expected.
- A non-2xx status is an error even if parsing the body fails.
- Error message order is validation messages, `detail`, `title`, then `Request failed with status <n>`.
- Ignore `AbortError` caused by effect cleanup. Surface every other network error as `The WaterFlex API is unavailable.`
- Do not automatically retry mutations. The current GET clients also do not retry.
- A response arriving after its effect is aborted must not update state.

#### Fleet URL state

Use these exact query keys and defaults:

| UI value | Query key/value | Omission/default rule |
|---|---|---|
| Search text | `search=<trimmed text>` | Omit when blank |
| Dealer | `dealerId=<external ID>` or `unassigned` | Omit for all dealers |
| Reporting | `reportingStatus=reporting|stale|offline|neverReported` | Omit for all |
| Low fill | `belowThreshold=true` | Omit when false |
| Sort | `sort=attention|lastReported|fillAscending|fillDescending|customer` | Omit `attention` |
| Page | `page=<positive integer>` | Omit page 1 |

The UI always requests `pageSize=25`, even though the API default is 50. Any search/filter/sort change sets page to 1 in the same navigation. Search input may display immediately while the deferred value drives URL/API work. Back/forward navigation must restore controls from the URL. Unknown query values fall back to defaults and are removed on the next user update.

#### Global DOM defaults

The exact current DOM cannot be inferred. If verbatim TSX is unavailable, use this accessible compatibility contract:


- Use one `h1` per route and ordered `h2` headings for panels.
- Use actual `button`, `a`, `input`, and Radix Select controls; never clickable `div` elements.
- Icon-only buttons require an accessible name and visible tooltip on hover/focus.
- Every form input has a programmatic label, help/error association, and `aria-invalid` when invalid.
- Loading status uses `role="status"`; request failures use `role="alert"`; decorative icons use `aria-hidden="true"`.
- On route change, set focus to `#main-content` without scrolling it unexpectedly.
- A disabled step is not focusable. A completed provisioning step remains reachable through its button.
- A submission button remains disabled while submitting and ignores a second activation.
- Never place the one-time device token in URL state, localStorage, sessionStorage, analytics, or logs.

#### Fleet page states

- While the first request set is pending, retain the page frame and show skeleton rows with fixed height; do not show zero metrics.
- Refresh retains prior successful data and marks it busy; it does not clear the table.
- A total count of zero shows `No devices match these filters.` and a `Clear filters` command.
- A nullable fill, RSSI, firmware, quality, or timestamp renders an em-free ASCII fallback `Not available`, never numeric zero.
- The table has a stable minimum width and an internal horizontal scroller. The document itself must not scroll horizontally at 390 CSS px.
- Row activation uses a normal link to `/fleet/{deviceId}` so open-in-new-tab works.
- Pagination is absent when `totalCount <= pageSize`; otherwise Previous and Next are disabled at boundaries and expose the current page in text.
- Correct the known Updated bug in the reconstruction baseline by comparing `generatedAtUtc` with the current clock; retain self-comparison only when reproducing the current defect intentionally.

#### Detail page states

- Default range is `7d`; valid controls are `24h`, `7d`, and `30d` in that order.
- Changing range refetches readings but retains device detail.
- The page displays at most 50 of the API's chronological readings, newest first.
- No-reading state is `No readings in this range.` and is not an error.
- Calibration values are displayed in centimeters with one decimal; raw readings remain integer millimeters.
- Credential UI displays active/revoked state and last use only, never credential ID/hash/token.
- No chart, recalibrate, replace, retire, or audit action is implied by the current page.

#### Legacy provisioning states

The exact step IDs are `customer`, `location`, `sensor`, `calibration`, and `review`. Completion is a separate result screen.

1. Customer selection requires an exact fixture customer ID.
2. Location selection requires one location and one tank under the selected customer.
3. Sensor requires serial, hardware ID, and model; work order is optional.
4. Calibration requires tank depth and a successful Web Serial capture.
5. Review submits exactly once to the legacy commission endpoint.

Changing customer clears location, tank, reading, review eligibility, and result. Changing location clears tank and reading. Changing tank, serial, hardware ID, model, or depth clears the reading. Navigating backward does not clear otherwise valid values. Restart clears all state including the plaintext token and returns to Customer.

The browser must keep the returned operational token only in component memory. Copy uses `navigator.clipboard.writeText` after a user action; success text clears after 2 seconds. If clipboard access fails, leave the token selectable and announce the failure without deleting it.

#### Web Serial protocol

Use these exact compatibility defaults:

- `navigator.serial.requestPort()` must occur from the Capture button click.
- Open at 115200 baud, 8 data bits, no parity, 1 stop bit, no flow control.
- Decode UTF-8 incrementally and accept CRLF or LF.
- Parse complete lines matching `^distance=(\d+) mm$`; ignore all other complete lines.
- Accept 30 through 4500 mm inclusive.
- Collect exactly five valid samples within 12,000 ms after the port opens.
- Sort ascending and choose element index 2 as the median.
- Reject when `max - min > 100` mm.
- Do not send bytes to the board.
- On success or failure: cancel the reader, release its lock, close the port, and clear timers.
- Map a user-cancelled chooser to `cancelled`, an already-open/missing port to `unavailable`, no five samples to `timeout`, and spread failure to `unstable`.
- PlatformIO Serial Monitor, Arduino Serial Monitor, and any other COM client must be closed first.

#### CSS compatibility tokens and responsive defaults

Pixel identity still requires the complete stylesheet and reference screenshots. If that artifact is unavailable, use this exact proposed token baseline:


- Use 16 px root text, 1.5 body line height, and zero letter spacing.
- The content maximum is 1440 px with 24 px desktop gutters and 16 px mobile gutters.
- Controls are at least 40 CSS px high; icon-only targets are at least 40 by 40 px.
- Panels use at most 8 px radius. Do not nest decorative cards.
- At <=1180 px, reduce multi-panel grids to two columns and retain table scrolling.
- At <=820 px, stack header rows and filter groups; detail/provisioning panels become one column.
- At <=580 px, use one-column metrics, full-width primary form actions, 16 px gutters, and no document overflow.
- Focus is a 2 px solid `--wf-focus` outline with 2 px offset and must not be removed.
- `prefers-reduced-motion: reduce` disables nonessential transition and animation durations.
- Reference captures must cover 1440x900, 1024x768, and 390x844 at 100% zoom with animation disabled. Store image hashes and allow no unexplained pixel delta.

### API and JSON closure defaults

The exact implemented route count is 14. In Development all 14 are mapped; outside Development only `/health` and `/api/v1/device/telemetry` are mapped. The planned activation route would become route 15 only after its implementation.

Use these serializer defaults explicitly rather than relying on framework defaults:


Additional exact defaults:

- Reject a non-JSON body on JSON endpoints with 415.
- Reject malformed JSON, missing required body, and type mismatch with ASP.NET Problem Details 400.
- In the corrected reconstruction baseline, explicit JSON null for required strings is a validation 400; never allow a normalization `NullReferenceException` to become 500.
- Preserve unknown-member rejection on every request contract, including planned activation and portal-to-cloud messages.
- Authentication runs before body validation on telemetry, so `LastUsedAtUtc` updates for a valid token even when the body later fails. This is current behavior, not a recommended change.
- Rate-limit partition key is authenticated Device ID. Window is 60 seconds, permit limit 10, queue limit 0, auto-replenishment true.
- `Retry-After` is integer seconds. A 429 body must contain only the documented non-secret fields.
- Never return stack traces, SQL text, connection strings, hashes, or tokens in Problem Details.
- `/health` remains liveness-only and returns 200 while SQL is unavailable. Add a separate `/ready` only as a production change.
- Production startup must throw a configuration error if `ConnectionStrings:SaltMonitor` is missing. Only Development may use the LocalDB fallback.
- Preserve cancellation from `HttpContext.RequestAborted` through every async database/API operation.

For exact response recreation, add one golden JSON fixture for every success and failure branch of all 14 routes. Normalize only generated GUIDs/timestamps/rowversions in snapshot comparison; property names, null presence, enum casing, status, headers, and message text are contract data.

### Canonical Development and test fixture defaults

The source fixture remains authoritative if recovered. If it is not available, use this **PROPOSED-RECONSTRUCTION-DEFAULT** dataset and do not claim that invented names/addresses match the original implementation:

| Customer | Location | Address | Tank(s) |
|---|---|---|---|
| `WF-C-10482`, account `10482`, North Ridge Apartments | `WF-L-10482-01`, Building A mechanical room | 1820 Ridgeview Ave, Madison, WI 53704 | `WF-A-10482-S1`, Primary softener, 600 lb; `WF-A-10482-S2`, Laundry softener, 350 lb |
| `WF-C-10482`, account `10482`, North Ridge Apartments | `WF-L-10482-02`, Building B mechanical room | 1840 Ridgeview Ave, Madison, WI 53704 | `WF-A-10482-S3`, Primary softener, 600 lb |
| `WF-C-22017`, account `22017`, Baker Family Residence | `WF-L-22017-01`, Utility room | 720 Maple St, Madison, WI 53703 | `WF-A-22017-S1`, Residential softener, 300 lb |
| `WF-C-31804`, account `31804`, Lakeside Dental Group | `WF-L-31804-01`, Main mechanical room | 410 Lake St, Madison, WI 53703 | `WF-A-31804-S1`, East softener, 450 lb; `WF-A-31804-S2`, West softener, 450 lb |

Identities remain exactly:

| User ID | Name | Role | Dealer external ID | Dealer name |
|---|---|---|---|---|
| `wf-ops-alex` | Alex Morgan | `waterFlexEmployee` | null | null |
| `north-star-jordan` | Jordan Lee | `dealerTechnician` | `WF-D-NORTH-STAR` | North Star Water Systems |
| `lakes-water-sam` | Sam Rivera | `dealerTechnician` | `WF-D-LAKES-WATER` | Lakes Water Conditioning |

Use these deterministic defaults in newly written tests unless a test explicitly targets another boundary:


Database test names are `WaterFlexSaltMonitor_Test_<32 lowercase GUID hex characters>`. Each fixture creates the database, applies all migrations, performs test-specific inserts, and deletes it in `DisposeAsync`. Cleanup failure must be reported, not swallowed. Tests remain assembly-serial until the migration-lock strategy changes.

The exact method names, setup code, SQL assertions, and all expected Problem Details still require verbatim test source. The behavior list and count alone cannot reproduce the same suite.

### Bench hardware, wiring, and electrical prerequisites

#### Bench BOM

| Quantity | Exact proposed item | Purpose |
|---:|---|---|
| 1 | Arduino Nano ESP32, Arduino SKU `ABX00083` | ESP32-S3 controller and native USB |
| 1 | DFRobot A02YYUW waterproof UART ultrasonic sensor, SKU `SEN0311` | 30-4500 mm distance measurement |
| 1 | USB-C data cable, <=2 m | Power, upload, and USB CDC serial |
| 1 | Windows computer with a direct USB port | PlatformIO and Chromium Web Serial |
| 3 | Female jumper leads or a locking adapter harness | 3V3, GND, and sensor TX |
| 1 each | 100 nF ceramic and 10 uF electrolytic capacitor | Local sensor supply decoupling when leads are long/noisy |
| 1 | Rigid, nonabsorbing top mount | Holds sensor normal to the measured surface |

Do not power the Nano simultaneously from incompatible external and USB supplies. Do not connect the sensor TX until its idle-high voltage has been measured at <=3.3 V. If a sensor must be powered from 5 V and its TX rises above 3.3 V, add a proper unidirectional level shifter or resistor divider before D4. The default documented setup powers the sensor from 3V3.

#### Wiring

| A02YYUW | Nano logical pin | ESP32-S3 GPIO for the pinned Nano variant | Notes |
|---|---|---:|---|
| VCC | `3V3` | n/a | 3.3 V bench default |
| GND | `GND` | n/a | Common ground required |
| TX | `D4` / UART RX | GPIO7 | Sensor output to Nano input |
| RX | unconnected | n/a | Processed-output mode; insulate conductor |
| Nano UART TX | `D5` | GPIO8 | Assigned in `begin` but not physically connected |

Use symbolic `D4` and `D5` in source. Do not pass integer `4` or `5`, because Arduino pin-numbering modes can map raw integers differently. Start UART with `Serial1.begin(9600, SERIAL_8N1, D4, D5)`.

The sensor face must be horizontal, unobstructed, and at least 30 mm from the nearest possible surface. Record `tankDepthMm` as the distance from the sensor face to the effective empty reference, not the physical tank exterior. Reject installations deeper than 4500 mm, with a beam intersecting tank walls/hardware, or with condensation/foam behavior that cannot meet the 100 mm spread check.

Field deployment additionally requires an approved isolated power supply, strain relief, ingress-rated enclosure/cable glands, mounting drawing, corrosion/chemical compatibility, EMC/radio/regulatory review, service disconnect, and installation procedure. These are **OWNER-INPUT-REQUIRED**; the Nano/jumper bench is not production hardware.

### Pinned bench firmware build

The current `platformio.ini` is floating. Use this **PROPOSED-RECONSTRUCTION-DEFAULT** when a deterministic rebuild is more important than matching an unknown earlier platform resolution:


No external `lib_deps` are needed for the bench parser. The platform lock must capture all resolved package versions and SHA-256 values, including framework, toolchain, esptool, CMake/Ninja if resolved, and uploader packages. If `upload_protocol=esptool` differs from the resolved board manifest, the board manifest wins and this line must be corrected and recorded rather than guessed.

Check in this explicit 16 MiB A/B partition table as `firmware/partitions/nano-16m-ota.csv`:


This partition choice enables future A/B OTA but does not implement OTA. It changes the build artifact relative to any unknown current board-default partition and must therefore be labeled as a proposed baseline.

### A02YYUW UART protocol and parser state machine

Use this exact parser contract for the bench baseline:


Parser states:

1. `SeekHeader`: discard bytes until `0xFF`.
2. `ReadHigh`: read one byte; start/restart the 200 ms frame deadline.
3. `ReadLow`: read one byte before the same deadline.
4. `ReadChecksum`: read one byte before the same deadline.
5. Validate checksum and range.
6. On success, return distance and reset to `SeekHeader`.
7. On timeout/checksum/range failure, increment the corresponding diagnostic counter, reset to `SeekHeader`, and continue scanning. Do not treat a failed payload byte as a second header; the next unread byte starts resynchronization.

One loop prints exactly one ASCII line:


or:


Use `\n` or `println` consistently; the browser accepts LF/CRLF. Do not print boot banners, debug logs, or ANSI control sequences in the compatibility profile because they complicate the Web Serial fixture, even though nonmatching lines are ignored.

### Current bench state machine

This is the only end-to-end physical state machine described as implemented:


There is no implemented transition from `NanoBoot` to Wi-Fi, portal, activation, telemetry, queue, or OTA. Testing the factory/session APIs separately does not connect them to the Nano.

### Planned field-pilot factory contract

The following is a proposed secure and retry-safe design, not current behavior.

At the factory, an authenticated station must:

1. Read and normalize the board hardware ID as 12 uppercase hex characters.
2. Allocate a serial matching `^[A-Z0-9-]{4,64}$`.
3. Generate a bootstrap credential ID `wf_boot_` plus 20 lowercase Crockford Base32 characters.
4. Generate a 32-byte bootstrap secret with an operating-system CSPRNG.
5. Generate a 12-character setup passphrase from unambiguous uppercase letters and digits with at least 64 bits of entropy.
6. Write serial, hardware ID, bootstrap credential ID, bootstrap secret, setup passphrase, API base URL, configuration version, and manufacturing timestamp to encrypted NVS.
7. POST only `SHA256(bootstrapSecret)` to the existing factory registration endpoint.
8. Read back and verify NVS, boot firmware, execute UART/self-test, and mark the station job passed.
9. Print a label containing serial, hardware ID suffix, model, and a QR with `https://setup.waterflex.example/device/<serial>`. Do not encode bootstrap or operational secrets. Put the setup passphrase on an inside-enclosure/service label only.
10. Zero temporary plaintext buffers and prevent station logs, shell history, and printer spool retention from containing secrets.

Production factory identity must use a client certificate or workload identity scoped to device registration. The Development header key must remain unavailable outside Development. Certificate issuer, station inventory, rotation, revocation, and audit retention are **OWNER-INPUT-REQUIRED**; production registration remains disabled until supplied.

### Planned SoftAP and captive portal contract

The Nano bench has no setup button. The field-pilot hardware must add a normally-open setup/recovery input from `D2` to GND with `INPUT_PULLUP`, accessible without opening energized mains equipment. Holding it for 5 seconds requests portal mode; holding it for 15 seconds requests a factory reset after a visible confirmation pattern.

Use these exact proposed portal defaults:

| Item | Default |
|---|---|
| Radio | 2.4 GHz only; country code from factory configuration, `US` for the pilot |
| SSID | `WaterFlex-` plus final 6 serial characters |
| Security | WPA2-Personal with the factory setup passphrase; never an open AP |
| AP address | `192.168.4.1/24` |
| DHCP pool | `192.168.4.2` through `192.168.4.20` |
| DNS | Wildcard A response to `192.168.4.1` while portal is active |
| Portal URL | `http://192.168.4.1/` |
| Concurrent clients | 1 |
| Idle timeout | 10 minutes |
| Absolute portal timeout | 20 minutes; restart only by physical action or unprovisioned reboot |
| Candidate Wi-Fi connect timeout | 30 seconds |
| Wi-Fi password maximum | 63 bytes for WPA2 passphrase; never trim or log it |
| SSID maximum | 32 bytes; preserve case and spaces |

Portal HTTP routes:

| Method/path | Behavior |
|---|---|
| `GET /` | Serve embedded setup HTML with no third-party resources |
| `GET /api/v1/networks` | Return deduplicated 2.4 GHz SSIDs sorted by RSSI, with RSSI/security only |
| `POST /api/v1/configure` | Accept SSID, password, optional hidden-network flag, and page CSRF token |
| `GET /api/v1/status` | Return `idle`, `connecting`, `connected`, `activating`, `complete`, or a stable error code |
| `POST /api/v1/restart` | Restart only after successful configuration or explicit confirmation |
| Captive probe paths | `/generate_204`, `/hotspot-detect.html`, `/ncsi.txt`, `/connecttest.txt` redirect to `/` while incomplete |

Generate a 128-bit random portal session/CSRF token at AP start, embed it in the initial HTML, and require it on mutations. Send `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, and a CSP allowing only self. The portal uses HTTP because local captive portal TLS cannot have a publicly valid IP certificate; security comes from physical access, WPA2, one client, short lifetime, and no cloud/device secrets in portal responses.

Do not persist candidate Wi-Fi credentials as active until association, DHCP, DNS, SNTP, and a TLS-authenticated API health/activation attempt have succeeded. Keep the previous working credentials until the new candidate commits atomically. Support hidden WPA2 networks by manual entry. For the pilot, fail with an explicit unsupported message for WPA-Enterprise, browser-sign-in captive networks, 5-GHz-only networks, and SSIDs/passwords beyond the limits above.

### Planned bootstrap authentication and activation API

Use a distinct ASP.NET scheme named `BootstrapToken` and header:


Validation mirrors DeviceToken hashing/fixed-time comparison but requires a valid, unrevoked, unconsumed bootstrap credential and a device in `Commissioning`. Update bootstrap `LastUsedAtUtc` on successful authentication. After five consecutive invalid-secret attempts for a known credential ID, impose a 15-minute credential-local lockout and audit only non-secret metadata. Reset the count on successful authentication.

Add this planned endpoint:


Request:


Firmware must generate and durably store `activationAttemptId`, operational credential ID, and the 32-byte operational secret before its first request. The API receives only the hash. This makes response-loss retry safe without storing or reissuing plaintext server-side.

Validation defaults:

- Schema exactly 1.
- Attempt ID nonempty UUID.
- Serial/hardware must match the authenticated bootstrap device after normalization.
- Firmware/configuration nonblank and <=64 characters.
- Operational credential ID matches `^wf_dev_[a-z0-9]{20}$` and is <=64 characters.
- Operational hash is Base64 for exactly 32 bytes.
- Exactly one unexpired dealer-owned `PendingSensor` session must exist for the device.

On first valid request, one serializable transaction must:

1. Lock/revalidate bootstrap, device, session, tank reservation, and absence of active installation.
2. Create an active-dated installation and calibration version 1 from session tank depth. The commissioning distance remains null conceptually until first telemetry; because the current column is non-null, the field-pilot migration must make it nullable rather than invent a distance.
3. Create the operational credential as provisional, storing only the supplied hash, and assign `ProvisionalCredentialId`.
4. Set session status to `AwaitingFirstTelemetry`, set `ActivatedAtUtc`, and replace expiry with `now + 30 minutes`.
5. Keep Device status `Commissioning`.
6. Write a non-secret `device_activation_started` audit event.

Return `201 Created` on the first request:


An exact retry with the same attempt ID, operational credential ID, and hash returns `200 OK` with the same durable identifiers/state. A reused attempt ID with different immutable fields returns 409 `activation_attempt_mismatch`. No pending session returns 409 `no_pending_commissioning`; an occupied/released tank returns 409 `commissioning_conflict`; invalid bootstrap returns 401/403 using the same disclosure rules as operational auth.

The API never returns an operational plaintext secret. Firmware constructs `<operationalCredentialId>.<base64url(secret)>` locally.

### Planned first-telemetry completion and expiry

DeviceToken authentication must gain one narrow production exception: a provisional credential may authenticate telemetry while its device is `Commissioning` only when its linked session is unexpired `AwaitingFirstTelemetry`. It cannot access any other route.

In the same serializable transaction that commits the first accepted reading:

1. Confirm provisional credential/session/device/install/calibration linkage.
2. Store the first distance as `CommissioningDistanceMm` if still null.
3. Mark Device `Active` and set `CommissionedAtUtc`.
4. Mark session `Completed` and set `CompletedAtUtc`.
5. Retain `CommissioningSession.ProvisionalCredentialId` as historical linkage; the Completed session state makes that unchanged credential fully operational.
6. Set bootstrap `ConsumedAtUtc` and revoke any other live bootstrap credential.
7. Write `commissioning_completed` audit.

A replay whose reading was already inserted during the same activation still completes the state idempotently. Do not require a second unique reading after a response loss.

If AwaitingFirstTelemetry expires, one transaction revokes the provisional credential, marks/removes the provisional installation using `RemovedAtUtc`, closes its calibration, resets an otherwise unchanged device to `Registered`, marks the session `Expired`, releases the tank, and writes an audit. It does not consume bootstrap, so a technician may create a new session. Cancellation follows the same cleanup but is allowed only before first telemetry completes.

Canonical session statuses for the field-pilot migration are:


Persist enum strings with this exact PascalCase in SQL and camel-case them in JSON.

### Planned production firmware state machine


Persistent state changes use two NVS slots with generation, length, schema version, and CRC32; write inactive slot, verify, then atomically switch the active marker. Never overwrite the last known-good Wi-Fi/identity record in place.

Required NVS keys/data, with secrets stored only in an encrypted NVS partition:


Do not persist customer/account/tank ownership on the device.

### Planned sampling, queue, and telemetry defaults

Use these exact field-pilot defaults unless field trials replace them through a versioned configuration:

| Behavior | Default |
|---|---|
| First report | Immediately after activation/connectivity |
| Normal report interval | Server `nextReportIntervalSeconds`, clamped to 900-86400; default 3600 |
| Sensor sample | Five checksum/range-valid UART frames within 12 seconds |
| Reported distance | Median of five valid frames |
| Stability | `max - min <=100` mm; otherwise no level reading and set `sensor_unstable` diagnostic |
| Quality | `100 - min(100, spreadMm)` for a valid five-frame sample |
| Boot ID | UUID v4 generated once per boot from hardware RNG |
| Sequence | Unsigned monotonic 64-bit value starting at 0 for each boot; persist before enqueue |
| Timestamp | UTC after SNTP; null when trustworthy time is unavailable |
| Queue | CRC-protected LittleFS ring, 168 readings minimum (7 days hourly) |
| Batch | Oldest first, maximum 50 readings and maximum 60 KiB serialized body |
| Acknowledgement | Remove only entries individually acknowledged accepted/duplicate with matching boot/sequence |
| Queue full | Drop oldest acknowledged candidate first; if none, drop oldest and increment persistent `queue_overflow` counter |

Retry delay sequence is 5, 15, 30, 60, 120, 300, 600, then 900 seconds, each with 0-20% positive hardware-random jitter. Reset after a successful authenticated response. Honor a valid `Retry-After` up to 3600 seconds. Status handling:

| Result | Firmware action |
|---|---|
| 200 | Apply interval, remove acknowledged rows, persist acknowledgement |
| 400 | Keep a bounded local diagnostic, drop only the invalid payload/readings identified, do not hot-loop |
| 401 | Stop using credential; attempt bootstrap recovery only if unconsumed bootstrap remains, otherwise require service |
| 403 | Enter Recovery and require service; do not erase evidence/secrets automatically |
| 409 | Keep queue, refetch activation state if provisional, otherwise back off and alert locally |
| 413 | Halve batch size down to 1; a single-row 413 is a firmware defect |
| 429 | Honor Retry-After and keep queue |
| 5xx/network | Keep queue and use exponential schedule |

Watchdog timeout is 30 seconds. Sensor, portal, network, filesystem, and upload work must yield; no operation may block the main task indefinitely. Persist crash reason and counters without secrets.

### Planned Wi-Fi, DNS, time, and TLS defaults

- Support 2.4 GHz WPA2-Personal for the pilot. Treat WPA3 transition mode as supported only after hardware-in-loop validation.
- DHCP is required. Static IP, proxy, WPA-Enterprise, and customer captive login are unsupported in pilot v1.
- Use DNS from DHCP; retry one alternate resolver only if organization policy supplies it.
- Synchronize from three configured SNTP hostnames. Require year >=2025 before normal TLS validation.
- Production API placeholder is `https://sensor-api.waterflex.example`; `.example` deliberately cannot be deployed. **OWNER-INPUT-REQUIRED** must replace it with a WaterFlex-owned DNS name before release.
- Require TLS 1.2 or newer, full hostname validation, and a minimal versioned public-root CA bundle. Never use an insecure client, trust-all callback, or `TrustServerCertificate` equivalent.
- Prefer CA trust over leaf certificate pinning so certificates can rotate. If policy requires pinning, pin at least two SPKI values with an overlap process.
- Set connect timeout 10 seconds, TLS/HTTP response timeout 30 seconds, and maximum response body 16 KiB for activation/telemetry responses.
- Send no Wi-Fi credentials, bootstrap secret, operational secret, token, or customer ownership in logs or telemetry.
- Store secrets in encrypted NVS and zero transient buffers where the SDK permits. Disable core dumps containing RAM secrets in production.

Secure Boot v2, flash encryption release mode, encrypted NVS key partition, anti-rollback eFuses, firmware signing keys, and OTA signer custody are **PLANNED-PRODUCTION** and irreversible. They must remain disabled on reusable Nano development boards. Production enablement requires a documented key ceremony, offline root, controlled signer, recovery inventory, and destructive-device qualification; there is no safe invented key/default.

### Planned OTA defaults

- Use signed HTTPS manifests and signed images over the same validated TLS policy.
- Use the A/B `app0`/`app1` partitions above.
- Download only when external power is stable and at least 25% filesystem headroom remains.
- Verify size, SHA-256, signature, board ID, minimum secure version, and configuration compatibility before setting the boot partition.
- Mark the new image pending. Confirm only after 60 seconds of uptime, sensor UART self-test, filesystem mount, Wi-Fi, and one authenticated API exchange.
- Roll back automatically after three failed boots or missing confirmation.
- Roll out by cohorts 1%, 10%, 25%, 50%, 100% with an owner-defined observation gate.
- Never deliver signing private keys, bootstrap secrets, or Wi-Fi credentials in an OTA payload.

The signature algorithm/key identifiers and rollout service are **OWNER-INPUT-REQUIRED**. Until supplied, OTA remains disabled and firmware updates use a controlled USB service procedure.

### Missing test packages and exact proposed additions

These packages are not current dependencies. Add them only to the reconstruction baseline that introduces frontend automation, with exact versions locked by npm:


Add scripts:


If those package versions are unavailable from the approved registry, do not float to latest. Record the reviewed replacement and regenerate the lock as a deliberate baseline revision.

Minimum frontend fixtures/tests:

1. All five route/redirect/deep-link behaviors.
2. Development user load, stored valid identity, repaired invalid identity, and fetch failure.
3. Every fleet query parameter/default/page reset/back-forward case.
4. Fleet loading, retained refresh, empty, 400, 401, 403, 404, 500, and offline states.
5. Detail ranges, no readings, max-50 newest-first rendering, and nullable diagnostics.
6. Provisioning invalidation rules for every upstream field.
7. Web Serial CRLF/chunk boundaries, ignored noise, invalid range, checksum-error line noise, cancel, timeout, and spread 100/101 mm boundaries.
8. One-time token never written to browser storage or URL and cleared on restart/unmount.
9. Keyboard-only Radix Select, step rail, focus restoration, and screen-reader names.
10. Screenshot and overflow checks at the three specified viewports.

Firmware tests use PlatformIO's Unity support without an application library dependency. Split the UART parser, sample reducer, queue codec, retry policy, state transition reducer, and portal validation into host-testable units. Required boundary fixtures include:


The checksum examples follow `(0xFF + high + low) & 0xFF`; fixture bytes must be corrected if an arithmetic verification test detects a typo before they become golden data.

Hardware-in-loop fixture:

- One pinned Nano ESP32 and one A02YYUW or deterministic UART frame generator.
- Programmable power interruption.
- 2.4 GHz WPA2 test AP with controllable loss/DNS/TLS failure.
- Local TLS test endpoint with valid, expired, wrong-host, and untrusted chains.
- USB serial capture and current measurement.
- Tests for portal entry, wrong Wi-Fi, response loss during activation, first-telemetry replay, seven-day queue, 429, reboot at every persistent transition, OTA rollback, and secret-free logs.

No production firmware release passes on build-only evidence.

### Blank-machine startup and acceptance sequence

This sequence closes startup ambiguities for `bench-local`:

1. Install the frozen x64 tools and reboot/open a new shell so PATH changes apply.
2. Verify versions before restore. Abort on a mismatch in exact-build mode.
3. Start `MSSQLLocalDB`; confirm an Integrated Security query succeeds.
4. Restore the local EF tool and NuGet packages from the configured feed.
5. Restore web dependencies with `npm --prefix .\web ci`; never run root npm install.
6. Resolve the pinned PlatformIO environment with `pio pkg install --project-dir .\firmware` when firmware work is required.
7. Apply all four EF migrations using Infrastructure as both project and startup project.
8. Build backend, run all 35 .NET cases, run frontend typecheck/build if the proposed tsconfig was adopted, and build firmware.
9. Set `FactoryProvisioning__DevelopmentKey` in the same shell that starts the API.
10. Start API in Development on `127.0.0.1:5188`; wait until `/health` returns exactly `{"status":"ok"}`.
11. Start Vite on `127.0.0.1:3000`; verify `/`, `/fleet`, a deep-linked detail path, and `/provision` return the SPA through the intended server.
12. Expect an empty fleet immediately after migrations. Create data through an API workflow; there is no seed command.
13. Upload firmware, open PlatformIO monitor, verify at least five valid `distance=N mm` lines, then close the monitor.
14. Open Edge at `http://127.0.0.1:3000`, select a dealer technician, and perform Web Serial capture. A COM port cannot be shared.
15. Submit legacy commission and retain the returned token only for the telemetry smoke test.
16. POST one valid telemetry reading and replay it; verify accepted then duplicate and one SQL row.
17. Switch to Alex and verify fleet summary/list/detail/history.
18. Separately register a factory device and create/get/cancel a pending session; confirm no installation or operational credential is created.

Acceptance fails if any of these are observed:

- Root solution or root npm lock is used as authoritative.
- A package/platform resolves without a committed lock/hash in exact-build mode.
- Vite starts on a fallback port.
- API starts outside Development for local staff/factory workflows.
- Serial monitor remains open during browser capture.
- Factory/session flow is reported as completing activation.
- Current firmware is reported as posting telemetry.
- A token/secret appears in logs, URL, browser storage, screenshots, or test output.

### Production decisions and fail-closed defaults

The following values cannot be recovered and should not be invented in deployable code. The exact behavior until an owner supplies them is shown in the last column.

| Missing owner fact | Required artifact | Fail-closed default |
|---|---|---|
| WaterFlex cloud account/subscription, region, naming, budgets | Approved architecture/IaC repository | No Production deployment |
| Real SQL SKU, identity, firewall, backup, RPO/RTO | Data platform design and restore test | Production startup has no database credential |
| Entra tenant/app registrations/claims/dealer mapping | Identity design, app IDs, redirect URIs, policies | Staff/dealer/factory routes remain unmapped in Production |
| WaterFlex customer API URL/schema/auth/SLA | Versioned adapter contract and sandbox | Fixture directory throws/feature is disabled outside Development |
| RouteFlex API/product/quantity/debounce/cooldown | Versioned gateway contract and policy approval | Ticket gateway is not registered; no automatic deliveries |
| Public sensor DNS and certificate authority | DNS/certificate runbook | `.example` endpoint blocks release |
| Factory station PKI and key custody | Machine identity and secret-injection runbook | Production factory registration disabled |
| Firmware signing/secure-boot/flash-encryption keys | Key ceremony, HSM/offline root, recovery plan | Irreversible features and OTA disabled |
| Approved power/enclosure/mount and certifications | Released BOM/drawings/qualification report | Bench hardware prohibited from unattended field use |
| RF country/customer Wi-Fi support policy | Pilot support matrix | `US`, WPA2-Personal, 2.4 GHz pilot only; no broad compatibility claim |
| Telemetry retention/privacy/residency | Data classification and retention policy | Do not launch production data collection |
| Fleet size/load/SLOs | Capacity model and load test | Single-instance pilot only; no scaling claim |
| Monitoring/on-call/escalation | Dashboards, alerts, runbook, roster | No production readiness approval |
| Firmware rollout cohorts/rollback authority | Release policy and device inventory | USB service updates only |

An opinionated pilot infrastructure can be proposed separately, but selecting Azure/AWS SKUs, regions, domains, tenant IDs, retention, or keys in this recreation guide would create fictional organizational facts. The correct exact default is disabled behavior plus a startup/build gate.

### Definition of exact recreation complete

The guide can claim exact recreation only when all of the following are true:

1. The complete source/archive manifest verifies with no missing or extra authoritative files.
2. OS/tool/runtime/package/platform/browser versions and hashes are frozen and reproducibly installed.
3. NuGet, npm, and PlatformIO restore in locked/offline-verifiable mode.
4. The four migration assemblies produce the expected model and migration-history IDs on a blank database.
5. All 35 current .NET cases pass with the documented LocalDB fixture.
6. Frontend typecheck/build and the added route/component/browser tests pass.
7. Reference DOM/accessibility snapshots and three-viewport screenshots match the approved baseline.
8. Bench firmware binary hash is recorded, UART fixtures pass, and physical output matches the serial contract.
9. The legacy bench workflow and separate factory PendingSensor workflow both pass without being represented as one workflow.
10. Every production-only route/state remains absent or disabled unless its implementation, tests, owner inputs, and security gates are complete.

Until item 1 exists, the honest deliverable is a behaviorally compatible reimplementation, not an exact recreation.

---

## 29. Production Deployment Closure Specification

> **Status and precedence:** This entire section is a proposed `field-pilot` reference architecture and completion contract. None of it is implemented or provisioned by the repository described above. Section 28 remains authoritative for exact/bench reconstruction and fail-closed behavior. If WaterFlex formally adopts this section, Section 29 deliberately supersedes only Section 28's conflicting **proposed** field-pilot defaults; it never reclassifies planned behavior as implemented. It converts the open decisions in Sections 17-20, 22, 24, 26, and 27 into one explicit default. Every value prefixed `REPLACE_WATERFLEX_` is an external WaterFlex decision that must be replaced and recorded before Production approval. A replacement must preserve or improve the stated security and availability property.

### 29.1 Release profiles and hard blockers

The current repository has no viable field-production profile. A telemetry-only deployment can expose `/health` and `/api/v1/device/telemetry`, but there is no production mechanism to create active field credentials and the firmware cannot call it. It is useful only for a controlled infrastructure smoke test with a pre-created test device.

The following gates are release blockers, not backlog suggestions:

| Gate | Required closure | Acceptance evidence |
|---|---|---|
| `PROD-GATE-001` | Map operations, technician, and factory endpoints outside Development and protect each with production authentication/authorization. Development headers must be rejected in Production. | Integration tests obtain production-style JWTs, prove each allowed role, prove cross-dealer denial, and prove `X-WaterFlex-Development-User` and `X-WaterFlex-Factory-Key` cannot grant access. |
| `PROD-GATE-002` | Implement bootstrap authentication, retry-safe activation, provisional credential handling, first-telemetry completion, bootstrap consumption, expiry cleanup, and recovery. | Power-loss/replay test repeats every activation step and creates exactly one installation, calibration, operational credential, and completed session. |
| `PROD-GATE-003` | Implement firmware Wi-Fi setup, public TLS validation, activation, encrypted durable credential/telemetry storage, batching, retry/backoff, clock acquisition, and OTA rollback. | A physical device completes commissioning, survives power/network loss, reports over public HTTPS, and rolls back a deliberately bad image. |
| `PROD-GATE-004` | Replace fixture WaterFlex identities and customer directory with production adapters and a documented dealer-ownership source. | Staging contract tests against a non-production WaterFlex tenant prove account/location/tank lookup and dealer isolation. |
| `PROD-GATE-005` | Add reproducible OCI images and Infrastructure as Code for every resource below. No console-created production resource is accepted. | A clean subscription deployment from the pinned release revision reaches green smoke checks without manual resource edits. |
| `PROD-GATE-006` | Add readiness checks, structured/redacted telemetry, alerting, backup policy, restore proof, release manifest, and rollback runbooks. | Operations owner signs the go-live evidence bundle defined in Section 29.21. |
| `PROD-GATE-007` | Fix explicit-null requests that can become 500 responses and add database constraints for state/range invariants and tank asset uniqueness, or document and test an equivalent invariant. | Negative API tests return 400, and a migration test proves invalid/duplicate rows are rejected. |
| `PROD-GATE-008` | Move fleet filtering/sorting/paging into SQL and run the capacity test for the approved fleet ceiling. | At the default 1,000-device pilot load, operations list p95 is below 1 second and API memory does not grow with total history. |
| `PROD-GATE-009` | Either implement the outbox/Worker contract in Section 29.15 or omit the Worker and all delivery automation claims from the release. | Deployment inventory and UI wording contain no nonfunctional delivery component. |
| `PROD-GATE-010` | Add a production frontend auth flow and remove the identity selector, localhost Swagger link, one-time token display, and legacy Web Serial commissioning route from the Production bundle. | Production browser test contains none of those controls or strings and completes real sign-in/sign-out. |

### 29.2 Explicit reference architecture and assumptions

Use the following baseline unless an Architecture Decision Record replaces it:

- **Cloud:** Microsoft Azure commercial cloud.
- **Environments:** isolated `staging` and `production` resource groups and databases. Local Development remains unchanged.
- **Primary/secondary regions:** East US 2 (`eastus2`) and Central US (`centralus`).
- **Public edge:** Azure Front Door Premium with Web Application Firewall in Prevention mode and Private Link origins.
- **Compute:** Linux Azure App Service for Containers. API and web use separate Premium v3 plans and deployment slots. The web image is NGINX serving immutable Vite output. Do not deploy the current Worker.
- **Registry:** Azure Container Registry Premium with immutable release tags, geo-replication, retention, scanning, and signed images.
- **Database:** Azure SQL Database, General Purpose provisioned Gen5 2 vCore baseline, zone redundancy where available, 32 GiB initial maximum, private endpoints, Entra-only authentication, and an auto-failover group to Central US.
- **Identity:** Microsoft Entra ID authorization-code flow with PKCE for staff/dealer users; managed identities for Azure workloads; dual human and station identities for factory registration; existing custom per-device credentials for telemetry after the bootstrap flow is completed.
- **Secrets and signing:** Azure Key Vault Premium with RBAC, private endpoint, purge protection, and HSM-backed firmware/container signing keys. Scaled device secrets remain hash-only in SQL and plaintext only on the device.
- **Observability:** Azure Monitor, workspace-based Application Insights, Log Analytics, availability tests, Action Groups, and OpenTelemetry instrumentation.
- **Deployment:** GitHub Actions with OIDC federation to per-environment Azure deployment identities. Immutable artifacts are promoted by digest; Production never rebuilds a Staging artifact.
- **Availability target:** 99.9% monthly availability for authenticated telemetry ingestion during the pilot.
- **Recovery target:** RPO at most 5 minutes and RTO at most 60 minutes for a regional service failure, subject to a measured failover drill.
- **Capacity baseline:** at most 1,000 active devices, one scheduled reading per device per hour, batches of at most 24 after an outage, 20 telemetry requests/second sustained, and 100 requests/second for a 5-minute reconnect burst. Replace this after a signed fleet forecast and load test.
- **Data baseline:** telemetry remains online for 400 days; audit/security events remain online for 730 days; centralized application logs remain searchable for 90 days and archived for 365 days. Legal/privacy owners must replace or approve these periods.

Do not silently substitute a single VM, LocalDB, public SQL endpoint, shared fleet secret, manually uploaded ZIP, mutable `latest` image, or Development environment. Those substitutions break the reference security or recovery contract.

### 29.3 Mandatory WaterFlex replacement register

Create `docs/production-decisions.md` and resolve every row before provisioning Production. The value shown is the reference default when a safe default exists.

| Replacement token | Reference default | Required WaterFlex owner/evidence |
|---|---|---|
| `REPLACE_WATERFLEX_AZURE_TENANT_ID` | No safe default | Cloud identity owner supplies tenant GUID. |
| `REPLACE_WATERFLEX_AZURE_SUBSCRIPTION_ID` | No safe default | Cloud platform owner supplies dedicated Production subscription GUID. |
| `REPLACE_WATERFLEX_NAME_SUFFIX` | Lowercase 4-8 character organization-unique suffix | Cloud platform owner proves global Azure name availability. |
| `REPLACE_WATERFLEX_PRIMARY_REGION` | `eastus2` | Architecture owner confirms service/SKU availability and data residency. |
| `REPLACE_WATERFLEX_SECONDARY_REGION` | `centralus` | Architecture owner confirms paired recovery placement. |
| `REPLACE_WATERFLEX_DNS_ZONE` | `waterflex.com` | DNS owner proves write access to the authoritative zone. |
| `REPLACE_WATERFLEX_STAFF_HOST` | `saltmonitor.waterflex.com` | Product/security owners approve public staff hostname. |
| `REPLACE_WATERFLEX_DEVICE_HOST` | `sensor-api.saltmonitor.waterflex.com` | Firmware owner freezes hostname before factory production. |
| `REPLACE_WATERFLEX_FACTORY_HOST` | `factory-api.saltmonitor.waterflex.com` | Factory/security owners approve hostname and source networks. |
| `REPLACE_WATERFLEX_FIRMWARE_HOST` | `firmware.saltmonitor.waterflex.com` | Firmware/security owners approve the immutable OTA artifact hostname and disclosure policy. |
| `REPLACE_WATERFLEX_CORPORATE_EGRESS_CIDRS` | No safe default | Network owner supplies named-location and factory egress CIDRs, with ticket reference. |
| `REPLACE_WATERFLEX_SQL_ADMIN_GROUP_OBJECT_ID` | No safe default | Entra/PIM group for emergency SQL administration; no individual user. |
| `REPLACE_WATERFLEX_EMPLOYEE_GROUP_OBJECT_ID` | No safe default | Entra group assigned the employee app role. |
| `REPLACE_WATERFLEX_DEALER_GROUP_MAP` | One Entra group object ID mapped to each `Dealers.ExternalId` | Dealer operations owner exports and signs the complete mapping. |
| `REPLACE_WATERFLEX_FACTORY_OPERATOR_GROUP_OBJECT_ID` | No safe default | Factory security owner supplies group and Conditional Access policy ID. |
| `REPLACE_WATERFLEX_DIRECTORY_BASE_URL` | `https://directory-api.nonprod.waterflex.com` in Staging | Integration owner supplies production URL, OpenAPI contract, scope, timeout, and support SLA. |
| `REPLACE_WATERFLEX_DIRECTORY_SCOPE` | `api://waterflex-directory/.default` | Entra owner supplies resource application ID URI. |
| `REPLACE_WATERFLEX_ROUTEFLEX_BASE_URL` | `https://routeflex-api.nonprod.waterflex.com` in Staging | Delivery integration owner supplies production URL and idempotency/SLA contract. |
| `REPLACE_WATERFLEX_ROUTEFLEX_SCOPE` | `api://routeflex/.default` | Entra owner supplies resource application ID URI. |
| `REPLACE_WATERFLEX_ONCALL_RECEIVER` | No safe default | Operations owner supplies monitored Action Group target and escalation test. |
| `REPLACE_WATERFLEX_SECURITY_RECEIVER` | No safe default | Security owner supplies 24x7 incident target. |
| `REPLACE_WATERFLEX_SUPPORT_CONTACT` | `support@waterflex.com` | Product owner confirms mailbox ownership and SLA. |
| `REPLACE_WATERFLEX_PILOT_FLEET_LIMIT` | `1000` | Product and SRE approve measured capacity ceiling. |
| `REPLACE_WATERFLEX_TELEMETRY_RETENTION_DAYS` | `400` | Privacy/legal/data owner approves retention and deletion behavior. |
| `REPLACE_WATERFLEX_RPO_MINUTES` | `5` | Business owner signs recovery requirement. |
| `REPLACE_WATERFLEX_RTO_MINUTES` | `60` | Business owner signs recovery requirement. |
| `REPLACE_WATERFLEX_COST_CENTER` | No safe default | FinOps owner supplies chargeback code and budget owner. |
| `REPLACE_WATERFLEX_REPOSITORY_URL` | No safe default | Engineering owner supplies the protected canonical repository URL. |
| `REPLACE_WATERFLEX_GITHUB_ORG` | No safe default | GitHub enterprise owner supplies the canonical organization slug. |
| `REPLACE_WATERFLEX_GITHUB_REPOSITORY` | No safe default | Engineering owner supplies the protected repository name under the approved organization. |
| `REPLACE_WATERFLEX_NPM_REGISTRY` | `https://registry.npmjs.org/` | Supply-chain owner approves any private mirror, scoped overrides, and short-lived authentication if required. |
| `REPLACE_WATERFLEX_NUGET_FEED` | Existing Microsoft `dotnet-public` feed if approved | Supply-chain owner approves feed/mirror, package-source mapping, and availability. |
| `REPLACE_WATERFLEX_PLATFORM_SECURITY_TEAM` | No safe default | GitHub organization owner supplies CODEOWNERS team slug. |
| `REPLACE_WATERFLEX_DELIVERY_POLICY` | Three low readings spanning 24 hours; 7-day cooldown | Product/delivery owner approves product, quantity, threshold, debounce, and cooldown. |
| `REPLACE_WATERFLEX_RELEASE_WINDOW` | Tuesday-Thursday, 14:00-18:00 UTC | Change/operations owner approves staffed release window. |
| `REPLACE_WATERFLEX_MANUFACTURER` | Approved contract manufacturer | Supply-chain/quality owner supplies legal entity, site, SLA, and contacts. |
| `REPLACE_WATERFLEX_HARDWARE_REVISION` | `WF-NANO-A02-REV-A` | Hardware/quality owner supplies released design and change record. |
| `REPLACE_WATERFLEX_DEVICE_MODEL` | `Arduino Nano ESP32` | Hardware/firmware owner freezes exact server/factory model value. |
| `REPLACE_WATERFLEX_HARDWARE_ID_SOURCE` | Wi-Fi station MAC | Hardware/security owner proves stable uniqueness and read-only derivation. |
| `REPLACE_WATERFLEX_SERIAL_FORMAT` | `WF-NANO-` plus eight digits and check digit | Factory owner supplies allocation authority and reserved ranges. |
| `REPLACE_WATERFLEX_SENSOR_TOLERANCE` | Greater of +/-20 mm or +/-2% | Hardware/quality owner approves measured production tolerance. |
| `REPLACE_WATERFLEX_PROVISIONING_GESTURE` | Recessed button held 8 seconds | Hardware/security owner approves physical behavior and enclosure support. |
| `REPLACE_WATERFLEX_FACTORY_EGRESS_CIDRS` | No safe default | Factory network owner supplies dedicated, monitored station egress CIDRs. |
| `REPLACE_WATERFLEX_FACTORY_PRINTER` | Approved 300 dpi thermal-transfer printer | Factory/quality owner supplies model, media, driver, calibration, and spares. |
| `REPLACE_WATERFLEX_FACTORY_RETENTION_YEARS` | `7` | Quality/privacy/legal owners approve genealogy retention. |
| `REPLACE_WATERFLEX_SECURE_BOOT_FEASIBILITY` | Secure Boot V2, flash encryption Release mode, encrypted NVS, signed A/B OTA | Hardware/security owners sign destructive qualification on exact hardware. |
| `REPLACE_WATERFLEX_WIFI_SUPPORT` | 2.4 GHz WPA2-Personal, DHCP, DNS, NTP, outbound 443 | Product/support owners approve tested customer matrix. |
| `REPLACE_WATERFLEX_NTP_ENDPOINTS` | Cloudflare, Google, and pool.ntp.org hostnames | Network/security owners approve availability, privacy, and customer firewall guidance. |
| `REPLACE_WATERFLEX_RECOVERY_SUBSCRIPTION_ID` | Dedicated recovery subscription | Cloud/security owners supply GUID, PIM groups, and billing owner. |
| `REPLACE_WATERFLEX_BUSINESS_OWNER` | No safe default | Accountable business service owner/team. |
| `REPLACE_WATERFLEX_PRODUCT_OWNER` | No safe default | Accountable product owner/team. |
| `REPLACE_WATERFLEX_ENGINEERING_OWNER` | No safe default | Accountable engineering owner/team. |
| `REPLACE_WATERFLEX_DATA_OWNER` | No safe default | Accountable data owner/team. |
| `REPLACE_WATERFLEX_PRIVACY_OWNER` | No safe default | Accountable privacy/legal owner/team. |
| `REPLACE_WATERFLEX_FACTORY_OWNER` | No safe default | Accountable factory operations owner/team. |
| `REPLACE_WATERFLEX_FIRMWARE_OWNER` | No safe default | Accountable firmware owner/team. |
| `REPLACE_WATERFLEX_DATABASE_OWNER` | No safe default | Accountable database platform owner/team. |
| `REPLACE_WATERFLEX_NETWORK_OWNER` | No safe default | Accountable DNS/network/edge owner/team. |
| `REPLACE_WATERFLEX_IDENTITY_OWNER` | No safe default | Accountable Entra/authorization owner/team. |
| `REPLACE_WATERFLEX_VENDOR_MANAGER` | No safe default | Accountable vendor/SLA owner/team. |
| `REPLACE_WATERFLEX_STATUS_PAGE` | Externally hosted status page | Communications/operations owner supplies URL and publishing authority. |
| `REPLACE_WATERFLEX_SPARE_POLICY` | Greater of 5% of fleet or 20 complete units | Factory/field support owners approve stocking and replenishment. |

Any unresolved row is a failed Production readiness review. Do not encode replacement values in source when a parameter, app setting, role assignment, or secret reference is appropriate.

Maintain the resolutions in `docs/production-decisions.yaml`, validated by `eng/production-decisions.schema.json` and `scripts/release/Test-ProductionDecisions.ps1`. Each decision contains `status: resolved`, non-secret `value`, durable owner group, evidence/change URI, approvers, and approval UTC. The validator extracts every exact `REPLACE_WATERFLEX_[A-Z0-9_]+` token from this guide and fails when the registry lacks a resolved entry. Secret values are vault references, never literals.

Before a release, separately scan deployable paths (`.github`, `infra`, `backend`, `web`, `firmware`, `eng`, and `scripts`) and fail on `REPLACE_WATERFLEX_`, `<approved-...>`, `<suffix>`, example tenant/client IDs, `.example` hosts, or a manifest null for a required artifact. The guide and decision-registry keys are templates and remain present; the deployable configuration generated from them must contain no placeholder.

### 29.4 Resource inventory and naming

Use lowercase hyphenated names except where Azure naming rules prohibit hyphens. Replace `<suffix>` once with `REPLACE_WATERFLEX_NAME_SUFFIX`; never put an environment-neutral resource in a Production resource group.

| Resource | Production name | Required properties |
|---|---|---|
| Resource groups | `rg-wfsm-shared-eus2`, `rg-wfsm-prod-eus2`, `rg-wfsm-prod-cus` | Azure Policy assignments and delete locks on stateful resources. |
| Front Door profile/endpoint | `afd-wfsm-prod` / `afd-wfsm-prod-<suffix>` | Premium, managed identity, access logs, three custom domains. |
| WAF policy | `waf-wfsm-prod` | Managed Default Rule Set and Bot Manager in Prevention; path-specific exclusions only by reviewed rule ID. |
| API App Service plan/app | `asp-wfsm-api-prod-eus2` / `app-wfsm-api-prod-eus2` | Linux P1v3, zone redundant, minimum 3 instances, `staging` slot, HTTPS only, public network disabled. |
| Web App Service plan/app | `asp-wfsm-web-prod-eus2` / `app-wfsm-web-prod-eus2` | Linux P0v3 or larger, minimum 2 instances, `staging` slot, HTTPS only, public network disabled. |
| Secondary API/web | `app-wfsm-api-prod-cus`, `app-wfsm-web-prod-cus` on `asp-wfsm-prod-cus` | Warm standby, minimum 2 API and 1 web instances, same digests/config schema, private origins, continuous synthetic checks. |
| Container registry | `acr<suffix>wfsmprod` | Premium, admin user disabled, private endpoint, geo-replica in Central US, release-tag write protection. |
| Virtual network | `vnet-wfsm-prod-eus2` | `10.42.0.0/16`; no overlapping corporate range. |
| Secondary virtual network | `vnet-wfsm-prod-cus` | `10.43.0.0/16`; equivalent integration/private-endpoint subnets and private DNS links. |
| Integration subnet | `snet-wfsm-app-outbound` | `10.42.1.0/24`, delegated to App Service, route table and NSG attached. |
| Private endpoint subnet | `snet-wfsm-private-endpoints` | `10.42.2.0/24`, private endpoint network policies configured as required. |
| Build runner subnet | `snet-wfsm-deploy` | `10.42.3.0/24`, only migration/deployment runner access; no general user workloads. |
| SQL servers/database | `sql-wfsm-prod-<suffix>-eus2`, `sql-wfsm-prod-<suffix>-cus`, `sqldb-wfsm-prod` | Entra-only, public access disabled, private endpoints, auditing, Defender, failover group `fog-wfsm-prod`. |
| Key Vault | `kv-wfsm-prod-<suffix>` | Premium, Azure RBAC, 90-day soft delete, purge protection, private endpoint, diagnostic logs. |
| App identities | `id-wfsm-api-prod`, `id-wfsm-web-prod`, `id-wfsm-migrate-prod` | User-assigned managed identities; no shared identity between runtime and migration. |
| Log Analytics/App Insights | `log-wfsm-prod-eus2`, `appi-wfsm-prod-eus2` | 90-day interactive retention, daily cap alert but no silent ingestion stop. |
| Action Group | `ag-wfsm-prod-critical` | Operations and security receivers, common alert schema enabled. |
| Release storage | `st<suffix>wfsmprod` | Private Blob, versioning, soft delete, immutable release-manifest container, lifecycle archive. |
| Firmware Blob origins | `firmware-manifests`, `firmware-images` in `st<suffix>wfsmprod` | Private containers, versioning/immutability, Front Door read-only origin, no listing or direct public access. |
| Maintenance jobs | `cae-wfsm-jobs-prod-eus2` plus `caj-wfsm-*-prod` | Private Azure Container Apps Jobs environment, signed images, dedicated identities, no ingress. |

Create equivalent names with `stg` for Staging. Staging may use one compute instance and no geo-secondary, but must preserve identity, TLS, private SQL, migration, and observability behavior. Production data must never be copied to Staging unless anonymized under an approved process.

Every resource must carry these tags:


### 29.5 Infrastructure as Code bootstrap

Add the following authoritative files; generated ARM JSON is not committed:


`main.bicep` must require these non-secret parameters:


It must output `frontDoorEndpointHostName`, `apiAppName`, `webAppName`, `registryLoginServer`, `sqlFailoverGroupFqdn`, `databaseName`, `keyVaultUri`, `applicationInsightsConnectionStringSecretUri`, and managed identity client/principal IDs. Outputs must contain no credentials.

Bootstrap and deploy from an authorized workstation or OIDC job:


The pipeline must archive the What-If result and require approval for deletes, public network exposure, role-owner grants, region changes, or reduced retention. Apply deny policies for public SQL/Key Vault/ACR access, missing diagnostic settings, non-TLS ingress, unapproved regions/SKUs, and missing required tags. Assign resource locks to Production SQL, Key Vault, Front Door, DNS records, and release storage after initial creation.

**Infrastructure acceptance:** destroy and recreate Staging from the same revision, compare `az deployment sub what-if` to no-change, and verify there are no manual resources carrying `application=waterflex-salt-monitor` without `managedBy=bicep`.

### 29.6 Hosting and runtime contract

API slot settings, with secrets supplied by Key Vault references or managed identity rather than literal pipeline values:


Replace the literal hostnames above if the replacement register changes them. Mark every environment-specific setting as a deployment-slot setting so a slot swap cannot import Staging identity, SQL, hostnames, or telemetry configuration into Production.

Add these endpoints:

| Endpoint | Purpose | Dependency behavior |
|---|---|---|
| `GET /health/live` | Process liveness | Never calls SQL or external services; 200 if process can serve. |
| `GET /health/ready` | Traffic readiness | Calls SQL with a 3-second timeout and verifies migrations are current; 200 only when ready, otherwise 503. Do not make optional WaterFlex/RouteFlex outages fail telemetry readiness. |
| `GET /version` | Release diagnosis | Returns version, Git SHA, build UTC, schema compatibility range, and firmware compatibility range; no secrets. Restrict detailed output to staff host if policy requires. |

Keep `/health` as a compatibility alias to `/health/live` for the first release, then document its retirement. App Service health checks use `/health/ready`; container health checks use `/health/live`. Enable Always On, graceful shutdown of at least 30 seconds, HTTP/2 at the edge, and autoscale from 3 to 10 API instances on 60% average CPU, p95 request latency above 750 ms, or request queue growth. Scale-in cooldown is at least 10 minutes.

The in-process device limiter is defense in depth only. Replace it with a replica-consistent limiter keyed by credential ID, using Azure Cache for Redis only if measured load requires it; until then, enforce the 1,000-device ceiling and an edge rate limit that cannot punish many devices behind one NAT. Commissioning idempotency must rely on SQL uniqueness/transactions, never sticky sessions.

**Hosting acceptance:** stop one API instance during a 20 request/second telemetry test. There must be no failed accepted reading, duplicate database row, or client-visible outage beyond retry. Restart all instances simultaneously and prove readiness stays 503 until SQL is usable.

### 29.7 DNS and TLS contract

Create these public records in `REPLACE_WATERFLEX_DNS_ZONE`, initially with TTL 300 seconds and later 3,600 seconds after a stable release:

| Name | Type | Target/purpose |
|---|---|---|
| `saltmonitor` | CNAME | Front Door endpoint; staff SPA and same-origin staff API. |
| `sensor-api.saltmonitor` | CNAME | Same Front Door endpoint; firmware device API only. |
| `factory-api.saltmonitor` | CNAME | Same Front Door endpoint; factory API only. |
| `firmware.saltmonitor` | CNAME | Same Front Door endpoint; signed immutable OTA manifests and binaries only. |
| `_dnsauth.saltmonitor` and provider-required validation names | TXT | Front Door custom-domain ownership validation. |

Add CAA only after confirming the current Azure Front Door managed-certificate issuer. The reference default permits DigiCert: `0 issue "digicert.com"`. A mismatched CAA record can prevent renewal and is a release blocker.

Front Door must issue and automatically renew managed certificates for all four names. Set the minimum TLS version to 1.2, prefer TLS 1.3 where the service/client supports it, redirect HTTP to HTTPS with 308, and reject invalid host headers. Do not pin an edge leaf certificate in firmware. Firmware carries an updateable bundle with the active and next approved public root CA and validates hostname, chain, validity, and time.

After a 14-day HTTPS-only soak, return:


Do not add `preload` until the WaterFlex DNS owner has verified every affected subdomain can remain HTTPS-only for the preload lifetime.

DNS/TLS acceptance from a network outside WaterFlex:


Expected: both health calls return 200 with a publicly trusted chain and matching hostname; HTTP returns one 308 to the identical HTTPS path; no redirect points to an Azure default hostname. Monitor certificate expiry and failed renewal, alerting at 45, 30, 14, and 7 days.

### 29.8 Network, edge routing, and reverse-proxy policy

Use this path/host allowlist. A route absent from this table returns 404 at the edge before reaching ASP.NET:

| Host | Paths sent to API origin | Allowed methods | Default origin |
|---|---|---|---|
| `saltmonitor.waterflex.com` | `/api/v1/ops/*`, `/api/v1/technician/*`, `/health/*`, `/version` | `GET`, `HEAD`, `OPTIONS`, and endpoint-required `POST` | Web origin with SPA fallback, including `/auth/callback`. |
| `sensor-api.saltmonitor.waterflex.com` | `/api/v1/device/telemetry`, `/api/v1/device/activate`, `/api/v1/device/credentials/rotate`, `/api/v1/device/firmware`, `/health/live` | `GET`, `HEAD`, `POST` | Reject 404. |
| `factory-api.saltmonitor.waterflex.com` | `/api/v1/factory/*`, `/health/live` | `GET`, `HEAD`, `POST` | Reject 404. |
| `firmware.saltmonitor.waterflex.com` | `/manifests/*`, `/images/*` | `GET`, `HEAD` only | Reject 404; immutable release Blob origin. |

Front Door is the only public origin caller. Use Premium Private Link to both App Services, disable each app's public network access, and remove Azure default hostnames from user-facing links. SQL, Key Vault, ACR, and release storage use private endpoints and public access disabled. Link these private DNS zones to `vnet-wfsm-prod-eus2`:


API outbound traffic goes through VNet integration. Permit only DNS/NTP/platform dependencies, Azure SQL 1433, Key Vault 443, Application Insights ingestion 443, Entra token endpoints 443, and approved WaterFlex/RouteFlex endpoints 443. Deny and log other Internet egress. Replace endpoint FQDNs and private connectivity after receiving the real integration contracts.

Implement forwarded-header handling before HTTPS redirection and authentication. Trust only the App Service/Front Door hop, process exactly one forwarded value, and reject malformed chains. Add these application settings for the new strongly typed options:


Do not use an unrestricted `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. Log the socket peer and final client address separately, without treating a caller-supplied forwarded header as authoritative.

WAF policy `waf-wfsm-prod` must include:

- Microsoft managed rules in Prevention mode for staff and factory paths.
- A reviewed device-path exclusion only for a specific false-positive rule, never a blanket WAF bypass.
- `FactoryNamedLocationsOnly`: priority 10, allow `factory-api` requests only from `REPLACE_WATERFLEX_FACTORY_EGRESS_CIDRS`; priority 20 blocks all other factory-host traffic.
- `FactoryRateLimit`: 60 requests per 5 minutes per source IP, with factory tests proving this accommodates station NAT.
- `StaffRateLimit`: 600 API requests per 5 minutes per source IP.
- `DeviceEdgeAbuseLimit`: 6,000 requests per 5 minutes per source IP so a customer-carrier NAT does not override per-device application limits.
- Maximum accepted request body at the edge at least 64 KiB and at most 128 KiB; Kestrel remains authoritative at 64 KiB.
- Access logging with `Authorization`, cookies, factory station token, query values, and bodies suppressed or redacted.

Same-origin staff hosting is the default, so do not enable CORS. If WaterFlex replaces it with a separate origin, add only that exact HTTPS origin, required methods/headers, no wildcard, and no credentials unless cookie authentication is selected and CSRF protection is implemented.

**Network acceptance:** public probes cannot connect to SQL, Key Vault, ACR, App Service default origins, or release Blob endpoints. A request to an API path on the wrong hostname returns 404. A spoofed `X-Forwarded-For` does not alter authorization, audit identity, or rate-limit key. A factory request from outside named locations is blocked and appears in WAF logs.

### 29.9 Static web hosting contract

Build the SPA once, copy only `web/dist` into an unprivileged NGINX image, and add `web/nginx.conf` with this behavior:


Front Door, not NGINX, routes `/api` to the API origin. A static response must never contain a database string, tenant secret, factory credential, or operational device token. Public frontend values are build-time configuration and must use these exact names:


Create `dist/version.json` containing only release version, Git SHA, and build UTC. It and `index.html` use no-cache; hashed assets are immutable. Do not put a secret in any `VITE_*` value.

Set these response headers at Front Door so error and API responses receive them too:


Keep `usb=(self)` only while an explicitly non-Production bench route needs Web Serial; the Production frontend should set `usb=()` after `PROD-GATE-010` removes that route. Do not add `unsafe-inline` to CSP to repair a build; change the bundle or use nonces/hashes.

**Static-host acceptance:** direct requests to `/fleet/<valid-guid>`, `/provision`, and an unknown client route return the SPA shell with HTTP 200, while missing `/assets/*` returns 404 rather than the shell. `index.html` changes immediately after deployment, hashed assets cache for one year, `/api` never returns `index.html`, and browser developer tools report zero CSP mixed-content or source violations during sign-in and normal fleet use.

### 29.10 SQL platform, permissions, retention, and recovery

Provision `sqldb-wfsm-prod` on the primary and secondary Azure SQL logical servers and place it in auto-failover group `fog-wfsm-prod`. Applications always connect to `fog-wfsm-prod.database.windows.net`, never a regional server name. Bicep must set:


`P7Y` is the reference recovery/legal-hold default, not an inferred WaterFlex requirement. Privacy, legal, and records owners must approve it because deleted customer data can remain in backups. If they reduce it, record the replacement alongside `REPLACE_WATERFLEX_TELEMETRY_RETENTION_DAYS`.

Enable Query Store in read/write mode with 30 days retention, automatic plan correction (`FORCE_LAST_GOOD_PLAN=ON`), Microsoft Defender for SQL, vulnerability assessment, failed/successful authentication auditing, and SQL audit export to both Log Analytics and a private immutable Blob container `sql-audit`. Alert at 70%, 85%, and 95% database/data-log utilization and on blocked-process duration above 30 seconds.

Create only Entra-contained database users. The Entra server administrator is the PIM-controlled group `REPLACE_WATERFLEX_SQL_ADMIN_GROUP_OBJECT_ID`; disable SQL authentication after bootstrap. Under an activated administrator session, create these roles and users:


If Entra display names are not unique, use Azure SQL's service-principal object-ID syntax supported by the selected server version and record the principal IDs in the deployment output. Do not grant the API `db_owner`, `db_ddladmin`, `CONTROL DATABASE`, `ALTER ANY USER`, or permission to delete migration history. The migration identity has no application runtime assignment and is enabled only for the one-shot migration job.

Add a least-privilege database role `wfsm_worker_runtime` only if the Worker is implemented. Grant it table-specific outbox/ticket/maintenance permissions; do not copy `wfsm_api_runtime` automatically. No frontend, firmware, or factory workstation connects directly to SQL.

The production schema work must include migrations with these stable names, even though generated timestamp prefixes will vary:

1. `HardenProductionInvariants`: add unique filtered index `UX_Tanks_ServiceLocationId_WaterFlexAssetId` where `WaterFlexAssetId IS NOT NULL`; add named checks for telemetry ranges, positive calibration depth/version, credential validity windows, provisioning audit ownership, and valid lifecycle/date combinations.
2. `AddProductionAuthorization`: add `DealerIdentityGroups` with unique Entra group object ID, dealer FK, validity interval, approver, and rowversion; add authorization-change audit records. Never infer dealer ownership from email domain or a client field.
3. `CompleteBootstrapActivation`: add only the state/idempotency columns or indexes proven necessary by the activation design; retain one live session/device/tank and one operational credential per activation-attempt invariants.
4. `AddFleetOperationalIndexes`: add SQL-side fleet filter/sort/latest-reading indexes selected from an actual execution plan and a batched telemetry-retention index on `ReceivedAtUtc, Id`.
5. `AddDeliveryOutbox`: optional and deployed only with the completed Worker contract in Section 29.15.

Add a daily retention operation that deletes `TelemetryReadings` older than `TelemetryRetention__OnlineDays=400` in committed batches of 10,000, pauses between batches, records rows/duration/cutoff, and aborts before a five-minute runtime. It must preserve readings under a documented legal hold. Retention must run from a separately authorized maintenance identity; it must not execute inside an API request or migration.

Apply these exact runtime settings:


Verify configuration and connectivity:


**SQL acceptance:** a public-network connection times out; an API managed-identity connection succeeds with `encrypt_option=TRUE`; `CREATE TABLE` and `DELETE FROM dbo.__EFMigrationsHistory` fail as the API identity; both succeed as appropriate under the migration identity; all expected migration IDs are present once. Restore the latest point-in-time backup into an isolated recovery database, run schema and row-count checks, and record measured RPO/RTO before go-live. Section 29.20 defines the recurring drill.

### 29.11 Entra identity and authorization contract

Create separate Entra app registrations/service principals for Staging and Production. Manage their manifests through `infra/entra/` and `scripts/entra/Sync-EntraApplications.ps1`; export sanitized manifests as release evidence. Console-only app registration changes are forbidden.

| Registration | Production display name | Required configuration |
|---|---|---|
| API resource | `app-wfsm-api-prod` | Single-tenant; Application ID URI `api://<api-client-id>`; delegated scope `access_as_user`; app roles below; no client secret; access-token version 2. |
| Browser SPA | `app-wfsm-spa-prod` | Single-tenant public client; SPA redirect `https://saltmonitor.waterflex.com/auth/callback`; post-logout redirect `https://saltmonitor.waterflex.com/`; delegated permission `access_as_user`; no implicit grant and no secret. |
| Factory station | `app-wfsm-factory-station-prod` | Single-tenant confidential client; application permission/app role `SaltMonitor.FactoryStation`; certificate credential only; one service principal or certificate per physical station. |
| Synthetic monitor | `app-wfsm-synthetic-prod` | Single-tenant confidential client; application role `SaltMonitor.SyntheticMonitor`; certificate credential only; synthetic endpoints/data only. |
| Directory/RouteFlex workload | API user-assigned managed identity `id-wfsm-api-prod` | Federated/managed-identity access to the approved external scopes; no client secret. Replace with a certificate credential only if the external resource cannot accept managed identity. |

Define these exact app-role values on `app-wfsm-api-prod`:


Assign employee, dealer-technician, and factory-operator Entra groups to their corresponding roles. Dealer technicians are Entra B2B members by reference default. Configure the groups optional claim to emit only groups assigned to the application, then map each approved group object ID to one `Dealers.Id` through `DealerIdentityGroups`. Reject a token with no mapping, more than one active dealer mapping, group overage markers, the wrong tenant, or a disabled dealer. Do not call Microsoft Graph during an API request to repair ambiguous claims.

JWT validation must require:


Use `oid` plus `tid` as the stable human subject key. Display name and email are presentation attributes, never authorization keys. Record actor object ID, resolved dealer ID, station service-principal ID where relevant, request trace ID, action, target, outcome, and UTC time in immutable audit events without token contents.

Add these exact API options and fail startup when a required Production value is absent:


Define named ASP.NET authorization policies `EmployeeOnly`, `DealerTechnicianOnly`, `FactoryOperatorOnly`, `FactoryStationOnly`, `FactoryRegistration` (operator and station identities together), `SyntheticMonitorOnly`, and `DeviceTelemetry`. `/api/v1/factory/*` requires two independently validated tokens: the human operator token in `Authorization` and the station client-credentials token in `X-WaterFlex-Station-Authorization`. Both identities are written to the audit event. Never forward either header downstream or log it. `SyntheticMonitorOnly` can access only non-mutating synthetic verification endpoints and synthetic-owned records; it grants no employee/dealer/factory business permission.

The Production frontend uses MSAL authorization code with PKCE and keeps tokens in memory or `sessionStorage`, not `localStorage`. Request only `openid`, `profile`, and `api://<api-client-id>/access_as_user`. Sign-out clears local account state and calls the Entra end-session endpoint. Do not issue an application cookie unless a backend-for-frontend architecture and CSRF design explicitly replaces this SPA default.

Conditional Access baseline:

- Require MFA for every human role.
- Require compliant or hybrid-joined devices for WaterFlex employees and factory operators.
- Require phishing-resistant authentication for factory operators and privileged SQL/deployment groups.
- Block legacy authentication, anonymous devices for factory roles, high-risk sign-ins, and countries where WaterFlex has no approved operators.
- Exclude only two monitored emergency accounts; store their credentials outside normal operator access, test quarterly, and alert on every use.
- Restrict factory station service principals to certificate credentials and approved source named locations. Rotate station certificates every 90 days with a 14-day overlap.

**Identity acceptance:** automated tests prove issuer, audience, tenant, expiry, role, station, and dealer mapping failures independently. An employee cannot call dealer/factory routes; dealer A receives 404 for dealer B data; a factory operator without a station token and a station without an operator token both receive 401/403; Development headers have no effect. Disable a user, group mapping, dealer, and station certificate in turn and verify access is removed within the documented token/cache lifetime. No browser storage entry contains a development identity or long-lived token after sign-out.

### 29.12 Secrets, keys, certificates, and redaction

The default architecture intentionally eliminates deploy-time SQL passwords, ACR passwords, external API client secrets, and Azure service-principal secrets. Use GitHub OIDC for deployment, managed identities for workloads, Entra tokens for SQL/integrations, and certificate credentials for factory stations.

Create these exact Key Vault objects only when their owning feature exists:

| Object | Type | Owner/use | Rotation |
|---|---|---|---|
| `firmware-signing-prod` | HSM EC P-256 key | Offline-approved Production firmware signer; public key embedded in bootloader/trust metadata. | Annual version rollover with old/new verification overlap through the full fleet update. |
| `release-manifest-signing-prod` | HSM EC P-256 key | Signs the immutable release manifest and firmware factory manifest. | Annual, with verification-key history retained. |
| `factory-label-signing-prod` | HSM EC P-256 key | Signs QR/label payloads so station/app can detect alteration. | Annual, dual-key verification during transition. |
| `waterflex-directory-client-certificate` | Certificate, only if managed identity is rejected | API authentication to the real WaterFlex directory. | 60 days, automated at 30 days remaining. |
| `routeflex-client-certificate` | Certificate, only if managed identity is rejected | Worker authentication to RouteFlex. | 60 days, automated at 30 days remaining. |
| `emergency-device-recovery-secret` | Secret, only after an approved recovery design | Break-glass device recovery; never a fleet-wide normal credential. | 30 days and immediately after any use. |

Do not store operational device or bootstrap plaintext secrets in Key Vault, SQL, labels, release artifacts, CI logs, browser storage, or support tickets. The factory station generates each 32-byte bootstrap secret with an OS CSPRNG, writes it once to encrypted device storage, sends only SHA-256 to the API, verifies a read-back challenge, then zeroizes process buffers as far as the platform permits. Section 29.19 defines that flow.

Key Vault settings are mandatory:


Assign `Key Vault Secrets User` to the API only for secrets actually referenced by enabled integrations. Assign `Key Vault Crypto User` on the individual signing key only to the environment-protected signing job identity. Assign `Key Vault Certificates Officer` to a dedicated certificate-rotation identity. Deployment identity may manage control-plane resources and role assignments but must not read secret values or sign releases. Human administrators use PIM and cannot be the routine CI signer.

Use these exact application configuration names for optional external adapters:


If a Key Vault certificate/secret is required, expose only its versionless URI in an App Service slot setting using `@Microsoft.KeyVault(SecretUri=https://kv-wfsm-prod-<suffix>.vault.azure.net/secrets/<name>/)`. Alert if resolution fails or the active certificate has fewer than 30 days remaining. Rotation must be proven without restarting every API instance at once.

Central redaction policy must remove or hash these fields before export: `Authorization`, `Cookie`, `Set-Cookie`, `X-WaterFlex-Station-Authorization`, `X-WaterFlex-Factory-Key`, device/bootstrap token fragments, Wi-Fi SSID/password, customer address, raw request/response body, SQL connection strings, and Key Vault URIs containing versions. Preserve credential ID only as an HMAC-derived correlation label using a dedicated rotating observability key; never log the presented secret or its SHA-256 database hash.

Run secret scanning on the full Git history and every artifact. The blocking patterns include Entra/Azure credentials, PEM/PFX/JWK private material, connection strings with passwords, device token format, `wf_boot_` plaintext pairs, Wi-Fi credentials, and firmware signing keys.

**Secret acceptance:** `az keyvault show` proves private access, purge protection, and RBAC; an API identity cannot sign firmware; a deployment identity cannot read a secret; a signing identity cannot alter infrastructure. Rotate a non-production station certificate and integration certificate with overlapping validity and zero request failures. Inject synthetic token/Wi-Fi values through every error path and prove they do not appear in App Insights, WAF, App Service, SQL audit, CI logs, crash dumps, browser storage, SBOMs, container layers, or firmware artifacts.

### 29.13 Migration build and execution contract

Never run `Database.Migrate()` from API or Worker startup. Build one Linux migration bundle and one review script from the same commit and EF model as the API image:


Package `/app/efbundle` alone in image `acr<suffix>wfsmprod.azurecr.io/wfsm-migrations:<version>-<gitsha>`, run as non-root, and sign/scan it like the API image. The release manifest records its digest, SHA-256, expected starting migration, expected ending migration, and reviewed SQL-script SHA-256.

The Production migration stage requires all of these preconditions:

1. Staging upgraded from an equivalent schema and passed all smoke/load tests for at least 24 hours.
2. `has-pending-model-changes` exits zero and the migration script contains no unapproved data loss, table rewrite, unbounded update, or long blocking index operation.
3. API version N and N-1 both tolerate the pre-migration schema; version N tolerates the post-migration schema. Destructive contract changes occur only in a later release after old code is gone.
4. Latest backup is successful, a point-in-time restore has passed within 30 days, free space is at least twice the estimated migration growth, and no long-running transaction or active incident exists.
5. A database approver and application approver approve the GitHub `production-migrate` environment. The initiator cannot approve their own run.

Run the signed migration image as a one-shot Azure Container Instance in delegated subnet `snet-wfsm-deploy`, with user-assigned identity `id-wfsm-migrate-prod`, private DNS, no public IP, and restart policy `Never`:


The pipeline fails unless exit code is zero and the expected ending migration is present exactly once. Archive logs after redaction, then delete the container group. GitHub environment concurrency must permit only one migration/deployment run per environment; EF's migration lock is a second guard, not the scheduler.

Migration rollback means stop promotion and ship a reviewed forward repair. Do not invoke `dotnet ef database update <old-migration>` in Production. If a migration causes unrecoverable corruption, declare an incident, stop writes, restore to a new database, validate it, repoint the failover/connection target under change approval, and reconcile accepted telemetry from device queues using idempotency keys.

**Migration acceptance:** rehearse each migration from a production-sized anonymized snapshot; capture duration, peak log/data growth, blocking, and query regressions. Run two migration jobs concurrently in Staging and prove only one changes the schema. Kill the winning job mid-migration, rerun it, and prove the database reaches one consistent expected migration set without duplicate/lost rows. The API remains ready throughout an expand migration or is deliberately drained under the approved downtime plan.

### 29.14 Container and registry contract

Add these files:


Do not add a Worker Dockerfile until the Worker meets Section 29.15. The migration image is built as specified in Section 29.13. SQL remains Azure SQL and is not part of a Production Compose stack.

The root `.dockerignore` must exclude at least:


The API Dockerfile contract is:

1. Build stage uses `mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:<approved-sdk-digest>`.
2. Restore copies only `global.json`, `NuGet.Config`, project/solution files, and required props/tool manifests before source, preserving cache correctness.
3. Run restore with NuGet package lock files and `RestoreLockedMode=true`, then `dotnet publish` Release with `--no-restore`, `UseAppHost=false`, and CI deterministic-build properties.
4. Runtime uses `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled@sha256:<approved-runtime-digest>`.
5. Runtime user is numeric non-root UID `1654`; working directory `/app`; port `8080`; no shell/package manager/source/build cache.
6. Entrypoint is `dotnet WaterFlex.SaltMonitor.Api.dll`; image sets only non-secret defaults.
7. OCI labels include source URL, revision, semantic version, creation UTC, licenses, title, and vendor.
8. Container listens on HTTP only behind Front Door/App Service, has a read-only root filesystem contract, writes temporary data only to `/tmp`, and requires no persistent volume.

The resulting effective runtime configuration is:


If the chiseled image lacks globalization data needed by browser-locale behavior, use the corresponding `-extra` chiseled image and pin its digest; never disable hostname/certificate validation or install packages interactively in a running container.

The web Dockerfile contract is:

1. Build stage uses `node:22-alpine@sha256:<approved-node-digest>` and runs `npm ci --ignore-scripts` unless a reviewed package requires an install script.
2. Build-time public `VITE_*` arguments are declared explicitly; none may match the secret scanner.
3. Runtime uses `nginxinc/nginx-unprivileged:1.28-alpine@sha256:<approved-nginx-digest>` or the then-approved version/digest.
4. It copies only `dist` and `nginx.conf`, runs as UID `101`, listens on `8080`, writes PID/cache/temp under `/tmp`, and serves no source maps unless the release policy explicitly uploads private source maps to observability storage.

Every base-image digest is updated through a reviewed dependency pull request. Floating tags are allowed only while resolving a digest in a non-release job. Release tags and deployment manifests always contain digests.

Build locally or in CI with BuildKit:


Use a version such as `1.4.0`, never `latest`, `prod`, or a branch name. Push once, obtain the digest, sign the digest using notation/cosign with the HSM-backed release key or approved keyless GitHub OIDC trust, generate SPDX and CycloneDX SBOMs, and attach provenance and vulnerability reports as OCI referrers. Production App Service settings use `DOCKER_CUSTOM_IMAGE_NAME=acr<suffix>wfsmprod.azurecr.io/wfsm-api@sha256:<digest>` and the corresponding web digest.

ACR policy:


Grant `AcrPull` only to App Service managed identities and migration runner identity. Grant `AcrPush` only to the CI build identity. No human has standing push/delete permission. Protect tags matching `v*`; retain every Production digest and its referrers for seven years or the approved release-record period. A digest referenced by any environment cannot be garbage-collected.

`Test-ContainerContract.ps1` must assert: user ID is nonzero; no writable path except `/tmp`; no shell/package manager/certificate private key/source file; port 8080 responds; `/health/live` succeeds; termination honors SIGTERM; image revision equals release manifest; SBOM/provenance/signature resolve; image contains no critical/high exploitable vulnerability under the approved exception policy.

**Container acceptance:** build twice from the same commit, lockfiles, arguments, and pinned base digests on isolated runners and compare application-layer digests. Run each image with all capabilities dropped, `no-new-privileges`, read-only root, 256 MiB memory, 0.5 CPU, and a writable `/tmp`; normal health and representative API/static requests pass. Tampering with one image byte or deploying an unsigned digest is rejected by the deployment verification policy.

### 29.15 Worker and delivery automation decision

Default decision for the first Production telemetry release: **do not build or deploy `WaterFlex.SaltMonitor.Worker`**. Remove it from deployment manifests and availability claims. Its current heartbeat is not a health signal and creates false operational confidence.

If automatic RouteFlex delivery becomes release scope, all of the following are required before adding `backend/src/WaterFlex.SaltMonitor.Worker/Dockerfile`:

- `DeliveryOutbox` table with immutable idempotency key, aggregate/device/tank IDs, payload schema/version, status, attempt count, next attempt, lease owner/expiry, created/processed/dead-letter UTC, external ticket ID, rowversion, and non-secret last error code.
- Unique index on `IdempotencyKey`; due-work index on `(Status, NextAttemptAtUtc)`; lease acquisition as one atomic SQL statement using locking semantics tested on Azure SQL.
- Telemetry transaction evaluates a documented debounce rule and inserts the domain change/outbox row atomically. The reference default is three consecutive valid readings below 35% spanning at least 24 hours, then a seven-day cooldown per installation; `REPLACE_WATERFLEX_DELIVERY_POLICY` must replace or approve it.
- RouteFlex call carries the same idempotency key and treats an already-created response as success. Exact endpoint, product/quantity rules, authentication, timeout, retryable statuses, and SLA must be supplied by the RouteFlex owner.
- Exponential backoff with full jitter, initial 30 seconds, cap 30 minutes, maximum 12 attempts over 24 hours; 400/401/403/404 are non-retryable; 408/429/5xx/network errors are retryable; honor bounded `Retry-After`.
- Dead letters page operations and require an audited replay command. Replays retain the original idempotency key and cannot be initiated through an unaudited SQL edit.
- Graceful shutdown stops leasing, completes or releases in-flight work, and uses a lease shorter than App Service termination grace.

Use these exact settings:


The Worker receives a dedicated managed identity, database role, App Service plan/app `asp-wfsm-worker-prod-eus2` / `app-wfsm-worker-prod-eus2`, minimum two instances, no public route, and `/health/live` plus `/health/ready` on an internal management endpoint. It emits queue-depth/age/attempt/dead-letter metrics defined in Section 29.17.

**Worker acceptance:** 100 concurrent workers processing 10,000 staged outbox rows create exactly one RouteFlex ticket per idempotency key. Kill workers after lease, during HTTP call, and after external success but before SQL commit; restart and prove no duplicate ticket. RouteFlex outage does not block telemetry writes, retries stay within policy, queue alerts fire, and dead-letter replay is audited.

### 29.16 CI/CD and supply-chain pipeline

Reference platform: GitHub Actions in a WaterFlex-owned GitHub organization. Replace with Azure DevOps only through an ADR that preserves OIDC, immutable promotion, segregation of duties, provenance, and all gates below.

Add these workflows and support files:


Normalize package restoration before enabling release CI:

1. The `web/package-lock.json` resolves from the public npm registry (`https://registry.npmjs.org/`). Commit a repository `.npmrc` containing only `registry=https://registry.npmjs.org/`, `engine-strict=true`, `fund=false`, and `audit=false`. If a private mirror is required, update the lock resolved URLs and inject authentication through OIDC/short-lived runner configuration, never `.npmrc` tokens.
2. Generate NuGet `packages.lock.json` for every project with `RestorePackagesWithLockFile=true`, then enforce `dotnet restore --locked-mode`. Continue the checked-in `dotnet-public` feed only if supply-chain owners approve it; otherwise replace `NuGet.Config` with `REPLACE_WATERFLEX_NUGET_FEED` and commit updated locks.
3. Add a frontend `tsconfig.json`, `typecheck`, lint, and unit-test scripts. `npm run build` alone is not a type or behavior gate.
4. Pin PlatformIO Core, platform, framework, toolchain, and library versions as specified in Section 29.19.
5. Replace hard-coded LocalDB integration fixtures with `TestDatabase__AdminConnectionString`. Reference CI uses a digest-pinned SQL Server 2022 Linux service container and unique databases. Until that change lands, use an ephemeral WaterFlex-hosted Windows runner with LocalDB; do not place production credentials on it.

Repository rules for `main`:

- Pull requests only; no force push or branch deletion.
- Two approvals, including one CODEOWNER for `infra/`, identity, migrations, factory, firmware, security, or release workflows.
- Dismiss stale reviews; require approval of the latest push and resolution of every conversation.
- Required checks: `backend-build-test`, `web-typecheck-test-build`, `firmware-build`, `migration-review`, `codeql`, `dependency-review`, `secret-scan`, `license-policy`, `iac-scan`, and `container-policy` when relevant.
- Require signed commits/tags, linear history, merge queue, and successful deployment to Staging before Production promotion.
- Workflow files and CODEOWNERS are owned by `REPLACE_WATERFLEX_PLATFORM_SECURITY_TEAM`.

Workflow permissions default to:


Grant job-local `id-token: write`, `packages: write`, `attestations: write`, or `security-events: write` only where needed. Pin every third-party action by full 40-character commit SHA, allow only GitHub/WaterFlex verified actions, and use ephemeral GitHub-hosted or hardened autoscaled runners. Pull-request jobs from forks receive no secrets, cloud token, or privileged network route.

`ci.yml` runs for pull requests and pushes to `main`, with this order:


Use `--ignore-scripts` only after verifying the locked dependency set builds without required install scripts; record narrowly approved exceptions. Cache packages by lock hash but never cache build output or credentials. Upload test results, coverage, migration SQL, and firmware map/size reports even on failure.

Security gates:

- CodeQL for C# and JavaScript/TypeScript on PR, `main`, and weekly schedule.
- GitHub secret protection with push protection plus Gitleaks full-history scan.
- Dependency Review blocks known exploitable direct or transitive dependencies rated High/Critical unless an unexpired, owner-approved exception identifies reachability and mitigation.
- `dotnet list package --vulnerable --include-transitive`, `npm audit --package-lock-only`, Trivy/Grype image and filesystem scans, Checkov/PSRule for Azure/Bicep, and license allow/deny policy.
- Generate SPDX and CycloneDX SBOMs for API, web, migration, and firmware; sign artifacts and manifest; publish SLSA provenance/attestation.
- Dynamic Staging tests run OWASP ZAP baseline against staff routes plus API abuse tests for auth, tenant isolation, body/rate limits, forwarded-header spoofing, and WAF behavior. Do not fuzz production devices.

`container-build.yml` runs only after `main` CI succeeds. It builds once, pushes commit-addressed images to ACR, captures digests, scans/signs/attests them, and writes `release-manifest.json` containing:


The manifest is validated against `eng/release-manifest.schema.json`, signed by `release-manifest-signing-prod` only during Production approval, and copied to the immutable release container. Missing artifacts use explicit `null`; they are never silently omitted.

`deploy-staging.yml` is triggered by a new candidate manifest, uses GitHub environment `staging`, validates Bicep, applies Staging IaC, runs the migration job, deploys images by digest to staging slots, executes Section 29.18 smoke plus load/security/browser tests, swaps slots, and starts a 24-hour soak. A scheduled physical-device canary must report throughout the soak.

`promote-production.yml` accepts only a Staging-qualified manifest digest and is manually dispatched. GitHub environment `production` requires two reviewers from separate application and operations teams; `production-migrate` requires database approval. Concurrency key `wfsm-production` cancels no in-progress job. It performs:

1. Re-verify signatures, provenance, SBOMs, scans, manifest schema, Staging evidence, and artifact digests.
2. Run Bicep What-If and approval gate.
3. Confirm backup/restore freshness and active incident/change freeze state.
4. Execute expand-only migration.
5. Deploy API/web digests to App Service `staging` slots with 0% public traffic and slot-specific Production settings.
6. Run private warm-up/readiness and authenticated smoke tests.
7. Shift device API traffic 1%, 10%, 50%, then 100%, holding 15 minutes at each stage; separately swap staff web/API after browser checks.
8. Automatically halt/revert traffic on Section 29.18 rollback thresholds.
9. Sign/archive final manifest, evidence, approvals, deployment IDs, effective configuration hashes, and observed metrics.

GitHub Azure identities are environment-specific and federated only to exact repository, branch/workflow, and environment subjects. `id-wfsm-gh-build` can push/sign candidates but cannot deploy. `id-wfsm-gh-stg-deploy` can change Staging. `id-wfsm-gh-prod-deploy` can update Production App Service/Front Door and run approved Bicep but cannot read secrets or migrate SQL. `id-wfsm-gh-prod-migrate` can run the private migration job and nothing else. No workflow uses a stored Azure client secret or publish profile.

**Pipeline acceptance:** create an empty Staging subscription, execute the documented bootstrap, and promote a candidate without console edits. Prove a fork PR, modified action SHA, unsigned image, rebuilt digest, missing SBOM, expired vulnerability exception, unreviewed migration, self-approved environment, wrong-tenant OIDC token, and Production data-plane secret request each fail closed. Re-run the same manifest and prove deployment is idempotent and does not rebuild or retag any artifact.

### 29.17 Observability, monitoring, and alert contract

Add OpenTelemetry through the following package set, pinned centrally in `eng/Versions.props` to one tested version and captured in NuGet lock files:


Use `Azure.Monitor.OpenTelemetry.AspNetCore` only if the implementation selects the direct Azure Monitor distribution instead of the vendor-neutral OTLP exporter. Do not register both exporters and double-send telemetry. The reference default sends OTLP over private/VNet-approved HTTPS to workspace-based Application Insights.

Set these API slot settings:


At pilot volume, sample all traces so security/provisioning/activation failures remain diagnosable. Replacing `parentbased_always_on` requires a cost and incident-diagnosis review and must retain 100% of errors, factory/activation operations, release smokes, and exemplars. Never sample metrics or security audit records.

Use W3C Trace Context. Accept a syntactically valid `traceparent` from trusted Front Door and generate a new trace otherwise. Never use a caller-selected correlation ID as a database key, authorization input, or metric dimension. Return only the resulting 32-hex trace ID in `X-WaterFlex-Trace-Id`; firmware records the last failed trace ID in its bounded diagnostics, not in normal telemetry labels.

Emit structured JSON logs with UTC timestamp, severity, event ID/name, message template, trace/span IDs, release version, environment, endpoint group, status/result, elapsed milliseconds, and bounded error code. Add actor/station/device correlation only as irreversible HMAC labels per Section 29.12. Customer name/address, serial, hardware ID, dealer display name, raw SQL, exception request data, and all credential material are prohibited in centralized logs.

Reserve these application event IDs and names:

| Range | Event names |
|---|---|
| 1000-1099 | `ApiStarted`, `ApiStopping`, `ConfigurationRejected`, `ReadinessChanged` |
| 1100-1199 | `DeviceAuthenticationFailed`, `DeviceUnavailable`, `TelemetryRateLimited` |
| 1200-1299 | `TelemetryBatchAccepted`, `TelemetryBatchRejected`, `TelemetryPersistenceRetried`, `TelemetryPersistenceFailed` |
| 1300-1399 | `StaffAuthenticationFailed`, `AuthorizationDenied`, `DealerMappingRejected` |
| 1400-1499 | `FactoryAuthenticationFailed`, `FactoryDeviceRegistered`, `FactoryRegistrationRejected` |
| 1500-1599 | `CommissioningSessionCreated`, `ActivationAttempted`, `ActivationCompleted`, `ActivationFailed`, `CommissioningExpired` |
| 1600-1699 | `ExternalDependencyFailed`, `CircuitOpened`, `CircuitClosed` |
| 1700-1799 | `RetentionStarted`, `RetentionCompleted`, `RetentionFailed` |
| 1800-1899 | `OutboxLeased`, `DeliveryCreated`, `DeliveryRetryScheduled`, `DeliveryDeadLettered` when Worker exists |

Never log routine successful telemetry per reading at Information level. Aggregate batch counters and traces; use Debug only in a time-bounded, approved diagnostic setting that preserves redaction.

Create meter `WaterFlex.SaltMonitor` and these stable metric instruments:


Allowed metric dimensions are bounded enums: `environment`, `result`, `endpoint_group`, `auth_scheme`, `failure_code` from a reviewed allowlist, `reading_status`, `reporting_status`, `dependency`, `http_method`, `status_class`, `firmware_major_minor`, and `release_version`. Never use device/credential/session/trace/customer/tank/dealer/station IDs, serials, URLs, exception messages, or user-agent as metric dimensions.

Provision these Azure Monitor workbooks from Bicep/JSON templates in `infra/monitoring/`:

1. `wb-wfsm-service-overview`: availability, request rate, 4xx/5xx, latency percentiles, instance count/restarts, current release, readiness, SQL dependency, and Front Door/WAF health.
2. `wb-wfsm-device-fleet`: accepted/duplicate/rejected readings, active reporting states, firmware major/minor distribution, reconnect burst, RSSI/quality aggregates, and no customer-identifying dimensions.
3. `wb-wfsm-commissioning`: session creation/completion/expiry, activation success/latency/failure code, factory registration, dealer-denial counts, and station health.
4. `wb-wfsm-data`: SQL CPU/data/log/storage, sessions, deadlocks, blocking, failed connections, query regressions, retention jobs, backup/failover state.
5. `wb-wfsm-security`: Entra failures, authorization denials, WAF actions, rate limits, secret-scan/deployment-policy results, Key Vault access anomalies, and break-glass use.
6. `wb-wfsm-release`: manifest/artifact versions, rollout stage, smoke results, error/latency comparison to prior version, rollback threshold state, and firmware cohort.

Create availability tests from at least three Azure regions:

- `avail-wfsm-staff-live`: `GET https://saltmonitor.waterflex.com/health/live`, every 1 minute, 10-second timeout, expect 200 and exact schema.
- `avail-wfsm-device-live`: `GET https://sensor-api.saltmonitor.waterflex.com/health/live`, every 1 minute, expect 200.
- `avail-wfsm-staff-auth`: every 5 minutes, obtains a `SaltMonitor.SyntheticMonitor` application token from the certificate-backed `app-wfsm-synthetic-prod` client, calls a non-mutating synthetic endpoint, and verifies release/schema. Store no password and exclude this identity from business counts.
- `avail-wfsm-telemetry-canary`: a dedicated synthetic device posts a unique, valid reading every 5 minutes to an isolated canary installation and queries a protected verification endpoint or SQL-side monitor to prove persistence within 2 minutes. Retention excludes it from operational fleet metrics.

The synthetic device credential is unique, revocable, labeled synthetic in server-owned data, and rotated every 90 days. Synthetic checks never use a real customer/tank or factory credential.

Define Action Groups `ag-wfsm-prod-critical`, `ag-wfsm-prod-warning`, and `ag-wfsm-prod-security`. Critical pages the primary/secondary on-call; warning creates the approved operations ticket; security pages the security receiver. Every receiver must acknowledge a quarterly test. Alerts carry runbook URL, dashboard URL, affected environment, release, trace/query link, and deduplication key.

Minimum alert rules:

| Alert | Severity and threshold | Window | Required action |
|---|---|---|---|
| `WfsmPublicAvailabilityFailed` | Sev 0: two or more regions fail either live test | 2 of 3 evaluations over 3 min | Page operations; test Front Door/origin/region. |
| `WfsmReadinessUnavailable` | Sev 0: ready success below 99% | 5 min | Page operations and database owner. |
| `WfsmTelemetryServerErrors` | Sev 1: 5xx/total above 2% and at least 20 requests | 5 min | Halt rollout; inspect release/SQL. |
| `WfsmTelemetryLatencyHigh` | Sev 2: p95 above 1,000 ms with at least 100 requests | 10 min | Scale/investigate SQL and throttling. |
| `WfsmTelemetryAcceptanceStopped` | Sev 1: zero accepted canary readings while canary is scheduled | 10 min | Page operations; validate device path end to end. |
| `WfsmDuplicateSpike` | Sev 2: duplicate readings above 25% and twice 24-hour baseline | 15 min | Investigate firmware retry/network or acknowledgement loss. |
| `WfsmDeviceOfflineSpike` | Sev 1: offline share rises 10 percentage points or 100 devices, whichever is lower | 15 min | Correlate provider/DNS/release; do not create tickets blindly. |
| `WfsmActivationFailureHigh` | Sev 1: failure above 5% with at least 5 attempts | 15 min | Halt factory/field rollout and page provisioning owner. |
| `WfsmAuthorizationDenialSpike` | Sev 2 security: above 5 times 7-day time-of-day baseline | 10 min | Investigate role mapping/token abuse. |
| `WfsmFactoryAuthFailure` | Sev 1 security: 5 failures for one station or 20 global | 5 min | Disable suspect station certificate and page security/factory. |
| `WfsmWafCriticalRule` | Sev 1 security: managed critical rule or custom factory block anomaly | 5 min | Page security; preserve evidence. |
| `WfsmSqlCpuHigh` | Sev 2: CPU above 80% | 15 min | Inspect Query Store and scale only after query triage. |
| `WfsmSqlStorageHigh` | Sev 2 at 85%; Sev 1 at 95% | 5 min | Run retention/capacity procedure. |
| `WfsmSqlDeadlockOrBlocking` | Sev 2: deadlock present or blocking above 30 sec | 5 min | Inspect transaction/query plan. |
| `WfsmKeyOrCertificateExpiring` | Sev 2 at 45/30 days; Sev 1 at 14/7 days | Daily | Rotate and verify overlap. |
| `WfsmBackupOrFailoverUnhealthy` | Sev 1: backup age/RPO or failover replication outside approved target | 10 min | Page database/operations owner. |
| `WfsmObservabilityIngestionStopped` | Sev 1: app traffic exists but logs/metrics/traces stop | 10 min | Restore telemetry path; do not assume service healthy. |
| `WfsmOutboxBacklog` | Optional Sev 1: oldest age above 15 min or any dead letter | 5 min | Page delivery owner; telemetry remains available. |

Use dynamic baselines only where explicitly shown; static release rollback gates in Section 29.18 must not be weakened by a learned baseline during a bad rollout. Alert rules and workbooks are IaC, versioned, reviewed, and deployed to Staging before Production.

Log Analytics retention is 90 interactive days. Archive selected application/security/audit tables to private immutable storage for 365 days; provisioning/security audit database records remain 730 days unless the approved privacy policy replaces them. Apply a daily ingestion budget alert at 50%, 75%, and 90% of forecast. Never configure a hard daily cap that silently removes Production security/incident evidence; reduce debug volume or sample approved traces instead.

Runbook links required by alerts:


**Observability acceptance:** in Staging, inject one controlled failure for every alert class, verify the alert opens within its stated window, reaches the correct receiver, deduplicates, includes a working runbook/query/dashboard link, and resolves automatically when healthy. Trace one synthetic telemetry reading from Front Door through auth, SQL commit, acknowledgement, metric exemplar, and canary verification without exposing its token or identifiers. Stop the exporter while traffic continues and prove `WfsmObservabilityIngestionStopped` fires through an independent platform signal.

### 29.18 Release, progressive delivery, and rollback

Version the deployable system as `MAJOR.MINOR.PATCH` and create signed annotated tags `saltmonitor-v<version>`. A release is one immutable compatibility set: API digest, web digest, migration digest/range, IaC revision, public configuration schema, OpenAPI hash, minimum/maximum firmware, and optional Worker digest. Firmware has its own signed version in Section 29.19 but must appear in the system release compatibility matrix.

Use these exact release channels and states:


Only a manifest digest moves between states. A withdrawn digest cannot be promoted again without a new release and incident/change reference. Do not mutate an existing semantic version, Git tag, OCI tag, firmware binary, SBOM, migration bundle, or release note.

Create `RELEASE.md` and a generated release record with:


Reference release window: Tuesday through Thursday, 14:00-18:00 UTC, with the application owner, operations commander, and database owner available through the 60-minute observation period. `REPLACE_WATERFLEX_RELEASE_WINDOW` may replace it. Emergency releases require an incident commander, the same technical checks where physically possible, and a retrospective within two business days.

Production preflight fails closed unless:

1. Candidate manifest signature, every artifact signature/digest, provenance, SBOM, license report, vulnerability policy, and source tag verify.
2. Staging used those exact digests for at least 24 hours with green synthetic, physical-device, browser, load, WAF, and alert tests.
3. All required GitHub checks/approvals are current; no High/Critical unresolved security finding or expired exception exists.
4. Bicep What-If has no unapproved delete, public exposure, permission expansion, stateful replacement, region change, or reduced retention.
5. The migration script is reviewed, backward-compatible, rehearsed at production scale, and its starting migration equals Production.
6. Latest SQL backup and failover replication meet RPO; a restore drill passed within 30 days; database utilization is below 70% and no blocking transaction exists.
7. Front Door, SQL, Key Vault, ACR, Entra, App Service, DNS, certificate, integration, and observability health are green; public certificates have more than 45 days remaining.
8. There is no Sev 0/1 incident, conflicting infrastructure change, Azure service advisory affecting the path, or approved change freeze.
9. Prior Production manifest remains runnable, its images and compatible schema are retained, and rollback has been dry-run in Staging.
10. Device queues can cover at least the maximum release plus rollback window and the canary cohort is identified by server-owned assignment, never a client assertion.

Create `scripts/smoke/Test-Deployment.ps1` with these parameters:


Token files are created on a memory-backed/ephemeral runner volume with restrictive ACLs, are never command-line values or uploaded artifacts, and are deleted in `finally`. The script must redact command exceptions before writing JUnit/JSON.

The smoke suite must execute these checks, each with a stable test ID:

| ID | Check |
|---|---|
| `SMOKE-EDGE-001` | HTTP redirects once to HTTPS; TLS hostname/chain/protocol and security headers pass for all hosts. |
| `SMOKE-EDGE-002` | Wrong-host API paths, Azure default origins, forbidden methods, oversized body, and spoofed forwarded headers fail as designed. |
| `SMOKE-APP-001` | `/health/live`, `/health/ready`, and `/version` return expected release/schema with no secret. |
| `SMOKE-WEB-001` | `/`, `/fleet`, and `/fleet/<synthetic-device-id>` load; missing hashed asset is 404; current assets match manifest; CSP has zero violations. |
| `SMOKE-AUTH-001` | Employee can read ops; anonymous/dealer cannot; sign-out invalidates browser state. |
| `SMOKE-AUTH-002` | Dealer A can access only dealer A; dealer B and unmapped/multi-mapped tokens cannot cross scope. |
| `SMOKE-FACTORY-001` | Factory route requires both approved operator and station token plus source location; every missing/wrong half fails. Use non-mutating validation mode. |
| `SMOKE-DEVICE-001` | Synthetic device posts one unique valid reading; response is `accepted`, persisted fill is correct, and trace is observable within 2 minutes. |
| `SMOKE-DEVICE-002` | Exact replay returns `duplicate` with original reading ID/time/fill and database count remains one. |
| `SMOKE-DEVICE-003` | Wrong, expired, revoked, and non-Active credentials return the documented 401/403 without updating business state. |
| `SMOKE-VALIDATION-001` | Unknown ownership JSON, explicit nulls, malformed values, duplicate in-batch keys, future time, and 64 KiB limits produce documented 4xx, never 500. |
| `SMOKE-SQL-001` | Expected migration exists once, encrypted connection is in use, runtime identity cannot execute DDL, and no pending model change exists. |
| `SMOKE-OBS-001` | Trace, metrics, structured logs, Front Door access, and redaction are visible for a synthetic request; no credential/address/body appears. |
| `SMOKE-ROLLBACK-001` | Prior manifest is resolvable, signed, retained, schema-compatible, and passes private-slot readiness before canary begins. |

Run read-only and synthetic-mutating smoke tests against the private deployment slot before public traffic. Factory smoke must provide a dedicated validation endpoint or disposable synthetic inventory transaction that is cleaned through an audited API, never direct SQL deletion.

For API rollout, Front Door origin group `og-wfsm-api-prod` has origins `api-current` and `api-candidate`. Bicep parameter `apiCandidateTrafficPercent` accepts only `0`, `1`, `10`, `50`, or `100` and converts them to deterministic origin weights. Session affinity is disabled. Advance only after the hold and minimum sample criteria:

| Stage | Candidate traffic | Hold | Minimum evidence |
|---|---:|---:|---|
| Private warm-up | 0% | 10 min | All smokes pass; 100 synthetic batches; stable readiness/startup. |
| Canary | 1% | 15 min | At least 1,000 total telemetry requests or 15 min, including physical canary reports. |
| Limited | 10% | 15 min | At least 5,000 requests; no threshold breach. |
| Half | 50% | 15 min | At least 10,000 requests; SQL/instance capacity below warning. |
| Full | 100% | 60 min | All smokes rerun; alerts and business metrics green. |

If pilot traffic cannot meet a minimum request count, extend the hold rather than waiving it; cap each stage at four hours, after which the release owner must withdraw or document a lower-volume validation plan approved before the release.

The staff SPA/API is lower volume. After API reaches at least 10% device traffic without regression, expose candidate staff origin only to an Entra-controlled internal canary group for 15 minutes, run browser/E2E/accessibility smokes, then switch all staff traffic. Keep `index.html` uncached and retain prior assets for the full rollback window.

Automatic halt and rollback triggers during any stage:

- Any new Sev 0 or release-correlated Sev 1 alert.
- Two consecutive synthetic telemetry canary failures or any persisted-reading correctness/idempotency failure.
- Candidate 5xx rate above 1% with at least 20 errors, or more than 0.5 percentage points above current origin for 5 minutes.
- Candidate telemetry p95 above 1 second and 50% above current origin for 10 minutes.
- Readiness below 100% for 2 minutes, restart loop, out-of-memory kill, or instance saturation with no healthy spare.
- SQL CPU above 85% for 10 minutes, storage/log above 95%, blocking above 30 seconds, deadlock regression, or failed failover/backup health.
- Authentication/authorization denial increases by both 2 percentage points and 50 events over current origin, excluding an explained attack blocked equally.
- Activation/factory failure above 5% with at least 5 attempts, dealer-scope breach, secret/redaction failure, signature/provenance mismatch, or WAF bypass.
- Physical canary cannot buffer/report or current fleet offline share rises by 10 percentage points/100 devices, whichever threshold is lower.

Rollback decision authority is the operations commander; security can unilaterally halt for identity, data exposure, signing, or isolation failure. The pipeline automatically sets candidate traffic to 0 on a trigger and preserves evidence. It does not wait for a meeting to remove a known-bad candidate.

Application rollback target is under five minutes:


The actual web swap/Front Door commands must be generated from the deployed Bicep outputs so names cannot drift. The sample above states the required operation; validate CLI flags against the pinned Azure CLI image in CI. Never change DNS for a normal application rollback and never rebuild the prior image.

Database rollback is forward-only under the expand/contract rule. If the new API has received traffic, do not run an EF down migration. Return traffic to the prior schema-compatible API and create a reviewed forward repair. If data integrity or confidentiality cannot be preserved, stop mutating routes, keep health/status communication available, declare an incident, and execute the restore/repoint procedure in Section 29.20.

After any partial rollout or regional restore:

1. Query accepted telemetry by device/boot/sequence and reconcile against retried device queues; uniqueness must collapse replay.
2. Reconcile commissioning activation attempt IDs, bootstrap consumed state, operational credentials, installation/calibration counts, and provisioning audit chain.
3. If Worker exists, reconcile every outbox idempotency key with RouteFlex before replay.
4. Revoke candidate-created credentials only through an audited recovery flow; never bulk-delete credentials to make counts match.
5. Record missing, duplicate, delayed, or manually repaired records in the incident evidence and notify affected operations owners under policy.

Post-release, observe for 60 minutes, rerun all Production smokes, confirm no alert suppression was added, capture current/previous comparison, mark the manifest `production`, and publish redacted release notes. Close the change only when the physical canary reports after the full rollout and the next scheduled backup is successful.

**Release/rollback acceptance:** in Staging, inject every automatic trigger at each traffic stage and prove promotion stops, candidate traffic reaches 0 within five minutes, prior API/web remain schema-compatible, synthetic/device replay creates no duplicate, and evidence is retained. Run a quarterly Production game day using synthetic traffic: deploy a deliberately failing candidate, exercise automatic rollback without DNS change or image rebuild, and measure detection plus restoration against the five-minute target.

### 29.19 Firmware and factory production contract

The current UART sketch cannot be factory-released or field-deployed. The reference design retains Arduino Nano ESP32 plus A02YYUW for the pilot, but WaterFlex hardware and safety owners must resolve these replacement points before ordering production units:

| Replacement token | Reference default | Required evidence |
|---|---|---|
| `REPLACE_WATERFLEX_MANUFACTURER` | Contract manufacturer operating under WaterFlex work instructions | Executed quality/security agreement and named escalation. |
| `REPLACE_WATERFLEX_HARDWARE_REVISION` | `WF-NANO-A02-REV-A` | Released schematic, wiring, enclosure, BOM, test points, and change-control record. |
| `REPLACE_WATERFLEX_DEVICE_MODEL` | `Arduino Nano ESP32` | Server/factory/firmware exact model string and supported board ID. |
| `REPLACE_WATERFLEX_HARDWARE_ID_SOURCE` | Wi-Fi station MAC, normalized as 12 uppercase hex characters | Proof that source is globally unique, stable, readable before provisioning, and not client-overridable. |
| `REPLACE_WATERFLEX_SERIAL_FORMAT` | `WF-NANO-` plus eight zero-padded decimal digits and check digit | Allocation owner, collision policy, printer/scan validation, and reserved ranges. |
| `REPLACE_WATERFLEX_SENSOR_TOLERANCE` | Greater of +/-20 mm or +/-2% at factory reference distances | Hardware owner signs measured capability and reject limits. |
| `REPLACE_WATERFLEX_PROVISIONING_GESTURE` | Dedicated recessed button held for 8 seconds while powered | Threat/safety review and released enclosure/hardware support. Do not substitute an undocumented reset sequence. |
| `REPLACE_WATERFLEX_FACTORY_EGRESS_CIDRS` | Subset of corporate CIDRs dedicated to factory cells | Network/security owner and WAF named-location test. |
| `REPLACE_WATERFLEX_FACTORY_PRINTER` | 300 dpi thermal-transfer printer with scanner verification | Approved model, label stock/ribbon, driver, spare, and calibration work instruction. |
| `REPLACE_WATERFLEX_FACTORY_RETENTION_YEARS` | 7 years | Quality/privacy/legal approval for device genealogy and test evidence. |
| `REPLACE_WATERFLEX_SECURE_BOOT_FEASIBILITY` | ESP32 Secure Boot V2, flash encryption Release mode, encrypted NVS, signed dual-slot OTA | Destructive engineering validation on the exact Nano/module/bootloader; documented recovery and yield impact. |
| `REPLACE_WATERFLEX_WIFI_SUPPORT` | 2.4 GHz WPA2-Personal, DHCP, DNS, NTP, outbound TCP 443; no enterprise/captive guest Wi-Fi | Product/support approval and customer compatibility data. |

Any hardware substitution creates a new hardware revision and requires fresh RF/electrical/environmental/regulatory, secure-boot, sensor, enclosure, and HIL evidence. Software approval cannot waive required FCC/IC/CE, safety, battery/power, materials, or installation approvals; WaterFlex compliance owners must identify the applicable jurisdictions and standards.

#### Pinned firmware build

Use Python 3.13 and pin PlatformIO Core in `firmware/requirements-build.txt`:


Reference `firmware/platformio.ini` baseline:


`6.12.0` and `6.1.18` are explicit reference pins, not claims about a currently tested repository state. Replace only through a dependency pull request that records resolved framework/toolchain package names, versions, download hashes, compiled map/size changes, UART/Wi-Fi/TLS/flash HIL results, and signed release approval. Archive `pio pkg list --json-output`, compiler version, partition CSV, bootloader, application ELF/MAP/BIN, merged factory image, SBOM, and SHA-256 in the release record.

Add these firmware files and modules:


No SSID, Wi-Fi password, bootstrap/operational secret, private signing key, test backdoor, factory API token, or environment-switch command may appear in source or a release binary. Production builds fail if HIL/test endpoints or debug serial secret output are enabled.

#### Device storage and boot security

Subject to `REPLACE_WATERFLEX_SECURE_BOOT_FEASIBILITY`, use Secure Boot V2, unique per-device flash-encryption key in Release mode, encrypted NVS, anti-rollback secure version, disabled JTAG, disabled unprotected ROM download, and signed bootloader/partition/application images. Stations flash only a CI-signed release bundle; no Production private signing key exists on a station or build runner.

Use dual OTA application partitions plus `otadata`, encrypted `nvs_keys`, encrypted `nvs`, a crash-safe telemetry journal, and a read-only factory metadata partition. Reserve at least two complete application image slots and 14 days of telemetry at the approved maximum record size before freezing `wfsm_ota.csv`. Disable plaintext core dumps; an approved encrypted crash-dump design must include access, deletion, and secret-redaction controls.

Persist these records with schema version and CRC/authentication:


Secrets are never exposed through USB serial, SoftAP responses, logs, QR labels, crash dumps, or a general factory read command. A privileged factory verification command returns only hash/challenge success. Factory reset erases Wi-Fi and operational credentials but does not disable secure boot/flash encryption or silently revive a consumed bootstrap credential; field recovery requires an audited server-issued replacement bootstrap credential and physical access.

Power-loss tests must interrupt every record update at every flash-sector boundary. On reboot, the device selects the last authenticated complete record, never reuses a `(BootId, SequenceNumber)`, and never discards an unacknowledged reading. Define `BootId` as a persisted random UUID generated at each successful application boot; sequence starts at zero and is monotonic for that boot.

#### Network, setup, and telemetry behavior

Reference field setup uses a WaterFlex-managed Android application and a temporary WPA2 SoftAP. It is a missing product that must be built, signed, MDM/distribution-managed, threat-modeled, and tested; the browser Web Serial workflow is not the Production substitute.

When the approved physical provisioning gesture occurs, the device:

1. Opens SoftAP `WaterFlex-Setup-<last6Serial>` with a random 16-character per-device setup password injected at factory and printed only on a tamper-evident inner setup card. The password is distinct from bootstrap/operational secrets.
2. Serves HTTPS or an application-layer authenticated/encrypted local protocol at `192.168.4.1`; if the exact platform cannot support locally trusted HTTPS, the Android app must authenticate the device using the signed factory public identity/challenge before transmitting Wi-Fi credentials. WPA2 alone is not sufficient device authentication.
3. Accepts only SSID, passphrase, security type, commissioning session ID, and a server-signed short-lived setup nonce. It never accepts customer/tank/dealer IDs as authoritative ownership.
4. Closes after 15 minutes, five failed setup authentications, successful activation, or button press. It cannot be reopened remotely.
5. Stores Wi-Fi credentials only in encrypted NVS and redacts them from all diagnostics.

Use these compile-time/runtime defaults:


The device supports only the approved `REPLACE_WATERFLEX_WIFI_SUPPORT` matrix. Setup must detect unsupported 5 GHz-only, WPA-Enterprise, captive portal, hidden SSID, no DHCP/DNS/NTP/443, and weak signal conditions and return a non-secret actionable code. It scans but does not upload neighboring SSIDs. Support documentation must list required outbound endpoints and ports; no inbound customer firewall rule or port forwarding is required.

Obtain trusted time from persisted monotonic-bounded time plus at least three approved NTP endpoints (`REPLACE_WATERFLEX_NTP_ENDPOINTS`; reference `time.cloudflare.com`, `time.google.com`, and `pool.ntp.org`) over UDP 123. Never move trusted time backward. Do not make an HTTPS connection with certificate-date checks disabled. If no trustworthy time can be established, queue readings with null `ObservedAtUtc`, expose a time-sync error, and retry with backoff.

Validate the public CA chain, hostname, validity, EKU, and TLS 1.2 or later. Carry both active and next root CA for a tested rollover period. Do not pin a leaf certificate or turn on `TrustServerCertificate`. Certificate and DNS failures queue data and back off; they never trigger factory reset or discard credentials.

The journal holds 336 readings (14 days at hourly schedule). On overflow, stop overwriting silently: preserve newest operational diagnostics plus the oldest unacknowledged boundary, increment a durable data-loss counter, expose `queue_overflow`, and alert after reconnection. Upload oldest first in batches of at most 24. Remove a record only after an accepted or duplicate acknowledgement matches boot/sequence. Use exponential backoff with full jitter; honor bounded `Retry-After`; add fleet-wide startup/report jitter so power restoration cannot create a synchronized burst.

#### Retry-safe activation and credential rotation

Add authentication scheme `BootstrapToken` and endpoint `POST /api/v1/device/activate`. Bootstrap token format remains `<bootstrapCredentialId>.<base64url-32-byte-secret>` and uses the same fixed-time hash validation principles as the operational scheme, with separate rate limits, audit, and failure codes.

Before the first activation request, the device atomically persists:

- random UUID `activationAttemptId`;
- random 32-byte operational secret;
- credential ID `wf_dev_` plus 16 random Base64URL bytes;
- SHA-256 operational-secret hash;
- commissioning session ID received from the authenticated setup app.

Request contract:



The server derives device identity from the bootstrap token, requires the live session to reference that device, verifies factory manifest/model/hardware policy, and performs one serializable transaction that creates installation, calibration, provisional hash-only operational credential, audit event, and `AwaitingFirstTelemetry` state. It returns no plaintext secret:


An exact replay with the same attempt, session, credential ID, and hash returns the same state. A changed payload under the same idempotency key returns 409. A different attempt while one is live returns the existing non-secret status and recovery instruction, never a second credential/install. Bootstrap failed attempts are counted and rate-limited by credential plus source without enabling an unauthenticated permanent denial of service.

The provisional operational credential may call telemetry only for its own AwaitingFirstTelemetry device. The first valid persisted telemetry transaction atomically marks device Active, session completed, credential active, and bootstrap consumed. Replayed first telemetry is duplicate and leaves the completed state unchanged. Expiry/recovery atomically revokes the provisional credential and returns a safe device state; a scheduled cleanup job handles sessions without relying on a technician read.

Credential rotation uses `POST /api/v1/device/credentials/rotate` authenticated by the current credential. Device generates/persists the next secret and sends only ID/hash plus a rotation attempt ID. Server creates a scoped pending credential idempotently; first telemetry under it activates it and starts a 30-day old-credential overlap, after which the old credential is revoked. Rotate at day 300, maximum age 365 days. An offline/failed device uses the old credential during overlap; after all credentials expire, only the audited physical recovery flow can restore access.

Activation and rotation settings:


#### Signed OTA and configuration

Provision private Blob containers `firmware-manifests` and `firmware-images` in `st<suffix>wfsmprod`; expose read-only objects only through `firmware.saltmonitor.waterflex.com`. Disable listing, writes, query-string SAS in logs, MIME sniffing, and mutable overwrite. Object names are content/version-addressed:


Every manifest is canonical JSON signed with `firmware-signing-prod` and contains:


The device validates signature, key/version, product, hardware revision, monotonic semantic/secure version, time window, size, URL host, and image SHA-256 before selecting an inactive OTA slot. It marks the new image healthy only after sensor/UART, encrypted storage, queue recovery, Wi-Fi, TLS, API authentication, and one acknowledged canary telemetry pass within 10 minutes. Otherwise bootloader rolls back to the prior signed image. Burn anti-rollback secure version only after the cohort is healthy and the prior image is no longer an approved rollback target; security fixes that raise it require explicit irreversible-change approval.

Roll out firmware by server-owned cohorts:

| Channel | Default cohort/hold | Promotion evidence |
|---|---|---|
| `firmware-lab` | 10 bench devices, 72 hours | Full HIL/fault matrix, no unexplained reset/data loss. |
| `firmware-factory-pilot` | 25 new units, 7 days | Factory yield/test and activation green. |
| `firmware-field-canary` | 1% or 10 devices, whichever is greater, 7 days | Reporting/queue/power/RSSI/support metrics no worse than threshold. |
| `firmware-limited` | 10%, then 50%, 72 hours each | No rollback/error/offline regression. |
| `firmware-general` | Remaining fleet | 14-day observation before closing release. |

Halt on any signature/boot failure, automatic rollback above 1%, crash/reset rate twice baseline, telemetry acceptance drop above 2 percentage points, offline increase above 5 percentage points, queue overflow/data loss, activation regression, or support safety incident. Cohort assignment and manifest response come from the server; devices cannot opt themselves into Production/general.

#### Factory station, CLI, labels, and genealogy

Reference station is WaterFlex-managed Windows 11 Enterprise hardware with Secure Boot, TPM 2.0, BitLocker, Defender/EDR, Intune compliance, no standing local admin, allowlisted signed applications, restricted USB device classes, automatic patching, 15-minute lock, and a unique non-exportable station certificate in TPM. It runs on an isolated factory VLAN with outbound DNS/NTP/HTTPS only to Entra, `factory-api`, firmware host, EDR/management, and approved printer services; no inbound Internet route.

Build and sign `wfsm-factory-cli` as a versioned .NET 10 self-contained tool. It authenticates the human interactively with phishing-resistant Entra MFA and the station through certificate client credentials. Commands and sequence:


The CLI performs each device as a server-tracked state machine: `Allocated`, `Identified`, `Flashed`, `Provisioned`, `Secured`, `TestPassed`, `LabelVerified`, `Released`, or `Quarantined`. It may resume an interrupted unit idempotently but cannot skip a state or overwrite released identity. Two-person approval is required to rework identity, replace hardware ID, issue recovery bootstrap, or release a quarantined security failure.

At provision, station CSPRNG generates a 32-byte bootstrap secret inside the station process, writes it once to encrypted device storage, sends only its SHA-256 hash plus factory metadata to `POST /api/v1/factory/devices`, verifies a server challenge signed/authenticated by the device-held secret, and zeroizes transient buffers. Factory API records operator OID, station service principal/certificate thumbprint, CLI/release/config versions, serial, hardware ID, model/revision, timestamps, test-result digest, and audit trace. Logs show only credential ID and irreversible correlation label.

Factory test fixture must verify:

- Exact hardware ID, serial, model/revision, firmware/config/manifest hashes, secure-boot/flash-encryption/JTAG/download-mode eFuse state.
- A02YYUW valid frames and checksum/range rejection; measured distances at 100, 500, 1,500, and 3,000 mm within `REPLACE_WATERFLEX_SENSOR_TOLERANCE` using a traceably calibrated target/jig.
- Five-sample spread at each point, UART recovery after noise/truncated frames, sensor disconnected/shorted behavior, and no unsafe power/thermal condition.
- Wi-Fi association, DNS, NTP, TLS chain/hostname, bootstrap challenge, encrypted storage, queue write/read, reset/brownout recovery, OTA slot boot, and diagnostics redaction.
- Radio RSSI/throughput at an approved fixture attenuation, USB/programming reliability, supply current limits, button/LED behavior, and label scan consistency.

The external device label contains only WaterFlex product/model, serial, hardware revision, regulatory IDs, power/safety marks, and support URL. The inner setup card contains SoftAP setup password and a signed QR payload with serial, model, hardware revision, bootstrap credential ID, factory manifest digest, label key ID, and signature. It contains no bootstrap/operational secret or Wi-Fi credential. Scanner verification decodes, validates signature/check digit, and confirms exact server/device identity before release.

Retain genealogy under `REPLACE_WATERFLEX_FACTORY_RETENTION_YEARS`: BOM/lot/date codes, module/sensor serials where available, station/operator, CLI/fixture/calibration versions, release digest, all test measurements/results, eFuse summary without keys, labels, rework/quarantine/scrap events, and shipment/work-order linkage. Fixture calibration expires at the approved interval; an expired fixture blocks tests.

Quarantined units cannot call activation or be relabeled as new. Rework preserves the original genealogy and requires reason/disposition. Scrap irreversibly revokes bootstrap credentials, erases flash where possible, records destruction, and prevents serial reuse. Factory API/API outage queues encrypted work locally for at most one shift but **does not release or ship a unit** until server registration/audit and label verification succeed.

#### Firmware/factory verification matrix

Automate native parser/state-machine tests, embedded tests, and HIL scenarios for: every UART byte offset/checksum/range; DNS/NTP/TLS/CA rollover; Wi-Fi wrong password/no DHCP/captive portal/weak signal; HTTP 400/401/403/409/413/429/5xx/timeouts/truncation; duplicate/out-of-order acknowledgements; queue full/wear; power cut during each flash write/activation/rotation/OTA phase; clock rollback; corrupt NVS/journal/OTA image; credential expiry/revocation; session expiry; 14-day outage and reconnect burst; server failover; bad signed/unsigned/wrong-hardware/expired OTA; and factory station/operator/certificate/network/printer/fixture failures.

Commands in the pinned build environment:


Because `--require-hashes` is required, `requirements-build.txt` must include transitive hashes generated by the approved dependency process, not only the one illustrative line above.

**Firmware/factory acceptance:** provision 100 pilot units through at least two stations/operators with zero duplicate identity, secret exposure, skipped state, unsigned image, or label mismatch; every released unit passes the calibrated fixture and activates exactly once. Run the complete HIL fault matrix on the signed candidate, power-cycle 1,000 times across storage/OTA states, retain 14 days of offline readings, reconnect without an API surge or data duplication, rotate credentials/CA/signing keys, and force a bad OTA rollback. Quarantine/rework/scrap, station revocation, server outage, and lost-setup-card procedures must be witnessed and auditable before shipment.

### 29.20 Backup, restore, and disaster recovery

Reference objectives are `REPLACE_WATERFLEX_RPO_MINUTES=5` and `REPLACE_WATERFLEX_RTO_MINUTES=60`. These are unproven until a representative drill meets them. The application owner defines recovery of service; the data owner defines acceptable loss; security owns credential/signing-key compromise decisions. A device queue reduces telemetry loss but does not satisfy RPO for commissioning, credentials, audits, or factory state.

#### Protected asset inventory

| Asset | Protection/default | Restore authority |
|---|---|---|
| Azure SQL operational database | 35-day PITR, geo-zone redundant backup, failover group, weekly/monthly/yearly LTR from Section 29.10 | Database incident lead plus operations commander. |
| API/web/Worker/migration images | Signed immutable ACR digests, Central US geo-replica, Production digest retained for approved record period | Release pipeline from signed manifest only. |
| Firmware images/manifests | Versioned, immutable Blob plus release manifest/signatures/SBOM; geo-redundant replication | Firmware release owner plus security for signing changes. |
| Infrastructure/policies/workflows | Protected Git repository, signed tag, release bundle, nightly mirror to a separate WaterFlex recovery organization/subscription | Platform owner under break-glass procedure. |
| Release records/evidence | Immutable Blob container with legal hold/time-based retention and geo-redundancy | Release/records owner; no routine delete. |
| Key Vault keys/certificates | Soft delete/purge protection, service geo-replication, per-version encrypted backup for HSM keys/certificates under documented constraints | Security key custodians using two-person approval. |
| Entra app/role/Conditional Access manifests | Nightly sanitized export with object IDs/configuration, encrypted and signed; no private key material | Identity incident lead. |
| DNS/Front Door/WAF/monitoring | Recreated from Bicep and release parameters; external DNS registrar/zone break-glass documented | Network/platform incident lead. |
| Logs/audits/genealogy | SQL retention plus immutable security/audit export and Log Analytics archive | Security/data owner. |
| Factory station pending work | TPM/BitLocker-encrypted queue for at most one shift; unreleased units remain quarantined until server confirmation | Factory lead; not a substitute for cloud backup. |

Use a separate locked resource group `rg-wfsm-recovery-cus` and, if WaterFlex provides it, `REPLACE_WATERFLEX_RECOVERY_SUBSCRIPTION_ID` with distinct PIM groups. Recovery storage `st<suffix>wfsmrecovery` must have public access disabled, private endpoints, versioning, container soft delete 90 days, GZRS/RA-GZRS where supported, immutable time-based retention, change-feed/audit diagnostics, and customer-managed encryption only if the key recovery process is independently proven. Do not create circular recovery in which the only copy of a vault key is encrypted by that same unavailable key.

Protect Production SQL, Key Vault, ACR, recovery storage, DNS zone, Front Door, and Log Analytics archive with `CanNotDelete` locks. Only a PIM-activated recovery group can remove a lock, and every removal pages security. Backups and recovery artifacts must be readable without a GitHub Actions outage; keep an offline signed copy of the current/previous manifests, public verification keys, emergency contacts, Azure resource IDs, and first-hour commands in the approved emergency repository. It contains no device/Wi-Fi/private signing secret.

#### Scheduled protection and verification

| Frequency | Required job/evidence |
|---|---|
| Continuous | SQL PITR/failover replication; Blob/ACR geo-replication; Front Door health; device queue. |
| Daily | Verify latest SQL restore point age, replication lag, backup redundancy, Key Vault/Blob soft-delete/purge settings, ACR digest replication, Git mirror, Entra export, release-manifest readability, and alert path. |
| Weekly | Verify LTR point exists, randomly retrieve and signature-check one image/firmware/SBOM/manifest, and compare IaC drift. |
| Monthly | Automated SQL PITR to isolated database, schema/invariant/checksum/sample-query validation, credential-safe controls, then audited deletion. |
| Quarterly | Full regional failover/failback game day, application redeploy from immutable artifacts, station/key/certificate recovery tabletop, and alert/escalation test. |
| Annually | Destructive recovery exercise from an empty recovery subscription, DNS/identity dependency tabletop, signing-key compromise drill, and business RPO/RTO reapproval. |

Create Azure Automation/Container App Job or a private CI scheduled job `wfsm-backup-verify` using dedicated identity `id-wfsm-backup-verify-prod`. It may inspect policies, create/delete isolated recovery databases, and write signed evidence; it cannot alter Production rows, issue credentials, change DNS, sign firmware, or deploy applications.

Use these monitoring configuration names:


#### Warm regional failover

Front Door origin groups contain primary East US 2 origins at priority 1 and warm Central US origins at priority 2. Secondary API/web run the exact current Production digests and configuration schema continuously; they use the failover-group SQL listener and secondary-region private endpoints/DNS. Synthetic traffic probes secondary origins directly through a protected diagnostic route so cold configuration drift cannot hide until an incident.

Normal SQL listener is `fog-wfsm-prod.database.windows.net`. Automatic failover grace is 60 minutes; operations may initiate planned or forced failover sooner under an incident. Before forced failover, capture `replication_lag_sec` and acknowledge possible loss if it exceeds RPO.

From the pinned Azure CLI recovery image:


Validate the exact Azure CLI syntax against the pinned CLI image during every quarterly drill; cloud command surfaces change. The authoritative action is the reviewed Bicep deployment and observed resource state, not copied console steps.

Regional sequence:

1. Incident commander freezes deployments, factory registration, activation, credential rotation, delivery replay, and retention jobs.
2. Determine whether compute, SQL, Front Door, Entra, Key Vault, or an integration is the fault; do not force SQL failover for an API-only incident.
3. Record last known healthy time, SQL replication lag, current release/config/migration, pending activations/outbox, and active firmware cohort.
4. Fail SQL only when required, enable secondary Front Door origins, and leave devices retrying with jitter.
5. Run private and public smoke tests from Section 29.18, then open telemetry first. Open staff reads, staff writes, factory, and activation separately after credential-state reconciliation.
6. Compare canary/device queue replay, activation attempts, credential/audit state, and external tickets. Communicate actual/possible data-loss interval.
7. Keep the former primary isolated until root cause and replication direction are understood. Fail back as a separate approved change after at least 24 healthy hours.

#### Point-in-time restore and logical corruption

Never restore over `sqldb-wfsm-prod`. Restore to a new, timestamped database on an isolated recovery server/subnet:


The recovery application runs with:


Validate database integrity, migration set, FK/check/unique invariants, critical row counts and hashes, newest timestamps, audit continuity, and sampled fleet queries. Determine the missing interval from immutable provisioning/security audit export, factory genealogy, RouteFlex idempotency records, and device queues.

A point-in-time restore can resurrect an unconsumed bootstrap credential, unrevoked operational credential, incomplete session, or old dealer mapping. Before any write route opens:

1. Mark every bootstrap/operational credential changed after restore time as `RecoveryReviewRequired` using non-secret immutable audit deltas. Default-deny uncertain bootstrap/factory credentials.
2. Reapply post-restore revocations/consumptions and dealer/station mapping changes from independently signed/audited events; require two-person review for ambiguous identity state.
3. Cancel or reconcile live commissioning sessions/activation attempt IDs; exactly one installation/calibration/credential may survive.
4. Rotate uncertain operational credentials through a staged device recovery cohort; never restore plaintext because none exists server-side.
5. Reconcile RouteFlex by idempotency key before Worker resumes.
6. Open telemetry writes and let devices replay; verify boot/sequence uniqueness. Open activation/factory/rotation/delivery last.

Promote the recovered database by a reviewed connection/failover-target change, not an ad hoc connection string pasted into one instance. Keep the damaged database read-only under incident retention. Record every repair query/script hash and actor.

#### Key, artifact, and control-plane recovery

- **Key Vault regional issue:** use the same geo-replicated vault URI and Azure-supported failover behavior; do not create a new same-name vault manually. Workloads cache only public verification material and bounded certificate state, never private keys. Signing pauses during vault outage; telemetry continues.
- **Signing-key compromise:** immediately halt firmware/factory/release signing, disable the key version, preserve evidence, and invoke two-person root-of-trust rollover. If device trust cannot revoke the key remotely without accepting compromised signatures, treat affected hardware as a field security incident with recall/reflash decision. Do not raise anti-rollback until the safe recovery image is proven.
- **ACR loss:** deploy only a digest whose signature/SBOM/provenance is verified from the geo-replica or immutable recovery export. Never rebuild the same version from source during an incident.
- **Firmware Blob loss:** restore the exact signed object/version and verify manifest/image hashes; devices stay on current image and retry checks with backoff.
- **GitHub outage:** use the offline signed manifests and preapproved emergency deployment identity/runbook. Every emergency action is later reconciled into Git/IaC; no source build occurs on an administrator laptop.
- **Entra outage:** device telemetry continues with device credentials; new staff/factory/activation operations fail closed. Existing human token lifetime remains bounded; do not introduce a local password bypass. Break-glass cloud control-plane accounts are for recovery, not application impersonation.
- **Front Door/DNS issue:** use the pre-provisioned Front Door recovery configuration and authoritative DNS break-glass process. DNS failover is last resort because firmware caches/TTL behavior lengthens recovery; preserve the frozen sensor hostname and trusted certificate chain.

#### Recovery evidence and acceptance

Each drill/incident stores a signed record with scenario, start/detect/declare/restore/validate times, decision makers, commands/deployment IDs, pre/post release and migration, backup/restore point, measured data loss, reconciliation counts, smoke/alert results, deviations, customer impact, and follow-up owners/dates. A tabletop without executing data restore and application validation does not count as a restore drill.

**Backup/DR acceptance:** from an empty Central US recovery resource group, restore an approved SQL point, deploy current signed API/web digests, recover public configuration/verification keys, pass all credential-safe validation, and serve synthetic telemetry within 60 minutes while losing no more than 5 minutes of committed state. Separately force failover during queued telemetry and an in-progress activation; prove telemetry replay is deduplicated, ambiguous activation/factory credentials remain blocked until reconciled, no RouteFlex duplicate occurs, and failback is a new controlled change. Repeat quarterly and treat a missed objective as a Production reliability defect with an owned remediation date.

### 29.21 Operational readiness prerequisites

No Production resource may receive customer, dealer, factory, or field-device traffic until the service has accountable owners, reachable support, approved data handling, measured capacity, working maintenance, trained field/factory procedures, and completed evidence below. A deployment that is technically green but operationally unowned is a failed launch.

#### Ownership and service record

Register `WaterFlex Salt Monitor` in the approved service catalog/CMDB with:


Every `REPLACE_WATERFLEX_*` owner must be a durable team/queue, not one person's mailbox. Define primary and secondary on-call rotations, manager escalation, security incident bridge, database escalation, factory escalation, Azure support contract ID, DNS registrar emergency contact, Entra tenant emergency contact, WaterFlex directory owner, RouteFlex owner, manufacturer/RMA contact, and communications approver. Test each path before pilot shipment.

#### Service levels and error budget

Reference Production objectives, measured at the public edge and excluding only predeclared maintenance and failures entirely before a valid request reaches WaterFlex:

| SLI | Target | Measurement |
|---|---:|---|
| Device telemetry availability | 99.9% monthly | Valid authenticated telemetry requests returning documented non-5xx/non-timeout response; duplicate is success. Do not exclude WaterFlex SQL/edge/deploy failures. |
| Device telemetry latency | 99% below 1 second monthly | Edge-to-ack duration for batches up to 24, excluding client upload time only where measurable. |
| Synthetic persistence freshness | 99.9% within 2 minutes | Canary reading committed and query-verifiable. |
| Activation availability | 99.5% monthly | Valid, eligible activation attempts complete server transaction without 5xx/timeout. User validation/conflict is not an availability failure. |
| Staff fleet read availability | 99.5% during approved business hours | Authenticated synthetic fleet/detail reads. |
| Factory registration availability | 99.5% during scheduled factory shifts | Valid dual-auth registration/validation transactions. |
| Data durability | RPO <=5 minutes | Measured committed-state loss in failover/restore drill. |
| Service recovery | RTO <=60 minutes | Incident declaration to validated restored service. |

99.9% permits approximately 43 minutes 50 seconds of monthly unavailability in a 30-day month. Publish rolling 7-day/30-day SLO and budget burn. Page on 14.4x burn over 1 hour and 6x burn over 6 hours; ticket on 3x burn over 3 days. Freeze risk-increasing feature releases when 50% of monthly budget is consumed in 7 days or 100% is consumed in the month. Reliability/security fixes may proceed under operations approval. Product and operations must sign any changed target; monitoring and support promises change in the same release.

#### Incident and support model

Use these severity defaults:

| Severity | Example | Acknowledge/command | Communication |
|---|---|---|---|
| Sev 0 | Credential/signing compromise, cross-dealer data exposure, destructive corruption, fleet-wide outage/safety issue | 5 min acknowledgement; incident commander and security lead within 15 min | Status update every 30 min plus legal/privacy process. |
| Sev 1 | Telemetry unavailable/incorrect for material fleet, activation/factory stopped, RPO/RTO threat | 15 min acknowledgement; commander within 30 min | Status update every 60 min. |
| Sev 2 | Degraded latency, localized offline spike, SQL capacity warning, delivery backlog | 1 hour during support coverage | Internal ticket/update every 4 hours. |
| Sev 3 | Single-device/user issue with workaround | 1 business day | Normal support ticket. |

Incident roles: commander, operations lead, application lead, communications lead, scribe, and security/privacy lead when applicable. The person changing Production should not also be incident commander. Preserve logs/audits/artifacts, use UTC, record every mitigation, and do not paste tokens/customer Wi-Fi/addresses into chat or tickets.

Create these additional runbooks:


Every runbook states detection, authority, safety/data cautions, exact commands/queries, rollback/stop conditions, verification, escalation, customer communication, and evidence. Exercise Sev 0 credential/data scenarios and Sev 1 availability/DR scenarios at least twice yearly; track actions to closure.

Support tooling must accept serial number, work order, non-secret trace ID, app/firmware version, timestamps, coarse error code, and consented diagnostic bundle. It must reject/redact device/bootstrap/Entra tokens and Wi-Fi passwords. Support users receive read-only, dealer-scoped, or explicitly elevated audited actions; there is no direct SQL-edit playbook. Recalibrate, replace, rotate credential, recover activation, and retire must be implemented as authorized idempotent APIs with reason, rowversion, audit, and reversal/compensation rules before support promises those actions.

#### Production access and recurring review

- No standing individual Owner, User Access Administrator, SQL administrator, Key Vault officer, DNS administrator, factory override, or Production write role.
- Use Entra groups, PIM just-in-time activation, MFA/phishing-resistant auth, approval, justification/change-or-incident ID, maximum four-hour duration, and alert/audit on activation.
- Maintain two monitored emergency cloud accounts excluded from normal federation/Conditional Access lockout, with hardware-bound credentials in separate custody. Test quarterly and rotate after use.
- Review human/group/service-principal/managed-identity/federated-credential/database/factory-station access quarterly and on owner/dealer/vendor departure. Remove orphaned credentials immediately.
- Disable portal changes through policy where practical. Detect drift daily; emergency changes are captured into Bicep/Git within one business day after stabilization.
- Production data access is audited and purpose-limited. Engineers use synthetic/anonymized data by default; exports require data-owner approval, encryption, expiry, and deletion evidence.

**Access acceptance:** a new on-call engineer can obtain approved read access but no write access; a PIM-approved responder can execute one audited mitigation and loses permission at expiry; a departed dealer/station/operator loses access within the documented token lifetime. Test emergency accounts without using them for application business actions.

#### Maintenance and lifecycle jobs

The current Worker cannot own maintenance. Add `WaterFlex.SaltMonitor.Maintenance` as a command-oriented .NET host and signed image, run as Azure Container Apps Jobs in private environment `cae-wfsm-jobs-prod-eus2`. Each job uses a dedicated managed identity and table-specific database role; jobs are idempotent, SQL-leased where overlap is possible, emit metrics/audit, and fail without blocking telemetry.

| Job resource/command | UTC schedule | Maximum runtime | Required behavior |
|---|---|---:|---|
| `caj-wfsm-session-expiry-prod` / `expire-commissioning-sessions` | `*/1 * * * *` | 50 sec | Expire pending/provisional sessions atomically, revoke provisional credential, restore safe device state, and audit. |
| `caj-wfsm-credential-lifecycle-prod` / `evaluate-credential-lifecycle` | `7 * * * *` | 10 min | Find rotation/expiry/revocation anomalies, notify cohorts/support, never generate plaintext. |
| `caj-wfsm-reporting-snapshot-prod` / `snapshot-reporting-status` | `*/5 * * * *` | 4 min | Produce bounded reporting/offline gauges and alert input without per-device metric labels. |
| `caj-wfsm-telemetry-retention-prod` / `delete-expired-telemetry` | `0 2 * * *` | 5 min | Batched legal-hold-aware deletion per Section 29.10. |
| `caj-wfsm-audit-export-prod` / `export-audit-events` | `17 * * * *` | 20 min | Export signed/checkpointed non-secret audit stream to immutable storage; no gap/duplicate ambiguity. |
| `caj-wfsm-firmware-cohort-prod` / `evaluate-firmware-cohorts` | `37 * * * *` | 10 min | Apply approved cohort manifest, halt thresholds, and no self-enrollment. |
| `caj-wfsm-invariant-scan-prod` / `scan-production-invariants` | `15 3 * * *` | 30 min | Read-only check uniqueness/state/orphan/credential/install/calibration invariants; page before any repair. |

Container Apps Jobs resources, schedules, identities, secrets, private DNS, logs, retry limits, and timeout are Bicep. Set `replicaRetryLimit=1` for idempotent jobs, `parallelism=1` unless the command explicitly partitions work, and `replicaCompletionCount=1`. A failed job alerts; it does not loop without bound. Retention and audit export cannot overlap themselves.

Add settings:


**Maintenance acceptance:** seed 100,000 expired/live/malformed-boundary records in Staging, launch two copies of each job, kill one mid-transaction, and rerun. Exactly the eligible rows change once, audits/checkpoints are complete, legal holds remain, locks expire safely, API latency/SLO remains green, and alerts fire for forced job failure/staleness.

#### Capacity, scale, and quota

Add versioned k6 tests:


Reference pre-production profile:


Run only synthetic identities/data in an isolated performance environment with Production-equivalent SKUs/configuration and representative history/index cardinality. Required thresholds: zero lost/duplicate business rows; telemetry HTTP failure below 0.1% excluding deliberate tests; p95 below 1 second and p99 below 2 seconds; SQL CPU below 70% steady/85% burst; data/log/storage below 70%; no connection-pool exhaustion, deadlock, queue overflow, unbounded memory, restart, or rate-limit punishment of unrelated devices behind shared NAT; fleet p95 below 1 second at page size 100.


Before go-live, obtain Azure quota for primary and secondary App Service workers, SQL vCores/storage, Front Door origins/private links/WAF, private endpoints, ACR tasks/storage, Log Analytics ingestion, Container Apps Jobs, managed identities, and deployment concurrency at twice reference capacity. Configure budgets at 50%, 75%, 90%, and 100% forecast to product/FinOps/operations; never auto-shut down Production telemetry at a budget threshold. Record cost per active device and review monthly.

**Capacity acceptance:** pass the 24-hour mixed soak and five-minute 100 request/second reconnect event on release-candidate artifacts, then intentionally exceed capacity to prove bounded 429/backoff, autoscale, alerting, and recovery without corrupting state. `REPLACE_WATERFLEX_PILOT_FLEET_LIMIT` cannot exceed the tested ceiling; re-test before each 2x fleet or material reporting-frequency increase.

#### Data, privacy, legal, and vendor prerequisites

The database contains customer/account/location/address, installation, device, dealer, audit, and operational telemetry data. Before pilot, the data/privacy/security owners must approve:

- Data inventory/flow diagram, classification, purpose/necessity, controller/processor roles, data residency, privacy notice/contract terms, dealer/manufacturer/support access, and breach process.
- Retention/deletion/legal-hold schedule for telemetry, identity mappings, provisioning audits, factory genealogy, logs, backups, tickets, and support bundles. Backup expiry is part of deletion completion.
- Customer/account closure, tank removal, dealer termination, device replacement/RMA, subject/access request, litigation hold, and anonymized non-production data procedures.
- Encryption/access/audit controls and a prohibition on using customer data for unrelated analytics/model training without separate approval.
- Threat model, penetration test, secure code/firmware review, dependency/license policy, vulnerability intake/disclosure, remediation SLA, and patch policy.

Reference vulnerability remediation: Critical exploited/exposed within 24 hours, other Critical within 72 hours, High within 14 days, Medium within 60 days, Low within 180 days; compensating controls and risk acceptance require security owner plus expiry. Firmware timelines account for signed cohort rollout but do not leave an actively exploited fleet unmitigated.

Execute contracts/SLAs and incident contacts for Azure, GitHub, DNS registrar/host, Entra tenant, WaterFlex directory, RouteFlex if enabled, manufacturer, hardware/module/sensor suppliers, Android app distribution/MDM, label/printer/fixture calibration, security scanning/signing, and support tooling. Confirm license/export/regulatory rights for .NET/NPM/PlatformIO/Espressif/Arduino libraries and redistribution of firmware/toolchains.

**Data/vendor acceptance:** data owner signs the inventory/retention/deletion test; privacy/security approve the pilot; a deletion/legal-hold drill reaches online data, archive, support bundles, and eventual backup expiry; vendor escalation is exercised; no unapproved Production data exists in Development/Staging, logs, CI, factory laptops, or support exports.

#### Field installation, replacement, and support readiness

Publish a versioned installation work instruction and train/certify dealer technicians. It must require: correct work order/customer/location/tank resolved from WaterFlex; serial/label signature scan; approved mount and sensor orientation; measured tank depth 10-450 cm; obstruction/condensation/brine/overflow/power/Wi-Fi safety checks; five stable readings with spread at most 100 mm; initial fill plausibility; setup-app/server identity; and no manual ownership entry trusted from the device.

Reference installation closeout thresholds:


Values are defaults; hardware/product owners must approve replacements. A failed threshold keeps the work order open and device in an explicit attention/quarantine state; technicians cannot click through by editing JSON or SQL. Capture only necessary installation evidence and apply approved retention/access. Never photograph or store customer Wi-Fi passwords.

Implement authorized workflows before launch: recalibrate with calibration version/effective interval; replace device while preserving installation history and revoking old credentials; retire/revoke/lost/stolen; customer Wi-Fi change via physical local setup; RMA quarantine and secure erase; lost setup card; expired credential physical recovery; and dealer transfer. Each workflow has idempotency key, reason code, optimistic concurrency, dual approval where security-sensitive, audit, and automated acceptance test.

Maintain pilot spare devices, sensor modules, mounts, power supplies, setup cards, calibrated fixtures, station/printer parts, and replacement lead times. Reference spare target is greater of 5% of deployed fleet or 20 complete units; `REPLACE_WATERFLEX_SPARE_POLICY` must be approved. Track serial/genealogy and never recycle a retired serial/bootstrap identity.

**Field acceptance:** two certified technicians independently install, replace, recalibrate, change Wi-Fi, recover, and retire synthetic pilot units using only published tools/runbooks. Server invariants, credential revocation, history, audit, first telemetry, and operations visibility remain correct; support can diagnose each injected failure without SQL access or secret collection.

#### Final go-live evidence and sign-off

Create `docs/go-live/production-readiness.md` and link immutable evidence for every item:


Required signatures: business/product, engineering, operations/SRE, security, data/privacy, database, identity/network, factory/firmware/hardware quality, field support, and release/change authority. A signature cannot waive a failed identity, data-integrity, secret, firmware-signing, restore, or cross-dealer isolation test; failed controls require remediation and re-test.

**Operational acceptance:** conduct a 72-hour Production dress rehearsal with synthetic/staff canary plus physical devices and factory/field exercises. Rotate on-call, trigger at least one failure per major runbook, deploy and roll back, run maintenance, restore data, and measure SLO/cost/support handling. Go live only when all evidence is current, every page reaches a trained responder, no unresolved replacement token or critical manual step remains, and the named authorities record a go decision against the signed release manifest.

### 29.22 Empty-environment execution and closure matrix

The current repository is **No-Go for field Production**. It fails at least `PROD-GATE-001` through `PROD-GATE-006`, `PROD-GATE-009`, and `PROD-GATE-010`; the remaining gates also require explicit evidence. The sequence below describes the future implementation after Section 28 exact-reconstruction artifacts are preserved and Section 29 is formally adopted.

#### Fail-closed feature configuration

Add these strongly typed settings. Every value defaults `false` in Production and must fail startup when enabled without its required authentication, adapter, route, migration, job, monitoring, and smoke evidence:


Dependency gates:

| Feature | Cannot become true until |
|---|---|
| `StaffOperationsEnabled` | Entra employee policy, Production routes/UI, real directory, SQL authorization mapping, browser tests, and audit are green. |
| `DealerProvisioningEnabled` | Dealer role/group mapping, cross-dealer tests, real directory, Android setup/session flow, field/support procedures, and audits are green. |
| `FactoryRegistrationEnabled` | Dual operator/station identity, named network, factory CLI/state machine, secret injection, genealogy/label/test fixture, and station revocation are green. |
| `BootstrapActivationEnabled` | Bootstrap scheme, idempotent activation/expiry/first telemetry, firmware encrypted persistence, physical power-loss matrix, and recovery job are green. |
| `CredentialRotationEnabled` | Device/server two-phase rotation, overlap/expiry/recovery, maintenance scan, and fleet canary are green. |
| `FirmwareOtaEnabled` | HSM key ceremony, secure-boot feasibility, signed A/B image/manifest, immutable firmware origin, cohort service, rollback, and HIL are green. |
| `DeliveryAutomationEnabled` | Outbox migration/Worker/RouteFlex contract/policy/idempotency/replay and operations acceptance in Section 29.15 are green. |
| `MaintenanceJobsEnabled` | Signed maintenance image, private Container Apps Jobs, dedicated roles, concurrency/idempotency tests, and alerts are green. |
| `SyntheticVerificationEnabled` | Synthetic Entra/device identities, isolated data, secret rotation, metrics exclusion, and alert verification are green. |

A telemetry-only infrastructure smoke environment may keep all flags false and use only synthetic pre-created Active credentials. It is not a field pilot and cannot create or onboard a real device.

#### One-time control-plane bootstrap

Before running any command, WaterFlex must supply a tenant, dedicated subscriptions, billing, approved regions, authoritative DNS access, protected GitHub repository/organization, PIM groups, recovery subscription, support contracts, and every Section 29.3 decision. No script can safely invent those prerequisites.

Add:


Run the bootstrap from a WaterFlex-managed PIM workstation. It requires temporary tenant/subscription/repository administration, emits no secret, and records every created object ID and role assignment:


Review the Entra What-If before running it without `-WhatIf`. Federated credentials use audience `api://AzureADTokenExchange` and exact subjects:


Do not use wildcard repository/branch/environment subjects. GitHub variables are non-secret `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, environment-specific `AZURE_CLIENT_ID`, `AZURE_PRIMARY_LOCATION`, `AZURE_SECONDARY_LOCATION`, and `WFSM_NAME_SUFFIX`. No Azure client secret, publish profile, SQL password, PFX, device token, or signing key is a GitHub secret.

Bootstrap role separation:

| Principal | Scope and permission ceiling |
|---|---|
| `id-wfsm-gh-build` | Push candidate artifacts/referrers to designated ACR repositories and request approved candidate signing; no App Service, SQL, Key Vault secret, DNS, role-assignment, or Production deployment access. |
| `id-wfsm-gh-stg-deploy` | Contributor through custom role only on `rg-wfsm-stg-*`; no Production scope. |
| `id-wfsm-gh-prod-deploy` | Update approved Bicep resource types, App Service slots, Front Door weights, monitoring, and tags in `rg-wfsm-prod-*`; cannot read secrets, sign, grant arbitrary roles, delete locked state, or access SQL data. |
| `id-wfsm-gh-prod-migrate` | Start/read/delete the named private migration container group and use `id-wfsm-migrate-prod`; no app/edge/DNS/signing permission. |
| Entra synchronization principal | Own/manage only the WaterFlex Salt Monitor app registrations/service principals/app-role definitions; no user/group creation, directory-wide read, credential export, or Azure subscription role. |
| One-time bootstrap operator | PIM-limited creation of custom roles/federation/policies/locks; access removed after verification and retained only through the break-glass process. |

Custom role definitions and federated credentials are IaC/manifest-controlled, reviewed by platform security, and checked for privilege expansion in What-If. Where an Azure role assignment must be bootstrapped before CI can deploy itself, the PIM operator applies that one assignment and archives the signed output; CI never grants itself broader access.

#### Clean deployment order

1. Validate every production decision and replacement evidence; approve architecture, threat/data/privacy/hardware design, SLO/RPO/RTO, capacity, vendors, and support ownership.
2. Bootstrap provider registrations, OIDC principals/custom roles, GitHub rules/environments, Entra applications/roles, DNS delegation, PIM groups, and policy assignments.
3. Execute the HSM/key ceremony for release/firmware/label signing; record only public key IDs/versions in deployable configuration.
4. Run subscription Bicep validate and What-If, approve, and create shared/Production/recovery resources, private DNS/endpoints, warm secondary, logs/alerts, locks, tags, quotas, and budgets.
5. Validate public-network denial and private connectivity before creating application database users. PIM-activate the SQL admin group and run `Initialize-DatabasePrincipals.sql` for managed identities.
6. Complete all missing code/migrations/UI/Android/firmware/factory/maintenance/Worker-when-enabled work and its unit, integration, browser, HIL, load, security, and fault tests.
7. Produce one signed candidate manifest through `container-build.yml`; never build on a deployment runner.
8. Deploy exact digests to Staging, migrate, execute all smokes/security/load/HIL/physical canary/alert/restore/rollback tests, and soak for at least 24 hours.
9. Create DNS validation records and Front Door custom domains/certificates. Keep field/factory features false until external TLS, WAF, identity, and route-isolation acceptance passes.
10. Promote the same manifest by digest through `promote-production.yml`, apply the reviewed expand migration, warm private slots, and execute progressive delivery from Section 29.18.
11. Enable each feature flag separately only after its dependency gate and smoke pass. Enable telemetry before staff writes, then staff reads/writes, dealer, factory, activation, rotation, maintenance, OTA, and delivery last.
12. Complete 60-minute observation, physical canary, backup, release evidence, on-call handoff, and signed go decision. Continue the 72-hour dress-rehearsal/launch watch.

Workflow dispatch examples:


The workflow obtains artifact and configuration values from the signed manifest and resolved decision registry; command-line dispatch must not accept alternate image digests, migration IDs, hostnames, or secret values.

#### Requested-category closure matrix

| Category | Current blocking fact | Exact proposed default/configuration | Acceptance authority |
|---|---|---|---|
| Hosting | Only local Kestrel/Vite; no Production host/IaC | Azure Front Door Premium; private App Service API/web with warm Central US standby; resources/tags/Bicep in Sections 29.2-29.6 | Hosting and clean-subscription tests in Sections 29.5-29.6 |
| DNS | No zone, names, records, TTL, or owner | `saltmonitor`, `sensor-api.saltmonitor`, `factory-api.saltmonitor`, `firmware.saltmonitor` CNAMEs; validation TXT; TTL 300 then 3,600 | External resolution and wrong-origin checks in Section 29.7 |
| TLS | Local HTTP; no public certificate or renewal | Front Door managed certs, TLS >=1.2, 308 redirect, HSTS soak, CA rollover without leaf pinning | External chain/hostname/renewal tests in Sections 29.7 and 29.19 |
| SQL | LocalDB fallback; no Production SKU/auth/network/backup/roles | Azure SQL GP Gen5 2 vCore baseline, private failover group, Entra-only managed identities, named roles/settings | Least-privilege, encrypted connection, migration, PITR/failover tests in Sections 29.10, 29.13, 29.20 |
| Identity | Production staff/factory routes absent; headers are insecure | Entra SPA/API app roles, dealer group table, dual factory operator/station tokens, Conditional Access, synthetic-only role | Issuer/audience/tenant/role/dealer/station/revocation tests in Section 29.11 |
| Secrets/keys | Environment variables only; no vault/custody/rotation | Key Vault Premium private RBAC, managed identity/OIDC, HSM signing, station certs, device hash-only protocol, redaction | Role-separation, rotation, synthetic leak tests in Section 29.12 |
| Networking | No VNet/private endpoints/firewall/egress/WAF | `10.42.0.0/16` primary, `10.43.0.0/16` secondary, named subnets/zones/private endpoints, allowlisted egress, WAF | Public-denial, route-isolation, NAT/rate/WAF tests in Section 29.8 |
| Reverse proxy | No trusted forwarded-header or host/path contract | Front Door host/path allowlist; `ReverseProxy__ForwardLimit=1`; known proxy/hosts; same-origin API | Spoofed-forwarded-header and wrong-host checks in Section 29.8 |
| Static hosting | Vite build only; no SPA fallback/cache/security headers | Unprivileged NGINX image, `/assets` immutable, `index.html` no-cache, fallback only for non-assets, Front Door CSP | Deep-link, asset 404, cache, CSP and `/api` isolation tests in Section 29.9 |
| Migrations | Manual local EF updates; no gate/identity/concurrency/rollback | Signed Linux EF bundle, idempotent review SQL, private one-shot ACI, migration managed identity, expand/contract | Concurrent/kill/replay/scale rehearsal in Section 29.13 |
| Containers | No Dockerfile/registry/SBOM/signing | Digest-pinned SDK/runtime/Node/NGINX, non-root/read-only contract, ACR Premium, signed OCI/SBOM/provenance | Rebuild, runtime-hardening, tamper/admission tests in Section 29.14 |
| CI/CD | No workflows, locks, scans, approvals, deploy target | GitHub Actions OIDC; named workflows/checks/environments; locked restore; immutable manifest/digest promotion | Fork/unsigned/missing-evidence/wrong-OIDC/idempotency tests in Section 29.16 |
| Monitoring | Liveness and default logs only | OTLP/OpenTelemetry config, stable meters/events, six workbooks, regional synthetics, named alerts/Action Groups | Failure injection, receiver/runbook, trace/redaction tests in Section 29.17 |
| Backup | No declared retention/restore drill/RPO/RTO | SQL PITR 35 days plus LTR/failover, immutable release/key/config recovery, warm standby, quarterly drills | Empty-region restore and credential-safe reconciliation in Section 29.20 |
| Rollback | Narrative only; no thresholds or tested action | Front Door weighted current/candidate origins, prior signed digests, <5 minute app rollback, forward-only DB repair | Trigger injection, prior compatibility, replay/reconcile game day in Section 29.18 |
| Release | No version/manifest/approvals/canary/evidence | Signed SemVer compatibility manifest, Staging soak, 1/10/50/100 traffic, objective stop gates, release record | Preflight, smoke IDs, holds, observation and signed closeout in Section 29.18 |
| Firmware factory | UART print sketch only; no field networking/security/factory tooling | Pinned PlatformIO, encrypted durable queue, hash-only activation/rotation, signed A/B OTA, dual-auth factory CLI, labels/genealogy/HIL | 100-unit/two-station pilot, power/queue/key/CA/OTA/quarantine matrix in Section 29.19 |
| Operational | No on-call/SLO/jobs/support/privacy/capacity/field runbooks | Named owners, SLO/error budget, PIM, Container Apps maintenance jobs, k6 profile, field/RMA/data/vendor gates | 72-hour dress rehearsal and complete go-live evidence in Section 29.21 |
| WaterFlex/RouteFlex integrations | Fixtures/stub; URLs/scopes/contracts/SLA unknown | Managed-identity adapters using `WaterFlexDirectory__*` and `RouteFlex__*`; delivery remains disabled unless outbox contract is complete | Staging contract/idempotency/outage tests in Sections 29.12 and 29.15 |

**Closure acceptance:** a reviewer must be able to select any row, locate one exact resource/configuration/policy and one executable or witnessed acceptance test, and trace its evidence into the signed release record. If a category has no owner-supplied value, implemented artifact, or passing evidence, its feature remains false and the field-pilot release is No-Go.
