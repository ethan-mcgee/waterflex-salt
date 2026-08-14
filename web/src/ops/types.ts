export type ReportingStatus = 'reporting' | 'stale' | 'offline' | 'neverReported';
export type FleetSort = 'attention' | 'lastReported' | 'fillAscending' | 'fillDescending' | 'customer';

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

export interface FleetDealerOption {
  externalId: string;
  displayName: string;
}

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

export interface FleetDevice {
  deviceId: string;
  installationId: string;
  serialNumber: string;
  hardwareId: string;
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
}

export interface FleetPageResult {
  generatedAtUtc: string;
  items: FleetDevice[];
  totalCount: number;
  page: number;
  pageSize: number;
}

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

export interface FleetHistory {
  resolution: TelemetryHistoryResolution;
  fromUtc: string;
  throughUtc: string;
  points: FleetHistoryPoint[];
}
