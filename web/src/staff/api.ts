import { developmentIdentityHeaders, type StaffRole } from '../development/DevelopmentIdentity';

/** Lifecycle state of a staff identity, from invitation acceptance through suspension/removal. */
export type StaffIdentityState = 'pendingActivation' | 'active' | 'suspended' | 'deprovisioning' | 'failed';

/** A console staff account (dealer or WaterFlex). `rowVersion` guards `changeState` against concurrent updates. */
export interface StaffMember {
  id: string; email: string; displayName: string; role: StaffRole;
  dealerExternalId: string | null; dealerName: string | null;
  state: StaffIdentityState; createdAtUtc: string; updatedAtUtc: string; rowVersion: number;
}

/** A pending invitation for a not-yet-activated staff account. */
export interface StaffInvitation {
  id: string; email: string; displayName: string; role: StaffRole;
  dealerExternalId: string | null; dealerName: string | null;
  status: string; createdAtUtc: string; expiresAtUtc: string; failureReason: string | null;
}

/**
 * Private fetch wrapper for this module. This is the same shared pattern independently
 * implemented in `provisioning/bootstrapApi.ts` (as `request`, throwing `ApiError`) and
 * `ops/api.ts` (as `getJson`/`request`, throwing `OpsApiError`): it injects
 * {@link developmentIdentityHeaders} as the auth mechanism, throws a plain `Error` parsed from an
 * RFC7807-style JSON problem body (title/detail), and returns typed JSON on success. If you're
 * reading this in one of the three files, the other two work identically.
 */
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

/** Fetches all active staff members. */
export const listStaff = (signal?: AbortSignal) => request<StaffMember[]>('/api/v1/staff-admin/staff', { signal });
/** Fetches all pending staff invitations. */
export const listInvitations = (signal?: AbortSignal) => request<StaffInvitation[]>('/api/v1/staff-admin/invitations', { signal });
/** Creates a new staff invitation and returns it. */
export const createInvitation = (input: { email: string; displayName: string; role: StaffRole; dealerExternalId: string | null; reason: string }) =>
  request<StaffInvitation>('/api/v1/staff-admin/invitations', { method: 'POST', body: JSON.stringify(input) });
/** Suspends or reactivates a staff member (optimistic-concurrency checked via `member.rowVersion`) and returns the updated member. */
export const changeState = (member: StaffMember, action: 'suspend' | 'reactivate', reason: string) =>
  request<StaffMember>(`/api/v1/staff-admin/staff/${member.id}/${action}`, { method: 'POST', body: JSON.stringify({ reason, rowVersion: member.rowVersion }) });
