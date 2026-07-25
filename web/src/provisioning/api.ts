import { developmentIdentityHeaders } from '../development/DevelopmentIdentity';
import type {
  CommissionSensorRequest,
  CommissionSensorResponse,
  WaterFlexCustomerOption,
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

export async function searchCustomers(
  search: string,
  signal?: AbortSignal,
): Promise<WaterFlexCustomerOption[]> {
  const params = new URLSearchParams();
  if (search.trim()) {
    params.set('search', search.trim());
  }

  const suffix = params.size > 0 ? `?${params.toString()}` : '';
  const response = await fetch(`/api/v1/technician/customers${suffix}`, {
    signal,
    headers: developmentIdentityHeaders(),
  });
  if (!response.ok) {
    throw await toApiError(response);
  }

  return response.json() as Promise<WaterFlexCustomerOption[]>;
}

export async function commissionSensor(
  request: CommissionSensorRequest,
): Promise<CommissionSensorResponse> {
  const response = await fetch('/api/v1/technician/commission', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...developmentIdentityHeaders(),
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw await toApiError(response);
  }

  return response.json() as Promise<CommissionSensorResponse>;
}

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