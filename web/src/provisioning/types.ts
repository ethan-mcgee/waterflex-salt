export interface InstallationWorkOrderView {
  workOrderNumber: string;
  customerDisplayName: string;
  locationDisplayName: string;
  addressSummary: string;
  tankLocation: string | null;
}

export type CommissioningSessionStatus =
  | 'pendingSensor'
  | 'awaitingFirstTelemetry'
  | 'completed'
  | 'expired'
  | 'cancelled'
  | 'failed';

export interface CommissioningSessionView {
  sessionId: string;
  deviceId: string;
  serialNumber: string;
  status: CommissioningSessionStatus;
  createdAtUtc: string;
  expiresAtUtc: string;
  dealerName: string;
  customerDisplayName: string;
  locationDisplayName: string;
  addressSummary: string;
  tankLabel: string;
  tankDepthCm: number;
  activatedAtUtc: string | null;
  completedAtUtc: string | null;
  failureCode: string | null;
}

export interface CreateWorkOrderCommissioningSessionRequest {
  workOrderNumber: string;
  serialNumber: string;
  tankLocation: string | null;
  tankDepthCm: number;
}
