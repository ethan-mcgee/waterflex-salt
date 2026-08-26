import { AlertTriangle, Check, RefreshCw, ShieldCheck, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import ThemedSelect from '../components/ThemedSelect';
import { useDevelopmentIdentity } from '../development/DevelopmentIdentity';
import { getAlert, getAlerts, transitionAlert } from './api';
import type { AlertDetail, AlertListItem, AlertPageResult, LowSaltAlertStatus } from './types';

export default function AlertsPage() {
  const { selectedUserId } = useDevelopmentIdentity();
  const [status, setStatus] = useState<LowSaltAlertStatus | ''>('');
  const [result, setResult] = useState<AlertPageResult | null>(null);
  const [detail, setDetail] = useState<AlertDetail | null>(null);
  const [error, setError] = useState('');
  const [refresh, setRefresh] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setError('');
    getAlerts(status || undefined, controller.signal)
      .then(setResult)
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Unable to load alerts.');
      });
    return () => controller.abort();
  }, [refresh, selectedUserId, status]);

  async function showDetail(alert: AlertListItem) {
    try { setDetail(await getAlert(alert.alertId)); }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Unable to load alert history.'); }
  }

  async function transition(alert: AlertListItem, action: 'acknowledge' | 'approve' | 'dismiss') {
    const reason = action === 'dismiss' ? window.prompt('Reason for dismissal:')?.trim() : undefined;
    if (action === 'dismiss' && !reason) return;
    try {
      setDetail(await transitionAlert(alert, action, reason));
      setRefresh((value) => value + 1);
    } catch (transitionError) {
      setError(transitionError instanceof Error ? transitionError.message : 'Alert transition failed.');
    }
  }

  return (
    <section className="fleet-page" aria-labelledby="alerts-heading">
      <header className="fleet-heading">
        <div><span className="eyebrow">Pilot review queue</span><h1 id="alerts-heading">Low-salt alerts</h1><p>Trusted, debounced alerts awaiting WaterFlex staff review.</p></div>
        <button className="icon-button" type="button" aria-label="Refresh alerts" onClick={() => setRefresh((value) => value + 1)}><RefreshCw size={17} /></button>
      </header>
      <div className="fleet-toolbar">
        <div className="fleet-select"><ThemedSelect ariaLabel="Alert status" value={status} options={[
          { value: '', label: 'All alert states' }, { value: 'open', label: 'Open' },
          { value: 'acknowledged', label: 'Acknowledged' }, { value: 'approved', label: 'Approved' },
          { value: 'dismissed', label: 'Dismissed' }, { value: 'resolved', label: 'Resolved' },
        ]} onValueChange={(value) => setStatus(value as LowSaltAlertStatus | '')} /></div>
        <span>{result?.totalCount ?? 0} alerts · {result?.deadLetterCount ?? 0} dead letters</span>
      </div>
      {error && <div className="fleet-message error-message" role="alert"><AlertTriangle size={20} />{error}</div>}
      <div className="fleet-table-shell"><div className="fleet-table-wrap"><table className="fleet-table alerts-table">
        <thead><tr><th>Status</th><th>Sensor</th><th>Customer</th><th>Tank</th><th>Fill</th><th>Opened</th><th>Delivery ticket</th><th>Actions</th></tr></thead>
        <tbody>{result?.items.map((alert) => <tr key={alert.alertId}>
          <td><button className="table-link" type="button" onClick={() => showDetail(alert)}>{alert.status}</button></td>
          <td>{alert.serialNumber}<small>{alert.dealerName}</small></td><td>{alert.customerDisplayName}<small>{alert.locationDisplayName}</small></td>
          <td>{alert.tankLabel}</td><td>{alert.lastEvidenceFillPercent.toFixed(1)}%</td><td>{new Date(alert.openedAtUtc).toLocaleString()}</td>
          <td>{alert.ticketStatus ?? '—'}{alert.ticketExternalId ? <small>{alert.ticketExternalId}</small> : null}</td>
          <td className="alert-actions">
            {alert.status === 'open' && <button title="Acknowledge" onClick={() => transition(alert, 'acknowledge')}><Check size={15} /></button>}
            {(alert.status === 'open' || alert.status === 'acknowledged') && <button title="Approve" onClick={() => transition(alert, 'approve')}><ShieldCheck size={15} /></button>}
            {!['dismissed', 'resolved'].includes(alert.status) && <button title="Dismiss" onClick={() => transition(alert, 'dismiss')}><X size={15} /></button>}
          </td>
        </tr>)}</tbody>
      </table></div></div>
      {detail && <aside className="alert-audit"><h2>Alert audit history</h2>{detail.auditHistory.map((event) => <p key={event.id}><strong>{event.eventType}</strong> · {new Date(event.occurredAtUtc).toLocaleString()} · {event.actorId}{event.reason ? ` · ${event.reason}` : ''}</p>)}</aside>}
    </section>
  );
}
