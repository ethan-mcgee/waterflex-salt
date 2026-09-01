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

/**
 * This file's fetch wrapper is split across {@link getJson} (reads) and inline `fetch` calls for
 * writes (e.g. {@link transitionAlert}), but follows the same shared pattern independently
 * implemented in `provisioning/bootstrapApi.ts` (as `request`, throwing `ApiError`) and
 * `staff/api.ts` (as `request`, throwing a plain `Error`): inject
 * {@link developmentIdentityHeaders} as the auth mechanism, throw a module-specific Error subclass
 * ({@link OpsApiError}) parsed from an RFC7807-style JSON problem body (title/detail), and return
 * typed JSON on success. If you're reading this in one of the three files, the other two work
 * identically. See {@link getFleetReadings} and {@link getFleetHistory} below for this file's one
 * extra behavior: retrying once on a 5xx response.
 */

/** Fetches a page of low-salt alerts, optionally filtered by status. */
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

/** Fetches one alert's full detail, including audit history and delivery ticket. */
export async function getAlert(alertId: string, signal?: AbortSignal): Promise<AlertDetail> {
  return getJson(`/api/v1/ops/alerts/${encodeURIComponent(alertId)}`, signal);
}

/** Applies an acknowledge/approve/dismiss transition to an alert (optimistic-concurrency checked via `alert.rowVersion`) and returns the updated detail. */
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

/** Fetches the list of dealers available for filtering the fleet view. */
export async function getFleetDealers(signal?: AbortSignal): Promise<FleetDealerOption[]> {
  return getJson('/api/v1/ops/dealers', signal);
}

/** Thrown when an ops API call fails; carries the HTTP status so callers can distinguish e.g. retryable 5xx from a hard 4xx. */
export class OpsApiError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message);
  }
}

/** Fetches fleet-wide summary counts (provisioned/reporting/below-threshold/etc.) for the given filters. */
export async function getFleetSummary(
  filters: FleetFilters,
  signal?: AbortSignal,
): Promise<FleetSummary> {
  return getJson(`/api/v1/ops/fleet/summary${toQuery(filters, false)}`, signal);
}

/** Fetches a filtered, sorted, paginated page of fleet devices. */
export async function getFleetDevices(
  filters: FleetFilters,
  signal?: AbortSignal,
): Promise<FleetPageResult> {
  return getJson(`/api/v1/ops/devices${toQuery(filters, true)}`, signal);
}

/** Fetches full detail for a single fleet device. */
export async function getFleetDevice(
  deviceId: string,
  signal?: AbortSignal,
): Promise<FleetDeviceDetail> {
  return getJson(`/api/v1/ops/devices/${encodeURIComponent(deviceId)}`, signal);
}

/**
 * Fetches raw telemetry readings for a device over the given range (used for the '24h' view).
 * Non-obvious behavior: if the first request fails with a 5xx (or a non-abort network error),
 * this waits 750ms via {@link retryDelay} and retries exactly once before giving up — see
 * {@link shouldRetryHistory}. A 4xx, or the caller's `signal` being aborted, is not retried.
 */
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

/**
 * Fetches bucketed telemetry history for a device over the given range/resolution (used for the
 * '7d'/'30d'/'13m'/'3y' views). Same one-shot 750ms retry-on-5xx behavior as
 * {@link getFleetReadings} — see {@link shouldRetryHistory} and {@link retryDelay}.
 */
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

/** Decides whether {@link getFleetReadings}/{@link getFleetHistory} should retry: yes for a 5xx `OpsApiError` or any non-abort error, no if the request was aborted or failed with a 4xx. */
function shouldRetryHistory(reason: unknown, signal?: AbortSignal): boolean {
  if (signal?.aborted) return false;
  if (reason instanceof OpsApiError) return reason.status >= 500;
  return !(reason instanceof DOMException && reason.name === 'AbortError');
}

/** Resolves after `milliseconds`, or rejects immediately with an `AbortError` if `signal` is already aborted or aborts while waiting — an abortable `setTimeout`. */
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

/** Shared GET helper: injects auth headers, throws {@link OpsApiError} parsed from an RFC7807-style problem body on failure, otherwise returns typed JSON. */
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

/** Serializes `FleetFilters` into a query string; `includePaging` controls whether sort/page/pageSize are included (summary requests omit them). */
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
