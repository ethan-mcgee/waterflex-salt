# Plan A: WaterFlex OEM/White-Label Integrated Salt Sensor

## Summary

Select an OEM/ODM partner that already manufactures an integrated, corded Wi-Fi ultrasonic level sensor and
white-label it for WaterFlex. The supplier provides the production PCB, enclosure, power design, radio module,
assembly, and certifications; WaterFlex defines the product requirements and owns or contractually controls
the custom firmware, cloud protocol, device identity, calibration behavior, and software integration.

Culligan dealers install and provision each sensor, associate it with an existing WaterFlex customer, and then
operate entirely through WaterFlex. The sensor reports raw distance and health telemetry directly to
WaterFlex's on-premises endpoint. A multi-tenant .NET backend calculates fill percentage and automatically
creates one WaterFlex/RouteFlex delivery ticket after the tank remains below 35%.

This plan avoids developing a production PCB and enclosure from scratch while retaining first-party data
control. It still requires OEM selection, firmware customization, contractual protections, product validation,
compliance review, manufacturing quality controls, and supply-chain management.

## Product boundaries

### In scope

- OEM/ODM discovery, RFQ, technical due diligence, commercial negotiation, and supplier qualification.
- White-label integrated sensor, enclosure, packaging, labels, and installation accessories.
- WaterFlex-controlled firmware requirements, Wi-Fi provisioning, calibration, telemetry, security, and OTA.
- Secure MQTT ingestion into WaterFlex infrastructure.
- Multi-tenant .NET backend and SQL Server persistence.
- Waterline-aware fill calculation, smoothing, device health, and low-salt rules.
- WaterFlex/RouteFlex delivery-ticket integration.
- Minimal React operations console for WaterFlex staff.
- Dealer installation and support procedures.
- Supplier sample validation, regulatory review, factory acceptance test, pilot, and production rollout.

### Out of scope

- Consumer application.
- New Culligan dealer portal.
- Subscription billing.
- Arduino development boards, carrier boards, and hand-built field assemblies; those are covered by Plan C.
- WaterFlex-designed production PCB and enclosure unless the selected OEM cannot meet requirements.
- Route optimization or driver workflow replacement.
- Duplication of WaterFlex customer, address, account, and product master data.

## Locked decisions

- WaterFlex is the only target platform and system of record.
- Culligan dealers install sensors and onboard their own customers.
- Hardware architecture and exact components are selected through the OEM process, not predetermined by an
  Arduino development board or a specific carrier.
- Preferred radio/MCU class: a pre-certified ESP32-family or equivalent 2.4 GHz Wi-Fi/BLE module that supports
  custom firmware, secure boot, encrypted storage, TLS, and signed OTA.
- Preferred sensing method: top-down, non-contact ultrasonic with raw distance output, a short blind zone,
  temperature compensation, and a sealed corrosion-resistant transducer.
- Sensor power: corded 5V; no battery requirement.
- Production target: below $50 per assembled sensor where volume and validation support it.
- Backend: .NET/C#, SQL Server, WaterFlex-hosted on-premises deployment.
- Device transport: MQTT over TLS with per-device authentication.
- Ticket trigger: sustained fill below 35%, with deduplication, open-ticket protection, and cooldown.
- WaterFlex endpoints are deferred; implement `IDeliveryTicketGateway` and a test stub first.

## Target architecture

1. The OEM ultrasonic transducer measures the distance from the tank lid to the first reflecting surface.
2. WaterFlex-controlled firmware validates and aggregates samples, then publishes raw distance and
  device-health telemetry.
3. The sensor connects through the customer's 2.4 GHz Wi-Fi to WaterFlex's MQTT endpoint over TLS.
4. A .NET ingestion worker authenticates the device, deduplicates messages, and emits a normalized reading.
5. SQL Server stores devices, customer mappings, calibration, readings, health, rules state, tickets, and audit.
6. The level service computes fill percentage using each tank's full and standing-water-line distances.
7. The rules engine evaluates the sustained 35% threshold and creates an idempotent outbox command.
8. A worker calls WaterFlex/RouteFlex through `IDeliveryTicketGateway`.
9. Existing WaterFlex interfaces expose dealer/customer results; the new React console is staff-only.

## Deliverables

### 1. Product and hardware requirements package

- Supported tank dimensions, lid types, mounting orientation, probe clearance, and acceptable installation
  tolerances.
- Required measurement range, blind zone, accuracy, repeatability, operating temperature, humidity,
  condensation, salt exposure, and Wi-Fi performance.
- Electrical, mechanical, environmental, labeling, serviceability, and expected-life requirements.
- Target unit cost, production volumes, warranty assumptions, and replacement procedure.
- Supplier and alternate-component strategy for MCU module, probe, regulator, enclosure, and power adapter.

### 2. OEM/ODM selection and evaluation package

- RFQ sent to at least three suppliers with the same measurable electrical, sensing, mechanical, firmware,
  security, compliance, cost, warranty, and volume requirements.
- Evaluation units from shortlisted suppliers, with product IDs, firmware versions, PCB/transducer details,
  radio module, enclosure material, power adapter, and complete data protocol documented.
- Demonstration that the supplier permits WaterFlex firmware or a WaterFlex-controlled MQTT/HTTPS endpoint;
  vendor-cloud-only products do not qualify for this plan.
- Demonstration that raw distance, quality/error state, source timestamp, firmware version, and device identity
  can be reported. A vendor-calculated percentage alone is insufficient unless independently validated.
- Supplier engineering support, firmware source/escrow or perpetual binary rights, OTA ownership, vulnerability
  response, product-change notification, warranty, RMA, lead time, minimum order, tooling, and volume pricing.
- Sample comparison report and scored selection matrix.

### 3. White-label production hardware package

- Integrated production PCB using a pre-certified Wi-Fi/BLE module and a sealed ultrasonic transducer.
- Stable corded 5V power design with a safety-listed external adapter, protection, decoupling, antenna
  clearance, production test points, and secure programming interface.
- RF-safe IP-rated enclosure with controlled probe alignment, gasket/vent strategy, cable strain relief, and
  universal or approved tank-lid mounting.
- WaterFlex branding, serial/QR label, regulatory labels, packaging, installation accessories, and tamper/RMA
  identification.
- Controlled manufacturing BOM, approved alternates, assembly drawings, enclosure files, firmware image,
  programming instructions, test limits, and serialized factory test records.
- OEM design-for-manufacture, design-for-test, component lifecycle, and change-control review accepted by
  WaterFlex before production approval.

### 4. WaterFlex-controlled OEM firmware

- Driver for the selected OEM transducer/interface, frame validation, timeout/error handling, multiple samples,
  outlier rejection, and confidence/quality state.
- Device identity, immutable serial number, firmware version, hardware revision, and manufacturing metadata.
- SoftAP captive-portal provisioning for 2.4 GHz Wi-Fi.
- Installer calibration workflow for full distance and normal standing-water/empty distance.
- Persistent configuration with versioning, validation, reset, and recovery behavior.
- MQTT over TLS, per-device credentials, reconnect/backoff, keepalive, quality-of-service decision, message
  sequence numbers, source timestamps, and offline buffering policy.
- Telemetry containing device ID, raw distance, sample quality, firmware version, uptime, reset reason,
  Wi-Fi signal, and error state.
- Secure OTA update, signed images, rollback, staged deployment, and recovery from interrupted updates.
- Watchdog, brownout handling, factory reset, diagnostic mode, and safe failure state.
- Installer-visible or installer-app status for boot, provisioning, connecting, online, calibration, error,
  and reset. Physical controls/indicators are selected with the OEM only when the workflow requires them.

### 5. Dealer installation and onboarding flow

1. Dealer selects the WaterFlex customer and begins sensor installation.
2. Dealer records or scans the sensor serial in WaterFlex.
3. Sensor is mounted perpendicular to the expected salt surface and clear of tank walls and brine hardware.
4. Dealer powers the device and enters the OEM-defined WaterFlex provisioning mode.
5. Dealer uses the approved WaterFlex installer flow to submit the customer's 2.4 GHz Wi-Fi credentials.
6. Dealer completes tank calibration or selects an approved tank profile, then verifies the standing-water line.
7. Sensor connects to WaterFlex MQTT and sends a commissioning message.
8. WaterFlex verifies device identity, customer mapping, calibration, and first reading.
9. Dealer receives a successful-installation result and leaves; homeowners require no app.

### 6. Shared WaterFlex backend

- .NET solution separated into API, Domain, Ingestion, Rules, Infrastructure, and Worker components.
- `ITelemetrySourceAdapter` contract so the domain does not depend on MQTT or this hardware.
- MQTT client/consumer with per-device authentication, schema validation, deduplication, replay protection,
  dead-letter handling, and observability.
- SQL Server schema for Dealer/Tenant, Device, DeviceCustomerMapping, Calibration, SensorReading,
  TriggerState, DeliveryTicket, OutboxEvent, DeviceHealth, FirmwareDeployment, and AuditEvent.
- Enforced tenant scope on every query and operation.
- Device registry and credential lifecycle: manufacture, claim, activate, rotate, revoke, replace, and retire.
- REST/internal APIs needed by existing WaterFlex interfaces and the operations console.

### 7. Fill percentage and signal-quality service

- Use `fillPct = clamp((emptyDistance - measuredDistance) / (emptyDistance - fullDistance) * 100, 0, 100)`.
- Treat the normal standing-water surface as the calibrated empty point, because submerged salt is not visible
  to a top-down ultrasonic sensor.
- Median or robust-window smoothing, configurable minimum sample count, rate-of-change checks, and invalid
  reading rejection.
- Optional regeneration-window suppression and configurable tank/customer thresholds.
- Confidence flags for coning, bridging, persistent waterline, out-of-range distance, unstable readings,
  bad calibration, and stale data.
- Preserve raw data and calculation version so results can be reprocessed and audited.

### 8. Ticket automation

- Trigger only after fill remains below 35% for the configured sample/time window.
- Require a valid customer mapping and acceptable reading confidence.
- Suppress creation when an open delivery ticket exists or the customer is in post-delivery cooldown.
- Generate an idempotency key based on tenant, customer/device, and depletion cycle.
- Store the request transactionally in an outbox; retry transient failures and dead-letter permanent failures.
- `IDeliveryTicketGateway` request includes WaterFlex customer/account reference, device, fill percentage,
  threshold, reading timestamp, salt product/quantity if known, and idempotency key.
- Use a stub until WaterFlex API endpoints and sandbox credentials arrive.
- Synchronize fulfilled/cancelled ticket status when WaterFlex supports it and reset the depletion cycle safely.

### 9. WaterFlex internal operations console

- WaterFlex staff authentication and role-based authorization.
- Fleet totals, online/offline/stale state, latest reading, firmware version, and Wi-Fi quality.
- Provisioning failures, calibration state, customer mapping, sensor replacement, and credential revocation.
- Reading and calculation history sufficient to diagnose false alarms.
- Ticket/outbox failures and controlled retry.
- OTA campaign creation, staged rollout, progress, failure, pause, and rollback.
- Tenant-aware support access and immutable audit history.

### 10. Compliance, manufacturing, and support package

- Verify the selected radio module's FCC/ISED/CE modular approvals and every grant condition.
- Obtain finished-product FCC Part 15B and applicable product reports/declarations from the OEM; independently
  review whether enclosure, cabling, labels, firmware, or power changes require retesting.
- Use a listed external power adapter and document power/enclosure labeling.
- Environmental and mechanical validation plan for temperature, humidity, condensation, corrosion, drop,
  vibration, cable strain, ingress, and mounting.
- Factory acceptance test: power, current, probe reading, Wi-Fi, identity, WaterFlex endpoint, provisioning,
  calibration, firmware version, security state, and serialized result.
- RMA, replacement, secure decommissioning, and warranty procedures.
- Dealer quick-start, installation checklist, troubleshooting guide, and WaterFlex support runbook.

## Implementation process

### Phase A0: Requirements, OEM discovery, and sample qualification

1. Freeze measurable tank/enclosure requirements and acceptance limits.
2. Issue a common RFQ to at least three qualified OEM/ODM candidates.
3. Review firmware rights, endpoint control, security architecture, certifications, supplier quality, warranty,
   lead time, minimum order, tooling, and volume pricing.
4. Obtain 3-5 evaluation units from each serious finalist and record hardware/firmware/product identifiers.
5. Stand up a development MQTT/TLS or HTTPS endpoint and prove raw distance plus health telemetry.
6. Test full, 35%, waterline, and empty conditions with pellets and crystals.
7. Test water film, coning, bridging, wall echoes, probe angle, regeneration changes, and mounting variation.
8. Run a multi-day humidity/condensation, weak-Wi-Fi, power-cycle, and connectivity soak.
9. Produce a scored supplier/sample report, compliance-gap analysis, and total landed-cost quotation.

**Exit gate:** at least one OEM sample provides repeatable useful level data on representative tanks, sends
secure first-party telemetry, grants acceptable firmware/data rights, and has a credible manufacturing,
compliance, support, and cost path.

### Phase A1: Firmware and shared-platform foundation

1. Establish WaterFlex-controlled firmware source/escrow, build, signing, test, and release responsibilities
  with the selected OEM.
2. Implement or require provisioning, persistent configuration, calibrated sampling, MQTT/TLS or approved
  HTTPS telemetry, device health, and OTA foundation on the OEM hardware.
3. Create the .NET solution and SQL schema.
4. Implement tenant boundaries, device registry, normalized-reading contract, MQTT adapter, and WaterFlex
   gateway stub.
5. Create unit and hardware-in-the-loop test harnesses.

**Dependencies:** OEM firmware customization can proceed in parallel with backend foundations after telemetry,
identity, security, and ownership contracts are agreed.

### Phase A2: Level processing and ticket automation

1. Implement calibration, waterline handling, smoothing, confidence, and stale-device detection.
2. Implement threshold/debounce, open-ticket suppression, cooldown, idempotency, and outbox processing.
3. Prove one physical low-tank sequence creates exactly one stub ticket.
4. Add the minimal operations-console device and ticket views.

### Phase A3: White-label production validation and certification

1. Freeze the OEM production hardware, enclosure, power adapter, transducer, labels, packaging, and firmware
  baseline selected from the qualified sample.
2. Review and approve OEM engineering validation evidence; independently repeat WaterFlex-critical sensing,
  security, environmental, and connectivity tests.
3. Complete RF/enclosure review, Part 15B and required safety/compliance gap closure.
4. Execute a small production-validation build using the real factory line and production components.
5. Finalize factory programming, per-device credentials, serialization, end-of-line testing, packaging,
  traceability, and logistics.
6. Lock approved alternates, product-change notification, requalification triggers, and change-control rules.

**Dependency:** do not approve tooling, non-recurring engineering charges, or volume inventory until sample
accuracy, firmware/data rights, compliance, supplier quality, and commercial demand are validated.

### Phase A4: WaterFlex integration and dealer pilot

1. Replace `IDeliveryTicketGateway` stub after WaterFlex endpoints arrive.
2. Add customer/device mapping and supported level/health fields to existing WaterFlex workflows.
3. Pilot with one Culligan dealer and 10-20 representative customers.
4. Measure installation time, pairing failures, offline rate, false-low/false-high rate, ticket precision,
   support burden, and OTA reliability.
5. Refine hardware, firmware, calibration, installation instructions, and operational alerts.

### Phase A5: Production rollout

1. Approve production only after pilot, certification, supply, security, support, and cost gates pass.
2. Roll out by dealer cohort with limited firmware campaigns and rollback capacity.
3. Monitor device health, ticket outcomes, support incidents, and sensor drift.
4. Establish quarterly firmware, security, supplier, and field-quality reviews.

## Verification

### Automated

- Unit tests for selected sensor-driver/payload parsing, configuration, telemetry encoding, fill math,
  smoothing, confidence, rules,
  idempotency, cooldown, tenant isolation, and ticket contracts.
- Firmware integration tests for Wi-Fi loss, MQTT reconnect, credential rejection, power interruption, OTA
  failure/rollback, corrupted configuration, watchdog reset, and factory reset.
- Backend integration test: MQTT telemetry -> normalized reading -> SQL -> rules -> one stub ticket.
- Security tests for invalid certificates, replay/duplicate messages, unauthorized device IDs, tenant crossing,
  secret rotation, and malicious payloads.
- Load test at 2,000 devices plus reconnection bursts and projected growth.

### Physical/manual

- Representative tank sizes, lid geometries, salt pellets/crystals, salt levels, and standing-water heights.
- Coning, bridging, thin water cover, full submersion at low salt, regeneration, foam, and turbulence.
- Condensation, corrosion exposure, temperature range, probe fouling, cable strain, enclosure ingress, and
  mounting-angle tolerance.
- Weak Wi-Fi, router replacement, DHCP changes, internet outage, server outage, and repeated power cycles.
- Installation, replacement, re-provisioning, calibration repeatability, and decommissioning.

## Success criteria

- Sensor production cost is supported by quotations and remains below the approved commercial target.
- Representative-tank testing produces an acceptable false-ticket and missed-ticket rate.
- Provisioning and installation fit the dealer workflow without homeowner software.
- Every device has unique revocable credentials and supports safe OTA rollback.
- A sustained low tank creates one and only one WaterFlex delivery ticket.
- Tenant boundaries prevent dealers from accessing one another's data.
- Manufacturing, compliance, supply, support, and field-replacement processes are approved before scale.

## Primary risks and mitigations

- **Ultrasonic ambiguity:** calibrate to waterline, smooth readings, score confidence, and validate many tanks.
- **Condensation/corrosion:** vented sealed enclosure, conformal coating, environmental soak, serviceable probe.
- **Basement Wi-Fi:** antenna keep-out, placement guidance, RSSI monitoring, reconnect logic, optional external
  antenna production variant if field data justifies it.
- **OEM customization scope and schedule:** qualify stock samples before paying for tooling or deep firmware/
  enclosure customization; preserve a fallback supplier and Plan C for technical benchmarking.
- **Certification/manufacturing:** use pre-certified radio module, listed supply, experienced contract
  manufacturer, factory test, alternates, and formal change control.
- **Security at scale:** per-device identity, TLS, signed OTA, credential rotation/revocation, audit, and staged
  firmware deployment.

## Deferred inputs

- WaterFlex customer lookup, device mapping, delivery creation, and ticket-status API endpoints.
- Final tank families and approved installation geometries.
- Production volumes, warranty target, and acceptable field-failure rate.
- OEM/ODM selection, final commercial agreement, tooling quotation, and independent compliance-review quote.
