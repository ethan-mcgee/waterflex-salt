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

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

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

export const getInstallationWorkOrder = (workOrderNumber: string, signal?: AbortSignal) =>
  request<InstallationWorkOrderView>(
    `/api/v1/technician/installation-work-orders/${encodeURIComponent(workOrderNumber)}`,
    { signal },
  );

export const createWorkOrderCommissioningSession = (request_: CreateWorkOrderCommissioningSessionRequest) =>
  request<CommissioningSessionView>('/api/v1/technician/work-order-commissioning-sessions', {
    method: 'POST',
    body: JSON.stringify(request_),
  });

export const getCommissioningSession = (sessionId: string, signal?: AbortSignal) =>
  request<CommissioningSessionView>(`/api/v1/technician/commissioning-sessions/${sessionId}`, { signal });

export const cancelCommissioningSession = (sessionId: string) =>
  request<CommissioningSessionView>(`/api/v1/technician/commissioning-sessions/${sessionId}/cancel`, {
    method: 'POST',
  });

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
