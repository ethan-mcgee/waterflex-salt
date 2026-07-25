export interface WaterFlexTankOption {
  waterFlexAssetId: string;
  label: string;
  capacityPounds: number | null;
}

export interface WaterFlexLocationOption {
  waterFlexLocationId: string;
  displayName: string;
  addressSummary: string;
  tanks: WaterFlexTankOption[];
}

export interface WaterFlexCustomerOption {
  waterFlexCustomerId: string;
  accountNumber: string;
  displayName: string;
  locations: WaterFlexLocationOption[];
}

export interface CommissionSensorRequest {
  waterFlexCustomerId: string;
  waterFlexLocationId: string;
  waterFlexAssetId: string;
  serialNumber: string;
  hardwareId: string;
  model: string;
  waterFlexWorkOrderId: string | null;
  tankDepthCm: number;
  currentDistanceCm: number;
}

export interface CommissionSensorResponse {
  deviceId: string;
  installationId: string;
  serialNumber: string;
  deviceToken: string;
  commissionedAtUtc: string;
  customerDisplayName: string;
  locationDisplayName: string;
  addressSummary: string;
  tankLabel: string;
  calibrationVersion: number;
  tankDepthCm: number;
  commissioningDistanceCm: number;
  initialFillPercent: number;
}