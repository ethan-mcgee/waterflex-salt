import { developmentIdentityHeaders } from '../development/DevelopmentIdentity';
import type {
  FleetDealerOption,
  FleetDeviceDetail,
  FleetFilters,
  FleetPageResult,
  FleetReading,
  FleetSummary,
} from './types';

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
  return getJson(
    `/api/v1/ops/devices/${encodeURIComponent(deviceId)}/readings?range=${range}`,
    signal,
  );
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