import { developmentIdentityHeaders } from '../development/DevelopmentIdentity';

export type WorkOrderStatus = 'open' | 'completed' | 'cancelled';

export interface WorkOrder {
  id: string;
  workOrderNumber: string;
  status: WorkOrderStatus;
  customerName: string;
  locationName: string;
  address: string;
  tankLocation: string;
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
  customerName: string;
  locationName: string;
  address: string;
  tankLocation: string;
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

export const listWorkOrders = (signal?: AbortSignal) => request<WorkOrder[]>('/api/v1/work-orders', { signal });
export const createWorkOrder = (input: CreateWorkOrderInput) =>
  request<WorkOrder>('/api/v1/work-orders', { method: 'POST', body: JSON.stringify(input) });
export const cancelWorkOrder = (order: WorkOrder, reason: string) =>
  request<WorkOrder>(`/api/v1/work-orders/${order.id}/cancel`, {
    method: 'POST', body: JSON.stringify({ reason, rowVersion: order.rowVersion }),
  });
