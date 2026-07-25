# Plan B: WaterFlex Tuya Ultrasonic Sensor Integration

## Summary

Use the prebuilt HojellyTek 03GW Tuya Wi-Fi ultrasonic sensor and build the WaterFlex software around it.
Culligan dealer installers use a private, WaterFlex-branded cross-platform installer app built with Tuya's
Smart Life App SDK. The sensor reports through Tuya Cloud; Tuya's message service sends data-point events to
a multi-tenant .NET backend. WaterFlex calculates tank status and creates one WaterFlex/RouteFlex delivery
ticket when salt remains below 35%.

This is the active pilot path because it avoids custom sensor firmware, PCB, enclosure, certification, and
manufacturing. Production approval depends on proving the device's data points, reporting cadence, brine-tank
accuracy, Tuya account scaling, commercial terms, supply continuity, and reliability.

## Product boundaries

### In scope

- Technical and commercial validation of the selected prebuilt Tuya sensor.
- Tuya Cloud Development project and production service configuration.
- Private cross-platform iOS/Android installer application using Tuya Smart Life App SDK.
- `Link My App` device association and scalable installer/device ownership model.
- Tuya Pulsar/message ingestion and status-API reconciliation.
- Multi-tenant .NET backend and SQL Server persistence.
- Waterline-aware fill calculation where raw distance permits it, smoothing, device health, and low-salt rules.
- WaterFlex/RouteFlex delivery-ticket integration.
- Minimal React operations console for WaterFlex staff.
- Dealer installation, pilot, support, and nationwide rollout procedures.

### Out of scope

- Consumer application or homeowner Tuya account.
- New Culligan dealer portal.
- Subscription billing.
- Custom sensor firmware, PCB, enclosure, radio certification, or manufacturing.
- Route optimization or replacement of WaterFlex customer/account systems.

## Locked decisions

- Selected provisional sensor: HojellyTek 03GW, Amazon ASIN `B0GKV59XM7`.
- Observed product characteristics: approximately $42, 2.4 GHz Wi-Fi, corded 5V, 0.1-3.0 m range, IP67
  ultrasonic probe on a 3 m cable, and Smart Life/Tuya Smart compatibility.
- The device is provisional and cannot be approved for production solely from its listing or limited reviews.
- Tuya app solution type: **Smart Life**.
- Production device link method: **Link My App**.
- App: one custom cross-platform iOS/Android installer codebase, not a consumer application.
- Framework is selected after an iOS/Android SDK spike; React Native is recommended because the broader
  product uses React/TypeScript, but Flutter remains acceptable if its Tuya integration proves stronger.
- Backend: .NET/C#, SQL Server, WaterFlex-hosted on-premises deployment.
- Primary ingestion: Tuya Message Service/Pulsar; status/shadow API is fallback and reconciliation.
- Ticket trigger: sustained fill below 35%, with deduplication, open-ticket protection, and cooldown.
- WaterFlex endpoints are deferred; implement `IDeliveryTicketGateway` and a stub first.

## Target architecture

1. Dealer installer signs into the WaterFlex-branded installer app.
2. The app identifies the WaterFlex dealer and customer selected for installation.
3. Tuya Smart Life App SDK pairs the 03GW to the customer's 2.4 GHz Wi-Fi.
4. The device is owned within the WaterFlex Tuya app ecosystem and linked to the Tuya cloud project through
   `Link My App`.
5. The app captures Tuya `device_id`, saves the WaterFlex customer mapping, performs supported calibration,
   and verifies the first reading.
6. Tuya Cloud receives device data-point updates.
7. Tuya Pulsar/message service pushes events to an outbound-connected .NET ingestion worker on WaterFlex
   infrastructure; signed status API calls reconcile missed or stale events.
8. A Tuya adapter normalizes the device data into the source-independent reading contract.
9. SQL Server stores devices, mappings, calibration, readings, health, rules state, tickets, and audit.
10. The rules engine evaluates sustained low salt and creates an idempotent WaterFlex/RouteFlex ticket.
11. Existing WaterFlex interfaces serve dealers; a small React console serves WaterFlex support/operations.

## Deliverables

### 1. Tuya technical and commercial due-diligence package

- Written device/product identification and proof that units from the intended supplier use the same Tuya
  product/category and data-point schema.
- Complete data-point inventory: codes, IDs, types, units, scale factors, permissions, ranges, and update rules.
- Verified raw distance or documented source percentage, calibration commands, alarm settings, online state,
  timestamp behavior, firmware version, and any device-reported tank parameters.
- Measured report cadence, on-change behavior, keepalive/online behavior, and cloud/event latency.
- Tuya quote and contract covering production tier, maximum monitored/controlled devices, monthly API/message
  allowance, event retention, logs, regions, SLA/support, renewal, overage, and export/termination behavior.
- Manufacturer/distributor documentation covering supply continuity, warranty, certifications, resale rights,
  product-change notification, firmware ownership/update policy, and volume price.
- Decision record showing whether the 03GW is production-worthy or only suitable for pilot use.

### 2. Tuya Cloud Development configuration

- Production and non-production cloud projects in the correct US data center.
- Enabled IoT Core/device APIs and Message Service/Pulsar subscriptions.
- Smart Life App SDK app registration with final iOS Bundle ID and Android package name/signing fingerprint.
- `Link My App` configured and verified.
- AppKey/AppSecret and cloud Access ID/Secret stored in an approved secrets system; never committed to source.
- Least-privilege service access, credential rotation, environment separation, audit, quota alerts, and billing
  renewal alerts.
- Documented regional behavior so app users, devices, and cloud projects cannot be accidentally split across
  incompatible data centers.

### 3. Cross-platform dealer installer app

- WaterFlex branding and private distribution for iOS and Android.
- WaterFlex/dealer authentication and authorization; avoid exposing generic homeowner account concepts.
- Customer selection/search through WaterFlex's existing APIs when available; stub fixture during development.
- Tuya Smart Life App SDK initialization, user identity, Home/space selection, and device pairing.
- EZ/AP/BLE pairing support required by the actual 03GW product; use Tuya UI BizBundles where supported and
  native bridges where cross-platform packages are incomplete.
- 2.4 GHz Wi-Fi guidance, permission handling, location/Bluetooth/local-network permissions as required by OS,
  retries, timeout, reset instructions, and actionable error messages.
- Device ID capture and atomic device-to-WaterFlex-customer association.
- Calibration/tank setup UI only for controls actually exposed by the device.
- Commissioning check: online state, latest data point, mapping, and successful backend acknowledgement.
- Sensor replacement, transfer, unpair, retry, and secure logout.
- App telemetry and privacy-safe diagnostic export for support.
- No tank dashboard, ticket management, customer notifications, billing, or homeowner functionality.

### 4. App distribution and lifecycle

- Android private distribution through Managed Google Play or controlled signed APK distribution, based on
  dealer device-management capabilities.
- iOS private distribution through Apple Business Manager Custom Apps or another approved private/unlisted
  route; TestFlight is development/pilot only because builds expire.
- Apple and Google organization developer accounts, signing keys, certificates, provisioning profiles,
  release roles, and recovery process.
- Mobile CI/CD for signed development, staging, and production builds.
- OS and Tuya SDK compatibility policy, dependency monitoring, upgrade cadence, and end-of-support rules.
- Dealer app-installation instructions and minimum supported iOS/Android versions.

### 5. Tuya account, Home/space, and tenant model

- Homeowners must not own production sensors in personal Smart Life accounts.
- Tuya app users represent authorized dealer installers or controlled service identities.
- WaterFlex remains authoritative for dealer/customer ownership; Tuya identifiers are integration metadata.
- Persist Tuya `device_id`, product/category, app user/UID where needed, Home/space ID, WaterFlex dealer ID,
  WaterFlex customer ID, install timestamp, and installer audit.
- Validate documented and observed limits for users, Homes/spaces, members, devices per Home, app accounts,
  and API visibility before fixing topology.
- Starting hypothesis: one Tuya Home/space per dealer branch with installers as members and WaterFlex holding
  customer-level mapping. Split by customer/site if Tuya capacity, permissions, or transfer behavior demands it.
- Never use one shared national username/password across installers.
- Define installer onboarding/offboarding, device reassignment, dealer sale/closure, and employee departure.

### 6. Tuya ingestion adapter

- .NET background service connecting to Tuya Message Service/Pulsar from WaterFlex infrastructure.
- Secure connection and credential handling, consumer group/subscription configuration, acknowledgement,
  reconnect/backoff, checkpointing, duplicate and out-of-order handling, poison-message isolation, and metrics.
- Parse Tuya event envelope and product-specific data points without leaking Tuya details into the domain layer.
- `ITelemetrySourceAdapter` maps event data to normalized device ID, source timestamp, received timestamp, raw
  distance or source percentage, online state, signal/health fields, and raw source metadata.
- Signed Tuya access-token manager and status/shadow API client for first sync, reconciliation, support, and
  stale-device investigation.
- Respect endpoint frequency limits; use events rather than fleet polling for normal operation.
- Quota, throttling, token-expiry, cloud-latency, and subscription-health alerts.
- Persist raw Tuya payloads for a bounded diagnostic period and normalized records for product retention.

### 7. Shared WaterFlex backend

- .NET solution separated into API, Domain, Ingestion, Rules, Infrastructure, and Worker components.
- SQL Server schema for Dealer/Tenant, Device, DeviceCustomerMapping, Calibration, SensorReading,
  TriggerState, DeliveryTicket, OutboxEvent, DeviceHealth, SourceIntegration, and AuditEvent.
- Tenant isolation on every query and operation.
- Device registry lifecycle: paired, commissioning, active, stale, offline, replacement, retired, and revoked.
- REST/internal APIs required by WaterFlex interfaces, the installer app, and the operations console.
- Source abstraction so Plan A hardware can replace Tuya without changing fill or ticketing logic.

### 8. Fill percentage and signal-quality service

- If raw distance is exposed, use
  `fillPct = clamp((emptyDistance - measuredDistance) / (emptyDistance - fullDistance) * 100, 0, 100)`.
- Calibrate empty at the normal standing-water surface so a low tank triggers before salt disappears below
  water and becomes invisible to the sensor.
- If only a Tuya-calculated percentage is exposed, document device calibration and determine whether its result
  is sufficiently accurate and auditable. Failure to obtain acceptable raw or calibrated data blocks production.
- Median/robust smoothing, minimum sample count, rate-of-change checks, invalid range rejection, regeneration
  suppression, stale-data detection, and source-confidence flags.
- Preserve raw event, normalized value, calculation version, and calibration version for audit/reprocessing.

### 9. Ticket automation

- Trigger only after fill remains below 35% for the configured sample/time window.
- Require an active device, valid customer mapping, recent reading, and acceptable confidence.
- Suppress creation when an open ticket exists or the customer is in post-delivery cooldown.
- Generate an idempotency key from tenant, customer/device, and depletion cycle.
- Write ticket commands to a transactional outbox and retry transient failures with dead-letter handling.
- `IDeliveryTicketGateway` request contains WaterFlex account/customer reference, device, fill percentage,
  threshold, source timestamp, salt product/quantity if known, and idempotency key.
- Use a stub until WaterFlex endpoints arrive; then add fulfilled/cancelled status synchronization if supported.

### 10. WaterFlex internal operations console

- WaterFlex staff authentication and role-based authorization.
- Fleet by dealer: paired, commissioning, online, stale, offline, unmapped, and failed.
- Latest Tuya event/API reconciliation, source percentage/raw distance, calculated fill, and calibration state.
- Pairing/install diagnostics, device-to-customer mapping, replacement, unpairing, and retirement.
- Tuya subscription/consumer health, quota, token, rate-limit, and event-lag status.
- Reading/calculation history, ticket/outbox failures, and controlled retry.
- Tenant-aware support access and complete audit history.
- No device firmware/OTA control unless the supplier/Tuya product explicitly exposes and supports it.

### 11. Dealer and support documentation

- Private installer-app installation and login guide.
- Physical mounting, 2.4 GHz Wi-Fi, reset/pair, calibration, customer association, and commissioning checklist.
- Troubleshooting for pairing modes, permission issues, wrong data center/account, weak Wi-Fi, stale data,
  replacement, and unpairing.
- WaterFlex support runbook for Tuya status, mapping, Pulsar, API reconciliation, and ticket failures.
- Supplier escalation, Tuya escalation, outage communication, and device replacement procedures.

## Implementation process

### Phase B0: Device and Tuya proof of concept

1. Purchase 3-5 03GW units, preferably covering multiple lots or suppliers.
2. Pair one unit with Smart Life only for discovery and inspect Tuya Device Debugging/data points.
3. Record all data points, scale factors, controls, report cadence, and online behavior.
4. Confirm raw distance or prove the vendor percentage/calibration is acceptable.
5. Create US development cloud project and Smart Life App SDK app.
6. Configure **Link My App**, Message Service/Pulsar, and test credentials.
7. Run a short Flutter-versus-React-Native/native-bridge spike on both iOS and Android; select one framework.
8. Pair a real device through the custom app and prove its `device_id` is visible to the cloud project.
9. Receive one Pulsar event in a throwaway .NET consumer and reconcile it through the status API.
10. Bench-test full, 35%, waterline, and empty conditions with salt pellets/crystals.
11. Test water film, coning, bridging, regeneration, probe angle, wall echoes, and mounting variation.
12. Run a multi-day humidity, weak-Wi-Fi, router-restart, power-cycle, and event-reconnect soak.
13. Obtain Tuya and supplier commercial terms and verify Home/space/member/device limits.

**Exit gate:** the custom app pairs the sensor on iOS and Android; the project receives sufficiently granular
and timely data; the physical sensor meets preliminary accuracy/reliability expectations; Tuya's scaling and
commercial model has no unresolved blocker.

### Phase B1: Shared foundation and source contracts

1. Create the .NET solution and SQL schema.
2. Implement tenant scope, device/customer mapping, normalized reading, calibration, health, ticket, outbox,
   and audit models.
3. Define `ITelemetrySourceAdapter` and `IDeliveryTicketGateway`.
4. Build a production-shaped Tuya event fixture adapter and WaterFlex ticket stub.
5. Create installer-app shell, WaterFlex authentication/customer-selection contracts, and mobile CI/CD.
6. Start the staff-only React operations console.

**Dependencies:** app, Tuya ingestion, and shared backend work can proceed in parallel after identity, mapping,
and normalized telemetry contracts are agreed.

### Phase B2: Production installer app and Tuya ingestion

1. Implement installer authentication, dealer/customer selection, pairing, device ID capture, mapping,
   calibration, commissioning verification, replacement, and diagnostics.
2. Implement Pulsar consumer reliability, raw event capture, DP normalization, token management, status API
   reconciliation, quotas, alerts, and dead-lettering.
3. Finalize Tuya Home/space topology and installer lifecycle based on validated limits.
4. Configure private iOS/Android signing and distribution.
5. Test all supported phone OS versions and actual dealer installation conditions.

### Phase B3: Level processing and ticket automation

1. Implement raw-distance or approved source-percentage calculation path.
2. Add waterline calibration, smoothing, confidence, regeneration filtering, and stale/offline detection.
3. Implement threshold/debounce, open-ticket suppression, cooldown, idempotency, and transactional outbox.
4. Prove one physical low-tank sequence creates exactly one stub ticket.
5. Complete required fleet, reading, mapping, and ticket-failure operations views.

### Phase B4: WaterFlex integration

1. Replace the ticket stub after WaterFlex endpoints and sandbox credentials arrive.
2. Connect installer customer search/mapping to WaterFlex.
3. Expose approved device level and health data through existing WaterFlex interfaces.
4. Add fulfilled/cancelled ticket status handling where supported.
5. Run end-to-end sandbox tests with duplicate, failure, retry, and replacement scenarios.

### Phase B5: Culligan dealer pilot

1. Select one dealer and 10-20 customers covering representative tanks, homes, installers, and Wi-Fi.
2. Train installers and observe first installations.
3. Measure app installation/pairing success, time on site, mapping errors, offline rate, event latency,
   false-low/false-high rate, ticket precision, support calls, and replacement rate.
4. Run at least one real or controlled low-salt cycle per representative tank family.
5. Review Tuya message/API consumption and extrapolate paid capacity/cost.
6. Resolve critical defects and repeat acceptance tests.

### Phase B6: Production approval and national rollout

1. Approve Plan B only after device reliability, raw/accepted data, cadence, Tuya terms, topology, private app
   distribution, supplier continuity, certifications, and pilot metrics pass.
2. Roll out by Culligan dealer cohort with enrollment limits and operational monitoring.
3. Maintain a spare/replacement process and supplier/product-change watch.
4. Monitor Tuya event lag, quotas, renewals, API changes, device schema drift, app SDK/OS changes, offline rate,
   ticket outcomes, and support incidents.
5. Keep the source-independent backend contract so migration to Plan A remains feasible.

## Verification

### Automated

- Unit tests for DP parsing/scale, normalization, fill math, waterline calibration, smoothing, confidence,
  threshold/debounce, idempotency, cooldown, tenant isolation, and ticket contracts.
- Mobile tests for authentication, permissions, pairing state, retries, mapping, calibration, replacement,
  logout, and interrupted commissioning.
- Integration test: recorded Tuya event -> Pulsar consumer -> normalized reading -> SQL -> rules -> one stub
  ticket.
- Reconciliation tests for token expiry, missed events, duplicate/out-of-order events, API throttling, stale
  state, and Tuya outage.
- Security tests for invalid app/service credentials, unauthorized installer/dealer, tenant crossing, mapping
  tampering, malicious payloads, secret rotation, and lost installer device.
- Load test at 2,000 devices plus reconnect/event bursts and projected growth.

### Physical/manual

- Representative tanks, lids, salt pellets/crystals, fill levels, waterline heights, and mounting locations.
- Thin water cover, coning, bridging, regeneration, foam, turbulence, angled surfaces, and wall echoes.
- Condensation, probe fouling, temperature, weak Wi-Fi, router replacement, internet loss, power cycles, and
  delayed Tuya events.
- Multiple iOS/Android phones, app upgrades, permissions denied/re-enabled, private distribution, and installer
  account changes.
- Sensor install, calibration, replacement, unpairing, reassignment, and decommissioning.

## Success criteria

- The selected sensor produces sufficiently granular and timely data for reliable low-salt decisions.
- The custom installer app pairs and commissions devices reliably on supported iOS and Android versions.
- Devices remain visible to the WaterFlex Tuya project without homeowner account dependency.
- Tuya Home/space and installer identity model supports nationwide multi-dealer operation.
- Tuya and supplier costs/terms are acceptable and documented before production commitment.
- A sustained low tank creates one and only one WaterFlex delivery ticket.
- Tenant boundaries prevent one dealer from accessing another dealer's data.
- Pilot false-ticket, missed-ticket, pairing, offline, replacement, and support rates meet agreed targets.

## Primary risks and mitigations

- **No raw distance:** make this a Phase B0 gate; reject the product if its percentage cannot meet accuracy and
  audit requirements.
- **Consumer-grade reliability:** test multiple lots, run soak/pilot tests, negotiate supplier controls, and
  retain Plan A as fallback.
- **Tuya dependency:** written commercial terms, event-first architecture, reconciliation, renewal/quota alerts,
  exportable normalized data, source adapter boundary, and migration plan.
- **Account/Home scaling:** validate limits and transfer workflows before topology lock; never use one shared
  national credential or homeowner accounts.
- **Cross-platform SDK gaps:** perform the pairing spike first, use native bridges/BizBundles, and restrict app
  scope to installation.
- **Private distribution complexity:** establish organization developer accounts and dealer-device management
  model early; do not rely on TestFlight for production.
- **Product/schema drift:** record product IDs and DP schemas, alert on unknown data points, regression-test
  firmware/product changes, and require supplier notice.
- **Wi-Fi installation failures:** clear 2.4 GHz workflow, pairing-mode guidance, retries, diagnostics, and
  installer training.

## Deferred inputs

- WaterFlex authentication, customer lookup, device mapping, delivery creation, and ticket-status endpoints.
- Final decision between React Native and Flutter after the pairing spike.
- Confirmed Tuya Home/space topology and documented limits.
- Written Tuya production quote and supplier commercial/certification documentation.
- Agreed pilot acceptance thresholds for accuracy, availability, installation time, and support burden.
