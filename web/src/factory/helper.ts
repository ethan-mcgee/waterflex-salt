export type HelperJobStatus = 'prepared' | 'queued' | 'flashing' | 'provisioning' | 'verifying' | 'completed' | 'failed';

export interface HelperEvidence {
  firmware: boolean;
  identity: boolean;
  portal: boolean;
  portalStartup: boolean;
  sensor: boolean;
  sensorSampleCount: number;
  sensorMinimumMm: number | null;
  sensorMaximumMm: number | null;
  sensorFailureCategories: string[];
}

export interface HelperJob {
  idempotencyKey: string;
  bootstrapCredentialId: string;
  bootstrapSecretHash: string;
  status: HelperJobStatus;
  message: string;
  serialNumber: string | null;
  evidence: HelperEvidence | null;
  failureCode: string | null;
}

export interface HelperLabel {
  serialNumber: string;
  setupNetwork: string;
  setupPassphrase: string;
  firmwareVersion: string;
  configurationVersion: string;
}

export interface HelperDevice {
  port: string;
  description: string;
}

export interface HelperDevices {
  status: 'none' | 'detected' | 'multiple';
  devices: HelperDevice[];
}

export interface HelperStation {
  helperVersion: string; protocolVersion: string; enrollmentStatus: 'enrolled' | 'unenrolled'; proposedWorkstationName: string;
  stationId: string | null; displayName: string | null; publicKeyThumbprint: string; publicKey: string; keyProviderType: 'tpm' | 'software';
}

async function helperRequest<T>(baseUrl: string, path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${baseUrl.replace(/\/$/, '')}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  });
  if (!response.ok) throw new Error(`Factory helper request failed (${response.status}).`);
  return await response.json() as T;
}

export const checkHelper = (baseUrl: string, signal?: AbortSignal) =>
  helperRequest<{ status: 'ready'; protocolVersion: string }>(baseUrl, '/v1/health', { signal });

export const getHelperDevices = (baseUrl: string, signal?: AbortSignal) =>
  helperRequest<HelperDevices>(baseUrl, '/v1/devices', { signal });

export const getHelperStation = (baseUrl: string, signal?: AbortSignal) => helperRequest<HelperStation>(baseUrl, '/v1/station', { signal });
export const enrollHelperStation = (baseUrl: string, input: { grantToken: string; displayName: string }) =>
  helperRequest<HelperStation>(baseUrl, '/v1/station/enroll', { method: 'POST', body: JSON.stringify(input) });

export const prepareHelperJob = (baseUrl: string, input: {
  idempotencyKey: string; bootstrapCredentialId: string; bootstrapSecret: string; setupPassphrase: string;
}) => helperRequest<HelperJob>(baseUrl, '/v1/jobs', { method: 'POST', body: JSON.stringify(input) });

export const startHelperJob = (baseUrl: string, idempotencyKey: string, input: {
  deviceId: string; serialNumber: string; model: string; firmwareVersion: string; configurationVersion: string;
  flashAuthorizationToken: string;
}) => helperRequest<HelperJob>(baseUrl, `/v1/jobs/${encodeURIComponent(idempotencyKey)}/start`, { method: 'POST', body: JSON.stringify(input) });

export const getHelperJob = (baseUrl: string, idempotencyKey: string, signal?: AbortSignal) =>
  helperRequest<HelperJob>(baseUrl, `/v1/jobs/${encodeURIComponent(idempotencyKey)}`, { signal });

export const getHelperLabel = (baseUrl: string, idempotencyKey: string) =>
  helperRequest<HelperLabel>(baseUrl, `/v1/jobs/${encodeURIComponent(idempotencyKey)}/label`);

export const clearHelperJob = (baseUrl: string, idempotencyKey: string) =>
  helperRequest<{ cleared: boolean }>(baseUrl, `/v1/jobs/${encodeURIComponent(idempotencyKey)}`, { method: 'DELETE' });
