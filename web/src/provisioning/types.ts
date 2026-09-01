/** Result of looking up an installation work order by number: the customer/location/tank it resolves to. */
export interface InstallationWorkOrderView {
  workOrderNumber: string;
  customerDisplayName: string;
  locationDisplayName: string;
  addressSummary: string;
  tankLocation: string | null;
}

/**
 * Server-driven lifecycle of a commissioning session, polled by `ProvisioningWorkflow`.
 * Non-terminal: pendingSensor (waiting for the sensor to join Wi-Fi) -> activatedAwaitingHealth ->
 * awaitingFirstTelemetry -> completed. Terminal failures: expired, cancelled, failed.
 */
export type CommissioningSessionStatus =
  | 'pendingSensor'
  | 'activatedAwaitingHealth'
  | 'awaitingFirstTelemetry'
  | 'completed'
  | 'expired'
  | 'cancelled'
  | 'failed';

/** Full state of an in-progress or finished commissioning session, as returned/polled by `bootstrapApi.ts`. */
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

/** Request body for reserving a sensor against a work order. */
export interface CreateWorkOrderCommissioningSessionRequest {
  workOrderNumber: string;
  serialNumber: string;
  tankLocation: string | null;
  tankDepthCm: number;
}
