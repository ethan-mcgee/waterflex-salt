import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { AlertTriangle, Check, ClipboardPlus, X } from 'lucide-react';
import ThemedSelect from '../components/ThemedSelect';
import { cancelWorkOrder, createWorkOrder, listWorkOrderDealers, listWorkOrders, type WorkOrder, type WorkOrderDealerOption, type WorkOrderStatus } from './api';

const STATUS_LABELS: Record<WorkOrderStatus, string> = { open: 'Open', completed: 'Completed', cancelled: 'Cancelled' };

const EMPTY_FORM = { customerName: '', locationName: '', address: '', tankLocation: '' };

export default function WorkOrdersPage({ administratorPreview = false }: { administratorPreview?: boolean }) {
  const [orders, setOrders] = useState<WorkOrder[]>([]);
  const [dealers, setDealers] = useState<WorkOrderDealerOption[]>([]);
  const [dealerExternalId, setDealerExternalId] = useState('');
  const [form, setForm] = useState(EMPTY_FORM);
  const [createdNumber, setCreatedNumber] = useState('');
  const [pendingId, setPendingId] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const refresh = useCallback(async (signal?: AbortSignal) => {
    if (administratorPreview && !dealerExternalId) { setOrders([]); return; }
    try { setOrders(await listWorkOrders(dealerExternalId || undefined, signal)); setError(''); }
    catch (value) {
      if (!signal?.aborted) setError(value instanceof Error ? value.message : 'Work orders are unavailable.');
    }
  }, [administratorPreview, dealerExternalId]);
  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal);
    return () => controller.abort();
  }, [refresh]);
  useEffect(() => {
    if (!administratorPreview) return;
    const controller = new AbortController();
    listWorkOrderDealers(controller.signal)
      .then(setDealers)
      .catch(value => {
        if (!controller.signal.aborted) setError(value instanceof Error ? value.message : 'Dealers are unavailable.');
      });
    return () => controller.abort();
  }, [administratorPreview]);

  function update(field: keyof typeof form, value: string) { setForm(current => ({ ...current, [field]: value })); }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true); setCreatedNumber(''); setError('');
    try {
      const created = await createWorkOrder(form, dealerExternalId || undefined);
      setCreatedNumber(created.workOrderNumber);
      setForm(EMPTY_FORM);
      await refresh();
    } catch (value) { setError(value instanceof Error ? value.message : 'Work order creation failed.'); }
    finally { setBusy(false); }
  }

  async function confirmCancellation(order: WorkOrder) {
    if (!reason.trim()) { setError('A cancellation reason is required.'); return; }
    setBusy(true); setError('');
    try { await cancelWorkOrder(order, reason.trim(), dealerExternalId || undefined); setPendingId(null); setReason(''); await refresh(); }
    catch (value) { setError(value instanceof Error ? value.message : 'Work order cancellation failed.'); }
    finally { setBusy(false); }
  }

  return (
    <section className="fleet-page" aria-labelledby="work-orders-heading">
      <header className="fleet-heading"><div><span className="eyebrow">Installation planning</span><h1 id="work-orders-heading">Work orders</h1><p>Create installation targets and manage orders for your dealer.</p></div></header>
      {administratorPreview && (
        <div className="detail-panel">
          <label className="form-field"><span>Active dealer</span>
            <ThemedSelect
              value={dealerExternalId}
              options={[{ value: '', label: 'Select a dealer' }, ...dealers.map(dealer => ({ value: dealer.externalId, label: dealer.displayName }))]}
              ariaLabel="Active dealer"
              disabled={busy}
              onValueChange={value => {
                setDealerExternalId(value);
                setOrders([]); setForm(EMPTY_FORM); setCreatedNumber(''); setPendingId(null); setReason(''); setError('');
              }}
            />
            <small>Select the dealer whose real work orders you want to manage. Actions remain attributed to your WaterFlex administrator identity.</small>
          </label>
        </div>
      )}
      {error && <div className="inline-alert error" role="alert"><AlertTriangle size={16} /><span>{error}</span></div>}
      {createdNumber && <div className="inline-alert success" role="status"><Check size={16} /><span>Work order created: <strong className="mono">{createdNumber}</strong>. Give this number to the installing technician.</span></div>}
      <div className="detail-panel">
        <h2><ClipboardPlus size={14} /> New installation work order</h2>
        <form className="form-grid two-column" onSubmit={submit}>
          <label className="form-field"><span>Customer name</span><input required value={form.customerName} onChange={event => update('customerName', event.target.value)} /></label>
          <label className="form-field"><span>Location name</span><input required value={form.locationName} onChange={event => update('locationName', event.target.value)} /></label>
          <label className="form-field span-two"><span>Address</span><input required value={form.address} onChange={event => update('address', event.target.value)} /></label>
          <label className="form-field span-two"><span>Tank location</span><input required value={form.tankLocation} onChange={event => update('tankLocation', event.target.value)} /><small>The technician measures tank depth during provisioning.</small></label>
          <div className="form-actions span-two"><button className="button button-primary" type="submit" disabled={busy || (administratorPreview && !dealerExternalId)}><ClipboardPlus size={15} /> Create work order</button></div>
        </form>
      </div>
      <h2 className="section-label">Dealer work orders <small>{orders.length} total</small></h2>
      <div className="fleet-table-shell">
        <table className="staff-table work-orders-table">
          <thead><tr><th>Order</th><th>Customer and site</th><th>Tank</th><th>Created</th><th>Status</th><th>Action</th></tr></thead>
          <tbody>
            {orders.length === 0 && <tr><td className="staff-empty" colSpan={6}>{administratorPreview && !dealerExternalId ? 'Select a dealer to view work orders.' : 'No work orders yet.'}</td></tr>}
            {orders.map(order => <tr key={order.id}>
              <td><span className="table-primary mono">{order.workOrderNumber}</span></td>
              <td><span className="table-primary">{order.customerName}</span><span className="table-secondary">{order.locationName} · {order.address}</span></td>
              <td>{order.tankLocation}</td>
              <td><span className="table-primary">{new Date(order.createdAtUtc).toLocaleDateString()}</span><span className="table-secondary">{order.createdBy}</span></td>
              <td><span className={`reporting-badge work-order-${order.status}`}>{STATUS_LABELS[order.status]}</span></td>
              <td>{order.status === 'open' && (pendingId === order.id ?
                <div className="reason-confirm"><input aria-label="Cancellation reason" placeholder="Cancellation reason" value={reason} onChange={event => setReason(event.target.value)} autoFocus /><button type="button" className="icon-button confirm-tone" title="Confirm cancellation" disabled={busy} onClick={() => void confirmCancellation(order)}><Check size={14} /></button><button type="button" className="icon-button cancel-tone" title="Keep order" onClick={() => { setPendingId(null); setReason(''); }}><X size={14} /></button></div>
                : <button type="button" className="button button-secondary button-small danger-tone" onClick={() => { setPendingId(order.id); setReason(''); }}>Cancel</button>)}</td>
            </tr>)}
          </tbody>
        </table>
      </div>
    </section>
  );
}
