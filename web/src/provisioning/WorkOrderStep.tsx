import { Building2, Check, LoaderCircle, Search, XCircle } from 'lucide-react';
import type { InstallationWorkOrderView } from './types';

export default function WorkOrderStep({
  workOrderNumber,
  onWorkOrderNumberChange,
  onLookup,
  loading,
  error,
  workOrder,
}: {
  workOrderNumber: string;
  onWorkOrderNumberChange: (value: string) => void;
  onLookup: () => void;
  loading: boolean;
  error: string;
  workOrder: InstallationWorkOrderView | null;
}) {
  return (
    <div className="step-section">
      <div className="section-intro">
        <h2>Look up the work order</h2>
        <p>Confirms the job is eligible and belongs to your dealer before a sensor is ever reserved.</p>
      </div>

      <label className="search-field">
        <Search size={19} />
        <input
          type="text"
          value={workOrderNumber}
          onChange={(event) => onWorkOrderNumberChange(event.target.value.toUpperCase())}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && workOrderNumber.trim()) {
              onLookup();
            }
          }}
          placeholder="WO-82417"
          autoFocus
        />
        {loading
          ? <LoaderCircle className="spin" size={18} aria-label="Looking up work order" />
          : (
            <button
              type="button"
              className="button button-secondary button-small"
              disabled={!workOrderNumber.trim()}
              onClick={onLookup}
            >
              Look up
            </button>
          )}
      </label>

      {error && (
        <div className="inline-alert error" role="alert">
          <XCircle size={18} /> <span>{error}</span>
        </div>
      )}

      {workOrder && (
        <div className="selection-list">
          <div className="selection-row selected">
            <span className="selection-symbol"><Building2 size={20} /></span>
            <span className="selection-copy">
              <strong>{workOrder.customerDisplayName} — {workOrder.locationDisplayName}</strong>
              <small>
                {workOrder.addressSummary}
                {workOrder.tankLocation ? ` · ${workOrder.tankLocation}` : ''}
              </small>
            </span>
            <span className="radio-indicator"><Check size={15} /></span>
          </div>
        </div>
      )}
    </div>
  );
}
