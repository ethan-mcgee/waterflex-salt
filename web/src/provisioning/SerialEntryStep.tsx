import { Info, XCircle } from 'lucide-react';
import type { InstallationWorkOrderView } from './types';

/** Pre-session step 2: a plain text/number form for the sensor serial number and usable tank depth (no hardware connection or USB capture — values are typed in directly). */
export default function SerialEntryStep({
  workOrder,
  serialNumber,
  onSerialNumberChange,
  tankDepth,
  onTankDepthChange,
  error,
}: {
  workOrder: InstallationWorkOrderView;
  serialNumber: string;
  onSerialNumberChange: (value: string) => void;
  tankDepth: string;
  onTankDepthChange: (value: string) => void;
  error: string;
}) {
  return (
    <div className="step-section">
      <div className="section-intro">
        <h2>Reserve a factory-registered sensor</h2>
        <p>The serial ties directly to this tank. No hardware ID or device token to copy — the sensor handles the rest itself.</p>
      </div>

      <div className="form-grid two-column">
        <label className="form-field">
          <span>Serial number</span>
          <input
            type="text"
            value={serialNumber}
            onChange={(event) => onSerialNumberChange(event.target.value.toUpperCase())}
            placeholder="WF-NANO-0412"
            autoFocus
          />
          <small>Printed on the sensor label</small>
        </label>
        <label className="form-field">
          <span>Usable tank depth</span>
          <input
            type="number"
            inputMode="decimal"
            min={10}
            max={450}
            value={tankDepth}
            onChange={(event) => onTankDepthChange(event.target.value)}
            placeholder="150"
          />
          <small>Centimeters, bottom to overflow line</small>
        </label>
      </div>

      <div className="inline-alert info">
        <Info size={15} />
        <span>
          Reserves this exact serial to {workOrder.customerDisplayName} · {workOrder.locationDisplayName} for 30
          minutes. Already reserved elsewhere or the tank already has a sensor? You&rsquo;ll see that here before
          anything is created.
        </span>
      </div>

      {error && (
        <div className="inline-alert error" role="alert">
          <XCircle size={18} /> <span>{error}</span>
        </div>
      )}
    </div>
  );
}
