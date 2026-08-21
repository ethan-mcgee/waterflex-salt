import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import type { StaffRole } from '../development/DevelopmentIdentity';
import { useDevelopmentIdentity } from '../development/DevelopmentIdentity';
import { changeState, createInvitation, listInvitations, listStaff, type StaffInvitation, type StaffMember } from './api';

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

  const refresh = useCallback(async () => {
    try { const [members, pending] = await Promise.all([listStaff(), listInvitations()]); setStaff(members); setInvitations(pending); setError(''); }
    catch (value) { setError(value instanceof Error ? value.message : 'Staff access data is unavailable.'); }
  }, []);
  useEffect(() => { void refresh(); }, [refresh]);

  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true);
    try {
      await createInvitation({ email, displayName, role, dealerExternalId: role.startsWith('dealer') ? dealerExternalId : null, reason });
      setEmail(''); setDisplayName(''); await refresh();
    } catch (value) { setError(value instanceof Error ? value.message : 'Invitation failed.'); }
    finally { setBusy(false); }
  }

  async function toggle(member: StaffMember) {
    const action = member.state === 'active' ? 'suspend' : 'reactivate';
    const changeReason = window.prompt(`Reason to ${action} ${member.email}?`);
    if (!changeReason) return;
    setBusy(true);
    try { await changeState(member, action, changeReason); await refresh(); }
    catch (value) { setError(value instanceof Error ? value.message : 'Staff state change failed.'); }
    finally { setBusy(false); }
  }

  const waterFlexAdmin = currentUser?.role === 'waterFlexAdministrator';
  const roles: StaffRole[] = waterFlexAdmin
    ? ['waterFlexEmployee', 'waterFlexAdministrator', 'dealerTechnician', 'dealerAdministrator']
    : ['dealerTechnician', 'dealerAdministrator'];

  return <section className="fleet-page">
    <div className="page-heading"><div><p className="eyebrow">Access control</p><h1>Staff provisioning</h1><p>Invite staff, review provisioning, and revoke console access.</p></div></div>
    {error && <div className="error-banner" role="alert">{error}</div>}
    <form className="filter-panel" onSubmit={submit}>
      <label>Email<input type="email" required value={email} onChange={(event) => setEmail(event.target.value)} /></label>
      <label>Name<input required value={displayName} onChange={(event) => setDisplayName(event.target.value)} /></label>
      <label>Role<select value={role} onChange={(event) => setRole(event.target.value as StaffRole)}>{roles.map((item) => <option key={item} value={item}>{formatRole(item)}</option>)}</select></label>
      {role.startsWith('dealer') && <label>Dealer ID<input required value={dealerExternalId} disabled={!waterFlexAdmin} onChange={(event) => setDealerExternalId(event.target.value)} /></label>}
      <label>Reason<input required value={reason} onChange={(event) => setReason(event.target.value)} /></label>
      <button className="primary-button" disabled={busy}>Send invitation</button>
    </form>
    <div className="table-shell"><table><thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Dealer</th><th>Status</th><th>Action</th></tr></thead>
      <tbody>{staff.map((member) => <tr key={member.id}><td>{member.displayName}</td><td>{member.email}</td><td>{formatRole(member.role)}</td><td>{member.dealerName ?? 'WaterFlex'}</td><td>{member.state}</td><td><button disabled={busy || member.state === 'deprovisioning'} onClick={() => void toggle(member)}>{member.state === 'active' ? 'Suspend' : 'Reactivate'}</button></td></tr>)}</tbody>
    </table></div>
    <h2>Invitations</h2>
    <div className="table-shell"><table><thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Status</th><th>Expires</th></tr></thead>
      <tbody>{invitations.map((invitation) => <tr key={invitation.id}><td>{invitation.displayName}</td><td>{invitation.email}</td><td>{formatRole(invitation.role)}</td><td>{invitation.status}</td><td>{new Date(invitation.expiresAtUtc).toLocaleString()}</td></tr>)}</tbody>
    </table></div>
  </section>;
}

function formatRole(role: StaffRole) { return role.replace(/([A-Z])/g, ' $1').replace(/^./, (value) => value.toUpperCase()); }
