/** Telemetry-reporting health for a device, derived server-side from how recently it last reported. */
export type ReportingStatus = 'reporting' | 'stale' | 'offline' | 'neverReported';
/** Server-reported health of the sensor hardware itself (distinct from reporting/connectivity health). */
export type SensorHealthStatus = 'unknown' | 'healthy' | 'faulted';
export type SensorFaultCode = 'readTimeout' | 'invalidSignal' | 'outOfRange' | 'unstableSignal';
/** Fleet table sort order; 'attention' surfaces devices needing operator attention first (server-defined ranking). */
export type FleetSort = 'attention' | 'lastReported' | 'fillAscending' | 'fillDescending' | 'customer';

/** Query parameters accepted by the fleet summary/devices endpoints; see {@link toQuery} in `ops/api.ts` for serialization. */
export interface FleetFilters {
  search?: string;
  reportingStatus?: ReportingStatus;
  belowThreshold?: boolean;
  lifecycleStatus?: string;
  firmwareVersion?: string;
  dealerId?: string;
  sort?: FleetSort;
  page?: number;
  pageSize?: number;
}

/** A dealer option for the fleet dealer filter dropdown. */
export interface FleetDealerOption {
  externalId: string;
  displayName: string;
}

/** Fleet-wide counts shown in the summary metric row; independent of the current filters (except dealer scoping via identity). */
export interface FleetSummary {
  generatedAtUtc: string;
  totalProvisioned: number;
  active: number;
  belowThreshold: number;
  reporting: number;
  stale: number;
  offline: number;
  neverReported: number;
}

/** A single row of the fleet table: latest known state of one provisioned device/installation. */
export interface FleetDevice {
  deviceId: string;
  installationId: string;
  serialNumber: string;
  model: string;
  lifecycleStatus: string;
  dealerExternalId: string | null;
  dealerName: string;
  customerDisplayName: string;
  accountNumber: string | null;
  locationDisplayName: string;
  addressSummary: string | null;
  tankLabel: string;
  capacityPounds: number | null;
  fillPercent: number | null;
  isBelowThreshold: boolean;
  reportingStatus: ReportingStatus;
  lastReportedAtUtc: string | null;
  rawDistanceMm: number | null;
  quality: number | null;
  wifiRssiDbm: number | null;
  firmwareVersion: string | null;
  errorFlags: string[];
  sensorStatus: SensorHealthStatus;
  sensorFault: SensorFaultCode | null;
  lastHealthReportedAtUtc: string | null;
  clockSynchronized: boolean;
  queuedReadingCount: number;
  droppedReadingCount: number;
}

/** One paginated page of the fleet device list. */
export interface FleetPageResult {
  generatedAtUtc: string;
  items: FleetDevice[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** Full device-detail-page payload: the fleet row plus provisioning/calibration/credential metadata not needed by the table view. */
export interface FleetDeviceDetail {
  device: FleetDevice;
  registeredAtUtc: string;
  commissionedAtUtc: string | null;
  installedAtUtc: string;
  installedBy: string | null;
  waterFlexWorkOrderId: string | null;
  calibrationVersion: number | null;
  tankDepthMm: number | null;
  commissioningDistanceMm: number | null;
  calibrationEffectiveFromUtc: string | null;
  hasActiveCredential: boolean;
  credentialLastUsedAtUtc: string | null;
  rowVersion: string;
}

/** A single raw telemetry reading, as shown in the device detail page's 24h history view. */
export interface FleetReading {
  readingId: number;
  timestampUtc: string;
  usesObservedTimestamp: boolean;
  receivedAtUtc: string;
  fillPercent: number;
  rawDistanceMm: number;
  quality: number;
  wifiRssiDbm: number;
  firmwareVersion: string;
  errorFlags: string[];
}

export type TelemetryHistoryResolution = 'hour' | 'day';

/** One bucketed (hour/day) aggregate of readings, as shown in the device detail page's 7d/30d/13m/3y history views. */
export interface FleetHistoryPoint {
  bucketStartUtc: string;
  bucketEndUtc: string;
  lastReadingAtUtc: string;
  readingCount: number;
  fillPercentMin: number;
  fillPercentMax: number;
  fillPercentAverage: number;
  fillPercentLatest: number;
  rawDistanceMmMin: number;
  rawDistanceMmMax: number;
  rawDistanceMmAverage: number;
  wifiRssiDbmMin: number;
  wifiRssiDbmMax: number;
  wifiRssiDbmAverage: number;
  worstQuality: number;
  errorCount: number;
  latestFirmwareVersion: string;
}

/** Response envelope for {@link getFleetHistory}: the resolution actually used plus the bucketed points. */
export interface FleetHistory {
  resolution: TelemetryHistoryResolution;
  fromUtc: string;
  throughUtc: string;
  points: FleetHistoryPoint[];
}

/** Review-workflow status of a low-salt alert, from opening through staff approval/dismissal to resolution. */
export type LowSaltAlertStatus = 'open' | 'acknowledged' | 'approved' | 'dismissed' | 'resolved';

/** Status of the downstream (e.g. RouteFlex) delivery ticket created for an approved alert. */
export type DeliveryTicketStatus = 'pending' | 'created' | 'resolved' | 'failed';

/** A single row of the alerts review queue. */
export interface AlertListItem {
  alertId: string;
  deviceId: string;
  installationId: string;
  serialNumber: string;
  dealerName: string;
  customerDisplayName: string;
  locationDisplayName: string;
  tankLabel: string;
  status: LowSaltAlertStatus;
  openedAtUtc: string;
  lastEvidenceAtUtc: string;
  lastEvidenceFillPercent: number;
  rowVersion: string;
  ticketStatus: DeliveryTicketStatus | null;
  ticketExternalId: string | null;
}

/** One entry in an alert's audit trail (a status transition or system event). */
export interface AlertAuditItem {
  id: number;
  eventType: string;
  actorType: string;
  actorId: string;
  reason: string | null;
  telemetryReadingId: number | null;
  fillPercent: number | null;
  occurredAtUtc: string;
}

/** Detail of the delivery ticket (if any) created for an alert once approved. */
export interface DeliveryTicketDetail {
  status: DeliveryTicketStatus;
  externalTicketId: string | null;
  requestedAtUtc: string;
  externalCreatedAtUtc: string | null;
  resolvedAtUtc: string | null;
  lastError: string | null;
}

/** Full alert-detail-panel payload: the alert plus its audit trail and delivery ticket. */
export interface AlertDetail { alert: AlertListItem; auditHistory: AlertAuditItem[]; ticket: DeliveryTicketDetail | null; }
/** One paginated page of the alerts list, plus a dead-letter count for alerts stuck in delivery. */
export interface AlertPageResult {
  items: AlertListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  deadLetterCount: number;
}
