import { developmentIdentityHeaders } from '../development/DevelopmentIdentity';

export interface FactoryConfiguration {
  enabled: boolean;
  model: string;
  approvedFirmwareVersion: string;
  configurationVersion: string;
  helperBaseUrl: string;
  helperProtocolVersion: string;
}

export type FactoryProvisioningStatus = 'registered' | 'provisioned' | 'failed' | 'quarantined';

export interface FactoryRegistration {
  deviceId: string;
  idempotencyKey: string;
  serialNumber: string;
  model: string;
  registeredAtUtc: string;
  bootstrapCredentialId: string;
  status: FactoryProvisioningStatus;
  verifiedAtUtc: string | null;
  failureCode: string | null;
  /** Short-lived, single-use token the local helper must present to flash the device. Null while quarantined — a retry mints its own. */
  flashAuthorizationToken: string | null;
}

export interface FactoryVerification {
  deviceId: string;
  serialNumber: string;
  status: FactoryProvisioningStatus;
  verifiedAtUtc: string;
  failureCode: string | null;
}

export interface FactoryRegistrationRequest {
  idempotencyKey: string;
  model: string;
  bootstrapCredentialId: string;
  bootstrapSecretHash: string;
  firmwareVersion: string;
  configurationVersion: string;
}

interface ApiProblem { title?: string; detail?: string; errors?: Record<string, string[]> }

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      'X-WaterFlex-Request': 'console',
      ...developmentIdentityHeaders(),
      ...init?.headers,
    },
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as ApiProblem;
    const validation = problem.errors ? Object.values(problem.errors).flat().join(' ') : '';
    throw new Error(validation || problem.detail || problem.title || `Factory request failed (${response.status}).`);
  }
  return await response.json() as T;
}

export const getFactoryConfiguration = (signal?: AbortSignal) =>
  request<FactoryConfiguration>('/api/v1/factory/configuration', { signal });

export const registerFactoryDevice = (input: FactoryRegistrationRequest) =>
  request<FactoryRegistration>('/api/v1/factory/devices', { method: 'POST', body: JSON.stringify(input) });

export const findFactoryDevice = (idempotencyKey: string, signal?: AbortSignal) =>
  request<FactoryRegistration>(`/api/v1/factory/devices/by-idempotency/${encodeURIComponent(idempotencyKey)}`, { signal });

export const findActiveFactoryDevice = (signal?: AbortSignal) =>
  request<FactoryRegistration>('/api/v1/factory/devices/active', { signal });

export const recordFactoryVerification = (
  deviceId: string,
  input: { firmwareVerified: boolean; identityVerified: boolean; portalVerified: boolean; sensorVerified: boolean; firmwareVersion: string; failureCode: string | null },
) => request<FactoryVerification>(`/api/v1/factory/devices/${deviceId}/verification`, { method: 'POST', body: JSON.stringify(input) });

export const retryFactoryDevice = (deviceId: string) =>
  request<FactoryRegistration>(`/api/v1/factory/devices/${deviceId}/retry`, { method: 'POST' });

export function createFactorySecrets() {
  const secret = crypto.getRandomValues(new Uint8Array(32));
  const passphrase = crypto.getRandomValues(new Uint8Array(24));
  return {
    idempotencyKey: crypto.randomUUID(),
    bootstrapCredentialId: `wf_boot_${crypto.randomUUID().replaceAll('-', '').slice(0, 24)}`,
    bootstrapSecret: base64Url(secret),
    setupPassphrase: base64Url(passphrase),
  };
}

function base64Url(value: Uint8Array) {
  let binary = '';
  value.forEach((byte) => { binary += String.fromCharCode(byte); });
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '');
}
