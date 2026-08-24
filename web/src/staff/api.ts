import { developmentIdentityHeaders, type StaffRole } from '../development/DevelopmentIdentity';

export type StaffIdentityState = 'pendingActivation' | 'active' | 'suspended' | 'deprovisioning' | 'failed';

export interface StaffMember {
  id: string; email: string; displayName: string; role: StaffRole;
  dealerExternalId: string | null; dealerName: string | null;
  state: StaffIdentityState; createdAtUtc: string; updatedAtUtc: string; rowVersion: number;
}

export interface StaffInvitation {
  id: string; email: string; displayName: string; role: StaffRole;
  dealerExternalId: string | null; dealerName: string | null;
  status: string; createdAtUtc: string; expiresAtUtc: string; failureReason: string | null;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: { 'Content-Type': 'application/json', 'X-WaterFlex-Request': 'console', ...developmentIdentityHeaders(), ...init?.headers },
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; title?: string } | null;
    throw new Error(problem?.detail ?? problem?.title ?? `Staff request failed (${response.status}).`);
  }
  return await response.json() as T;
}

export const listStaff = (signal?: AbortSignal) => request<StaffMember[]>('/api/v1/staff-admin/staff', { signal });
export const listInvitations = (signal?: AbortSignal) => request<StaffInvitation[]>('/api/v1/staff-admin/invitations', { signal });
export const createInvitation = (input: { email: string; displayName: string; role: StaffRole; dealerExternalId: string | null; reason: string }) =>
  request<StaffInvitation>('/api/v1/staff-admin/invitations', { method: 'POST', body: JSON.stringify(input) });
export const changeState = (member: StaffMember, action: 'suspend' | 'reactivate', reason: string) =>
  request<StaffMember>(`/api/v1/staff-admin/staff/${member.id}/${action}`, { method: 'POST', body: JSON.stringify({ reason, rowVersion: member.rowVersion }) });
