export type HelperJobStatus = 'prepared' | 'queued' | 'flashing' | 'provisioning' | 'verifying' | 'completed' | 'failed';

export interface HelperEvidence {
  firmware: boolean;
  identity: boolean;
  portal: boolean;
  sensor: boolean;
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

export const prepareHelperJob = (baseUrl: string, input: {
  idempotencyKey: string; bootstrapCredentialId: string; bootstrapSecret: string; setupPassphrase: string;
}) => helperRequest<HelperJob>(baseUrl, '/v1/jobs', { method: 'POST', body: JSON.stringify(input) });

export const startHelperJob = (baseUrl: string, idempotencyKey: string, input: {
  deviceId: string; serialNumber: string; model: string; firmwareVersion: string; configurationVersion: string;
}) => helperRequest<HelperJob>(baseUrl, `/v1/jobs/${encodeURIComponent(idempotencyKey)}/start`, { method: 'POST', body: JSON.stringify(input) });

export const getHelperJob = (baseUrl: string, idempotencyKey: string, signal?: AbortSignal) =>
  helperRequest<HelperJob>(baseUrl, `/v1/jobs/${encodeURIComponent(idempotencyKey)}`, { signal });

export const clearHelperJob = (baseUrl: string, idempotencyKey: string) =>
  helperRequest<{ cleared: boolean }>(baseUrl, `/v1/jobs/${encodeURIComponent(idempotencyKey)}`, { method: 'DELETE' });
