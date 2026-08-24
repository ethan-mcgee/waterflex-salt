import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { AlertTriangle, Check, ShieldCheck, ShieldOff, UserPlus, X } from 'lucide-react';
import ThemedSelect from '../components/ThemedSelect';
import type { StaffRole } from '../development/DevelopmentIdentity';
import { useDevelopmentIdentity } from '../development/DevelopmentIdentity';
import {
  changeState,
  createInvitation,
  listInvitations,
  listStaff,
  type StaffIdentityState,
  type StaffInvitation,
  type StaffMember,
} from './api';

const STATE_LABELS: Record<StaffIdentityState, string> = {
  pendingActivation: 'Pending activation',
  active: 'Active',
  suspended: 'Suspended',
  deprovisioning: 'Deprovisioning',
  failed: 'Failed',
};

const STATE_TONE: Record<StaffIdentityState, string> = {
  pendingActivation: 'pending',
  active: 'active',
  suspended: 'suspended',
  deprovisioning: 'deprovisioning',
  failed: 'suspended',
};

export default function StaffPage() {
  const { currentUser } = useDevelopmentIdentity();
  const [staff, setStaff] = useState<StaffMember[]>([]);
  const [invitations, setInvitations] = useState<StaffInvitation[]>([]);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [role, setRole] = useState<StaffRole>(currentUser?.role === 'dealerAdministrator' ? 'dealerTechnician' : 'waterFlexEmployee');
  const [dealerExternalId, setDealerExternalId] = useState(currentUser?.dealerExternalId ?? '');
  const [reason, setReason] = useState('New staff access');
  const [pendingMemberId, setPendingMemberId] = useState<string | null>(null);
  const [pendingReason, setPendingReason] = useState('');

  const refresh = useCallback(async () => {
    try { const [members, pending] = await Promise.all([listStaff(), listInvitations()]); setStaff(members); setInvitations(pending); setError(''); }
    catch (value) { setError(value instanceof Error ? value.message : 'Staff access data is unavailable.'); }
  }, []);
  useEffect(() => { void refresh(); }, [refresh]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    try {
      await createInvitation({ email, displayName, role, dealerExternalId: role.startsWith('dealer') ? dealerExternalId : null, reason });
      setEmail('');
      setDisplayName('');
      await refresh();
    } catch (value) { setError(value instanceof Error ? value.message : 'Invitation failed.'); }
    finally { setBusy(false); }
  }

  function startToggle(member: StaffMember) {
    setPendingMemberId(member.id);
    setPendingReason('');
    setError('');
  }

  function cancelToggle() {
    setPendingMemberId(null);
    setPendingReason('');
  }

  async function confirmToggle(member: StaffMember) {
    const changeReason = pendingReason.trim();
    if (!changeReason) { setError('A reason is required to change staff state.'); return; }
    const action = member.state === 'active' ? 'suspend' : 'reactivate';
    setBusy(true);
    try {
      await changeState(member, action, changeReason);
      setPendingMemberId(null);
      setPendingReason('');
      await refresh();
    } catch (value) { setError(value instanceof Error ? value.message : 'Staff state change failed.'); }
    finally { setBusy(false); }
  }

  const waterFlexAdmin = currentUser?.role === 'waterFlexAdministrator';
  const roles: StaffRole[] = waterFlexAdmin
    ? ['waterFlexEmployee', 'waterFlexAdministrator', 'dealerTechnician', 'dealerAdministrator']
    : ['dealerTechnician', 'dealerAdministrator'];
  const showDealerField = role.startsWith('dealer');

  return (
    <section className="fleet-page" aria-labelledby="staff-heading">
      <header className="fleet-heading">
        <div>
          <span className="eyebrow">Access control</span>
          <h1 id="staff-heading">Staff provisioning</h1>
          <p>Invite staff, review provisioning, and revoke console access.</p>
        </div>
      </header>

      {error && (
        <div className="inline-alert error" role="alert">
          <AlertTriangle size={16} />
          <span>{error}</span>
        </div>
      )}

      <div className="detail-panel">
        <h2><UserPlus size={14} /> New staff invitation</h2>
        <form className="form-grid two-column" onSubmit={submit}>
          <label className="form-field">
            <span>Email</span>
            <input type="email" required placeholder="name@example.com" value={email} onChange={(event) => setEmail(event.target.value)} />
          </label>
          <label className="form-field">
            <span>Name</span>
            <input required placeholder="Full name" value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
          </label>
          <label className="form-field">
            <span>Role</span>
            <ThemedSelect
              value={role}
              ariaLabel="Role"
              options={roles.map((item) => ({ value: item, label: formatRole(item) }))}
              onValueChange={(value) => setRole(value as StaffRole)}
            />
          </label>
          {showDealerField && (
            <label className="form-field">
              <span>Dealer ID</span>
              <input
                required
                placeholder="Dealer external ID"
                value={dealerExternalId}
                disabled={!waterFlexAdmin}
                onChange={(event) => setDealerExternalId(event.target.value)}
              />
              <small>{waterFlexAdmin ? 'Sets the dealer this account can access.' : 'Locked to your own dealer.'}</small>
            </label>
          )}
          <label className="form-field span-two">
            <span>Reason</span>
            <input required value={reason} onChange={(event) => setReason(event.target.value)} />
          </label>
          <div className="form-actions span-two">
            <button type="submit" className="button button-primary" disabled={busy}>
              <UserPlus size={15} /> Send invitation
            </button>
            {busy && <span className="form-hint">Sending invitation…</span>}
          </div>
        </form>
      </div>

      <h2 className="section-label">Active staff <small>{staff.length} console members</small></h2>
      <div className="fleet-table-shell">
        <table className="staff-table">
          <thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Dealer</th><th>Status</th><th style={{ width: 180 }}>Action</th></tr></thead>
          <tbody>
            {staff.length === 0 && <tr><td className="staff-empty" colSpan={6}>No staff members yet.</td></tr>}
            {staff.map((member) => (
              <tr key={member.id}>
                <td><span className="table-primary">{member.displayName}</span></td>
                <td><span className="table-secondary">{member.email}</span></td>
                <td>{formatRole(member.role)}</td>
                <td>{member.dealerName ?? 'WaterFlex'}</td>
                <td><span className={`reporting-badge ${STATE_TONE[member.state]}`}>{STATE_LABELS[member.state]}</span></td>
                <td>
                  {pendingMemberId === member.id ? (
                    <div className="reason-confirm">
                      <input
                        placeholder="Reason"
                        value={pendingReason}
                        onChange={(event) => setPendingReason(event.target.value)}
                        autoFocus
                      />
                      <button type="button" className="icon-button confirm-tone" title="Confirm" disabled={busy} onClick={() => void confirmToggle(member)}>
                        <Check size={14} />
                      </button>
                      <button type="button" className="icon-button cancel-tone" title="Cancel" onClick={cancelToggle}>
                        <X size={14} />
                      </button>
                    </div>
                  ) : (
                    <button
                      type="button"
                      className={`button button-secondary button-small ${member.state === 'active' ? 'danger-tone' : 'success-tone'}`}
                      disabled={busy || member.state === 'deprovisioning'}
                      onClick={() => startToggle(member)}
                    >
                      {member.state === 'active' ? <ShieldOff size={13} /> : <ShieldCheck size={13} />}
                      {member.state === 'active' ? 'Suspend' : 'Reactivate'}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <h2 className="section-label">Invitations <small>{invitations.length} pending</small></h2>
      <div className="fleet-table-shell">
        <table className="staff-table">
          <thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Status</th><th>Expires</th></tr></thead>
          <tbody>
            {invitations.length === 0 && <tr><td className="staff-empty" colSpan={5}>No pending invitations.</td></tr>}
            {invitations.map((invitation) => (
              <tr key={invitation.id}>
                <td><span className="table-primary">{invitation.displayName}</span></td>
                <td><span className="table-secondary">{invitation.email}</span></td>
                <td>{formatRole(invitation.role)}</td>
                <td><span className="reporting-badge pending">{invitation.status}</span></td>
                <td><span className="table-secondary">{new Date(invitation.expiresAtUtc).toLocaleString()}</span></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function formatRole(role: StaffRole) { return role.replace(/([A-Z])/g, ' $1').replace(/^./, (value) => value.toUpperCase()); }
