import { developmentIdentityHeaders } from '../development/DevelopmentIdentity';
import type {
  CommissioningSessionView,
  CreateWorkOrderCommissioningSessionRequest,
  InstallationWorkOrderView,
} from './types';

interface ApiProblem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

/** Module-specific error for failed technician/provisioning API calls, parsed from an RFC7807-style JSON problem body. */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

/**
 * Private fetch wrapper for this module. This is the same shared pattern independently
 * implemented in `ops/api.ts` (as `getJson`/`request`) and `staff/api.ts` (as `request`): it
 * injects {@link developmentIdentityHeaders} as the auth mechanism, throws a module-specific
 * Error subclass (here, {@link ApiError}) parsed from an RFC7807-style JSON problem body
 * (title/detail/errors), and returns typed JSON on success. If you're reading this in one of the
 * three files, the other two work identically.
 */
async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...developmentIdentityHeaders(), ...init?.headers },
  });
  if (!response.ok) {
    throw await toApiError(response);
  }

  return response.json() as Promise<T>;
}

/** Looks up an installation work order by number; returns the customer/location/tank it resolves to. */
export const getInstallationWorkOrder = (workOrderNumber: string, signal?: AbortSignal) =>
  request<InstallationWorkOrderView>(
    `/api/v1/technician/installation-work-orders/${encodeURIComponent(workOrderNumber)}`,
    { signal },
  );

/** Creates (reserves) a commissioning session for the given work order, serial number, and tank depth; returns the new session. */
export const createWorkOrderCommissioningSession = (request_: CreateWorkOrderCommissioningSessionRequest) =>
  request<CommissioningSessionView>('/api/v1/technician/work-order-commissioning-sessions', {
    method: 'POST',
    body: JSON.stringify(request_),
  });

/** Fetches the current state of a commissioning session by id; used for both the initial load and each poll tick. */
export const getCommissioningSession = (sessionId: string, signal?: AbortSignal) =>
  request<CommissioningSessionView>(`/api/v1/technician/commissioning-sessions/${sessionId}`, { signal });

/** Cancels an in-progress commissioning session, releasing the reserved sensor; returns the resulting session. */
export const cancelCommissioningSession = (sessionId: string) =>
  request<CommissioningSessionView>(`/api/v1/technician/commissioning-sessions/${sessionId}/cancel`, {
    method: 'POST',
  });

/** Parses a failed fetch Response's RFC7807-style JSON problem body (title/detail/errors) into an {@link ApiError}. */
async function toApiError(response: Response): Promise<ApiError> {
  let problem: ApiProblem = {};
  try {
    problem = (await response.json()) as ApiProblem;
  } catch {
    // The fallback below is more useful than exposing a JSON parsing error.
  }

  const validationMessage = problem.errors
    ? Object.values(problem.errors).flat().join(' ')
    : undefined;
  const message = validationMessage
    || problem.detail
    || problem.title
    || `Request failed with status ${response.status}.`;

  return new ApiError(message, response.status, problem.errors);
}
