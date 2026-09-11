import { developmentIdentityHeaders } from '../development/DevelopmentIdentity';

export type WorkOrderStatus = 'open' | 'completed' | 'cancelled';

export interface WorkOrder {
  id: string;
  workOrderNumber: string;
  status: WorkOrderStatus;
  customerName: string;
  locationName: string | null;
  address: string;
  tankLocation: string | null;
  firstName: string | null;
  lastName: string | null;
  streetAddress: string | null;
  addressLine2: string | null;
  city: string | null;
  state: string | null;
  zipCode: string | null;
  createdByActorId: string;
  createdBy: string;
  createdAtUtc: string;
  completedAtUtc: string | null;
  cancelledByActorId: string | null;
  cancelledBy: string | null;
  cancelledAtUtc: string | null;
  cancellationReason: string | null;
  rowVersion: number;
}

export interface CreateWorkOrderInput {
  firstName: string;
  lastName: string;
  streetAddress: string;
  city: string;
  state: string;
  zipCode: string;
  locationName?: string;
  addressLine2?: string;
}

export interface WorkOrderDealerOption {
  externalId: string;
  displayName: string;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: { 'Content-Type': 'application/json', 'X-WaterFlex-Request': 'console', ...developmentIdentityHeaders(), ...init?.headers },
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; title?: string } | null;
    throw new Error(problem?.detail ?? problem?.title ?? `Work order request failed (${response.status}).`);
  }
  return await response.json() as T;
}

const scopedPath = (path: string, dealerExternalId?: string) => {
  if (!dealerExternalId) return path;
  return `${path}?${new URLSearchParams({ dealerExternalId })}`;
};

export const listWorkOrderDealers = (signal?: AbortSignal) =>
  request<WorkOrderDealerOption[]>('/api/v1/ops/dealers', { signal });
export const listWorkOrders = (dealerExternalId?: string, signal?: AbortSignal) =>
  request<WorkOrder[]>(scopedPath('/api/v1/work-orders', dealerExternalId), { signal });
export const createWorkOrder = (input: CreateWorkOrderInput, dealerExternalId?: string) =>
  request<WorkOrder>(scopedPath('/api/v1/work-orders', dealerExternalId), { method: 'POST', body: JSON.stringify(input) });
export const cancelWorkOrder = (order: WorkOrder, reason: string, dealerExternalId?: string) =>
  request<WorkOrder>(scopedPath(`/api/v1/work-orders/${order.id}/cancel`, dealerExternalId), {
    method: 'POST', body: JSON.stringify({ reason, rowVersion: order.rowVersion }),
  });
