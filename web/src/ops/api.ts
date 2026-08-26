import { developmentIdentityHeaders } from '../development/DevelopmentIdentity';
import type {
  FleetDealerOption,
  FleetDeviceDetail,
  FleetFilters,
  FleetHistory,
  FleetPageResult,
  FleetReading,
  FleetSummary,
  AlertDetail,
  AlertListItem,
  AlertPageResult,
  LowSaltAlertStatus,
} from './types';

export async function getAlerts(
  status?: LowSaltAlertStatus,
  page?: number,
  signal?: AbortSignal,
): Promise<AlertPageResult> {
  const params = new URLSearchParams();
  if (status) params.set('status', status);
  if (page) params.set('page', String(page));
  const query = params.toString();
  return getJson(`/api/v1/ops/alerts${query ? `?${query}` : ''}`, signal);
}

export async function getAlert(alertId: string, signal?: AbortSignal): Promise<AlertDetail> {
  return getJson(`/api/v1/ops/alerts/${encodeURIComponent(alertId)}`, signal);
}

export async function transitionAlert(
  alert: AlertListItem,
  transition: 'acknowledge' | 'approve' | 'dismiss',
  reason?: string,
): Promise<AlertDetail> {
  const response = await fetch(`/api/v1/ops/alerts/${encodeURIComponent(alert.alertId)}/${transition}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...developmentIdentityHeaders() },
    body: JSON.stringify({ expectedRowVersion: alert.rowVersion, reason }),
  });
  if (!response.ok) throw new OpsApiError(`Alert transition failed with status ${response.status}.`, response.status);
  return response.json() as Promise<AlertDetail>;
}

export async function getFleetDealers(signal?: AbortSignal): Promise<FleetDealerOption[]> {
  return getJson('/api/v1/ops/dealers', signal);
}

export class OpsApiError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message);
  }
}

export async function getFleetSummary(
  filters: FleetFilters,
  signal?: AbortSignal,
): Promise<FleetSummary> {
  return getJson(`/api/v1/ops/fleet/summary${toQuery(filters, false)}`, signal);
}

export async function getFleetDevices(
  filters: FleetFilters,
  signal?: AbortSignal,
): Promise<FleetPageResult> {
  return getJson(`/api/v1/ops/devices${toQuery(filters, true)}`, signal);
}

export async function getFleetDevice(
  deviceId: string,
  signal?: AbortSignal,
): Promise<FleetDeviceDetail> {
  return getJson(`/api/v1/ops/devices/${encodeURIComponent(deviceId)}`, signal);
}

export async function getFleetReadings(
  deviceId: string,
  range: '24h' | '7d' | '30d',
  signal?: AbortSignal,
): Promise<FleetReading[]> {
  const url = `/api/v1/ops/devices/${encodeURIComponent(deviceId)}/readings?range=${range}&limit=1500`;
  try {
    return await getJson(url, signal);
  } catch (reason) {
    if (!shouldRetryHistory(reason, signal)) throw reason;
    await retryDelay(750, signal);
    return getJson(url, signal);
  }
}

export async function getFleetHistory(
  deviceId: string,
  range: '7d' | '30d' | '13m' | '3y',
  resolution: 'auto' | 'hour' | 'day' = 'auto',
  signal?: AbortSignal,
): Promise<FleetHistory> {
  const url = `/api/v1/ops/devices/${encodeURIComponent(deviceId)}/history?range=${range}&resolution=${resolution}`;
  try {
    return await getJson(url, signal);
  } catch (reason) {
    if (!shouldRetryHistory(reason, signal)) throw reason;
    await retryDelay(750, signal);
    return getJson(url, signal);
  }
}

function shouldRetryHistory(reason: unknown, signal?: AbortSignal): boolean {
  if (signal?.aborted) return false;
  if (reason instanceof OpsApiError) return reason.status >= 500;
  return !(reason instanceof DOMException && reason.name === 'AbortError');
}

function retryDelay(milliseconds: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(new DOMException('The operation was aborted.', 'AbortError'));
      return;
    }
    let timeout = 0;
    const abort = () => {
      window.clearTimeout(timeout);
      reject(new DOMException('The operation was aborted.', 'AbortError'));
    };
    timeout = window.setTimeout(() => {
      signal?.removeEventListener('abort', abort);
      resolve();
    }, milliseconds);
    signal?.addEventListener('abort', abort, { once: true });
  });
}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, { signal, headers: developmentIdentityHeaders() });
  if (!response.ok) {
    let message = `Request failed with status ${response.status}.`;
    try {
      const problem = await response.json() as { title?: string; detail?: string };
      message = problem.detail || problem.title || message;
    } catch {
      // Preserve the status fallback when the response is not JSON.
    }
    throw new OpsApiError(message, response.status);
  }
  return response.json() as Promise<T>;
}

function toQuery(filters: FleetFilters, includePaging: boolean): string {
  const params = new URLSearchParams();
  if (filters.search?.trim()) params.set('search', filters.search.trim());
  if (filters.reportingStatus) params.set('reportingStatus', filters.reportingStatus);
  if (filters.belowThreshold !== undefined) {
    params.set('belowThreshold', String(filters.belowThreshold));
  }
  if (filters.lifecycleStatus) params.set('lifecycleStatus', filters.lifecycleStatus);
  if (filters.firmwareVersion) params.set('firmwareVersion', filters.firmwareVersion);
  if (filters.dealerId) params.set('dealerId', filters.dealerId);
  if (includePaging) {
    if (filters.sort) params.set('sort', filters.sort);
    params.set('page', String(filters.page ?? 1));
    params.set('pageSize', String(filters.pageSize ?? 50));
  }
  const query = params.toString();
  return query ? `?${query}` : '';
}
