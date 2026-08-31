import {
  ArrowLeft,
  ArrowRight,
  Building2,
  Check,
  CheckCircle2,
  Cpu,
  Gauge,
  LoaderCircle,
  MapPin,
  Search,
  Wifi,
  Wrench,
  XCircle,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import {
  ApiError,
  cancelCommissioningSession,
  createWorkOrderCommissioningSession,
  getCommissioningSession,
  getInstallationWorkOrder,
} from './bootstrapApi';
import ConfirmationStep from './ConfirmationStep';
import SerialEntryStep from './SerialEntryStep';
import SessionPollingStep from './SessionPollingStep';
import type { CommissioningSessionStatus, CommissioningSessionView, InstallationWorkOrderView } from './types';
import WifiHandoffStep from './WifiHandoffStep';
import WorkOrderStep from './WorkOrderStep';

type PreSessionStep = 'workOrder' | 'sensor';
type RailStepId = 'workOrder' | 'sensor' | 'wifi' | 'activating' | 'complete';

interface RailStep {
  id: RailStepId;
  shortLabel: string;
  icon: LucideIcon;
}

const RAIL_STEPS: RailStep[] = [
  { id: 'workOrder', shortLabel: 'Find job', icon: Search },
  { id: 'sensor', shortLabel: 'Assign sensor', icon: Cpu },
  { id: 'wifi', shortLabel: 'Connect Wi-Fi', icon: Wifi },
  { id: 'activating', shortLabel: 'Activating', icon: LoaderCircle },
  { id: 'complete', shortLabel: 'Complete', icon: CheckCircle2 },
];

const TERMINAL_FAILURE_STATUSES: CommissioningSessionStatus[] = ['expired', 'cancelled', 'failed'];
const POLL_INTERVAL_MS = 4000;

const FAILURE_MESSAGES: Record<string, string> = {
  expired: 'This reservation expired before the sensor reported in. Reserve the sensor again to try once more.',
  cancelled: 'This reservation was cancelled.',
  failed: 'The sensor could not activate. Reserve the sensor again to try once more.',
};

export default function ProvisioningWorkflow() {
  const [step, setStep] = useState<PreSessionStep>('workOrder');

  const [workOrderNumber, setWorkOrderNumber] = useState('');
  const [workOrder, setWorkOrder] = useState<InstallationWorkOrderView | null>(null);
  const [workOrderLoading, setWorkOrderLoading] = useState(false);
  const [workOrderError, setWorkOrderError] = useState('');

  const [serialNumber, setSerialNumber] = useState('');
  const [tankDepth, setTankDepth] = useState('150');
  const [creatingSession, setCreatingSession] = useState(false);
  const [sensorError, setSensorError] = useState('');

  const [session, setSession] = useState<CommissioningSessionView | null>(null);

  useEffect(() => {
    window.scrollTo({ top: 0 });
  }, [step, session?.status]);

  useEffect(() => {
    if (!session || session.status === 'completed' || TERMINAL_FAILURE_STATUSES.includes(session.status)) {
      return;
    }
    const sessionId = session.sessionId;
    const interval = window.setInterval(async () => {
      try {
        setSession(await getCommissioningSession(sessionId));
      } catch {
        // Transient network hiccup — the next tick retries.
      }
    }, POLL_INTERVAL_MS);
    return () => window.clearInterval(interval);
  }, [session?.sessionId, session?.status]);

  async function lookupWorkOrder() {
    const number = workOrderNumber.trim();
    if (!number) {
      return;
    }
    setWorkOrderLoading(true);
    setWorkOrderError('');
    setWorkOrder(null);
    try {
      setWorkOrder(await getInstallationWorkOrder(number));
    } catch (error) {
      setWorkOrderError(error instanceof ApiError ? error.message : 'Unable to look up that work order.');
    } finally {
      setWorkOrderLoading(false);
    }
  }

  async function reserveSensor() {
    if (!workOrder) {
      return;
    }
    const depth = Number(tankDepth);
    setCreatingSession(true);
    setSensorError('');
    try {
      setSession(await createWorkOrderCommissioningSession({
        workOrderNumber: workOrder.workOrderNumber,
        serialNumber: serialNumber.trim(),
        tankLocation: workOrder.tankLocation,
        tankDepthCm: depth,
      }));
    } catch (error) {
      setSensorError(error instanceof ApiError ? error.message : 'Unable to reserve that sensor.');
    } finally {
      setCreatingSession(false);
    }
  }

  async function cancelReservation() {
    if (!session) {
      return;
    }
    try {
      await cancelCommissioningSession(session.sessionId);
    } catch {
      // Best-effort — the session will still expire on its own if this fails.
    }
    setSession(null);
  }

  function restartWorkflow() {
    setStep('workOrder');
    setWorkOrderNumber('');
    setWorkOrder(null);
    setWorkOrderError('');
    setSerialNumber('');
    setTankDepth('150');
    setSensorError('');
    setSession(null);
  }

  const railIndex = computeRailIndex(step, session);
  const isTerminalFailure = session !== null && TERMINAL_FAILURE_STATUSES.includes(session.status);
  const serialValid = serialNumber.trim().length >= 4;
  const depthCm = Number(tankDepth);
  const depthValid = Number.isFinite(depthCm) && depthCm >= 10 && depthCm <= 450;

  const accountLabel = session?.customerDisplayName ?? workOrder?.customerDisplayName ?? 'Not selected';
  const locationLabel = session?.locationDisplayName ?? workOrder?.locationDisplayName ?? 'Not confirmed';
  const tankLabel = session?.tankLabel ?? workOrder?.tankLocation ?? 'Not selected';
  const sensorLabel = session?.serialNumber ?? (serialNumber.trim() || 'Not entered');

  return (
    <div className="workflow-layout">
      <nav className="step-rail" aria-label="Provisioning steps">
        <div className="rail-title">Provisioning</div>
        <ol>
          {RAIL_STEPS.map((railStep, index) => {
            const Icon = railStep.icon;
            const complete = index < railIndex;
            const active = index === railIndex;
            return (
              <li key={railStep.id} className={index < RAIL_STEPS.length - 1 ? 'has-line' : ''}>
                <button
                  type="button"
                  className={active ? 'active' : complete ? 'complete' : ''}
                  disabled={index > railIndex}
                  aria-current={active ? 'step' : undefined}
                >
                  <span className="step-icon">{complete ? <Check size={16} /> : <Icon size={17} />}</span>
                  <span>
                    <small>Step {index + 1}</small>
                    <strong>{railStep.shortLabel}</strong>
                  </span>
                </button>
              </li>
            );
          })}
        </ol>
      </nav>

      <section className="workflow-main" aria-labelledby="workflow-title">
        <header className="workflow-heading">
          <div>
            <span className="eyebrow">Installation record</span>
            <h1 id="workflow-title">{headingFor(step, session)}</h1>
          </div>
          {statusPillFor(session)}
        </header>

        <div className="step-content">
          {session ? (
            isTerminalFailure ? (
              <div className="step-section">
                <div className="inline-alert error" role="alert">
                  <XCircle size={18} />
                  <span>{FAILURE_MESSAGES[session.status] ?? 'This reservation could not be completed.'}</span>
                </div>
              </div>
            ) : session.status === 'pendingSensor' ? (
              <WifiHandoffStep session={session} />
            ) : session.status === 'awaitingFirstTelemetry' ? (
              <SessionPollingStep session={session} />
            ) : (
              <ConfirmationStep session={session} />
            )
          ) : step === 'workOrder' ? (
            <WorkOrderStep
              workOrderNumber={workOrderNumber}
              onWorkOrderNumberChange={setWorkOrderNumber}
              onLookup={lookupWorkOrder}
              loading={workOrderLoading}
              error={workOrderError}
              workOrder={workOrder}
            />
          ) : workOrder && (
            <SerialEntryStep
              workOrder={workOrder}
              serialNumber={serialNumber}
              onSerialNumberChange={setSerialNumber}
              tankDepth={tankDepth}
              onTankDepthChange={setTankDepth}
              error={sensorError}
            />
          )}
        </div>

        <footer className="workflow-actions">
          {session ? (
            isTerminalFailure ? (
              <>
                <span />
                <button type="button" className="button button-primary" onClick={restartWorkflow}>
                  Start over <ArrowRight size={18} />
                </button>
              </>
            ) : session.status === 'completed' ? (
              <>
                <span />
                <button type="button" className="button button-primary" onClick={restartWorkflow}>
                  Commission another sensor <ArrowRight size={18} />
                </button>
              </>
            ) : (
              <>
                <button type="button" className="button button-secondary danger-tone" onClick={cancelReservation}>
                  Cancel reservation
                </button>
                <span />
              </>
            )
          ) : step === 'workOrder' ? (
            <>
              <button type="button" className="button button-secondary" disabled>
                <ArrowLeft size={18} /> Back
              </button>
              <button
                type="button"
                className="button button-primary"
                disabled={!workOrder}
                onClick={() => setStep('sensor')}
              >
                Continue <ArrowRight size={18} />
              </button>
            </>
          ) : (
            <>
              <button type="button" className="button button-secondary" onClick={() => setStep('workOrder')}>
                <ArrowLeft size={18} /> Back
              </button>
              <button
                type="button"
                className="button button-primary"
                disabled={!serialValid || !depthValid || creatingSession}
                onClick={reserveSensor}
              >
                {creatingSession
                  ? <><LoaderCircle className="spin" size={18} /> Reserving…</>
                  : <>Reserve sensor <ArrowRight size={18} /></>}
              </button>
            </>
          )}
        </footer>
      </section>

      <aside className="job-context" aria-label="Current installation">
        <div className="context-heading">
          <Wrench size={18} />
          <span>Current installation</span>
        </div>
        <ContextItem icon={Building2} label="Account" value={accountLabel} />
        <ContextItem icon={MapPin} label="Location" value={locationLabel} />
        <ContextItem icon={Gauge} label="Tank" value={tankLabel} />
        <ContextItem icon={Cpu} label="Sensor" value={sensorLabel} />
        <div className="context-rule" />
        <div className="connection-state">
          <span><Wifi size={13} /> WaterFlex API</span>
          <strong>Connected</strong>
        </div>
      </aside>
    </div>
  );
}

function computeRailIndex(step: PreSessionStep, session: CommissioningSessionView | null): number {
  if (!session) {
    return step === 'workOrder' ? 0 : 1;
  }
  switch (session.status) {
    case 'completed':
      return 4;
    case 'awaitingFirstTelemetry':
      return 3;
    default:
      return 2;
  }
}

function headingFor(step: PreSessionStep, session: CommissioningSessionView | null): string {
  if (!session) {
    return step === 'workOrder' ? 'Find the installation' : 'Assign a sensor';
  }
  switch (session.status) {
    case 'pendingSensor':
      return "Join the sensor's network";
    case 'awaitingFirstTelemetry':
      return 'Finishing automatically';
    case 'completed':
      return `${session.serialNumber} is live`;
    default:
      return 'Reservation ended';
  }
}

function statusPillFor(session: CommissioningSessionView | null) {
  if (!session) {
    return <span className="status-pill draft"><span /> Draft</span>;
  }
  switch (session.status) {
    case 'pendingSensor':
      return <span className="status-pill pending"><span /> Reserved</span>;
    case 'awaitingFirstTelemetry':
      return <span className="status-pill pending"><span /> Awaiting sensor</span>;
    case 'completed':
      return <span className="status-pill success"><span /> Active</span>;
    default:
      return null;
  }
}

function ContextItem({ icon: Icon, label, value }: { icon: LucideIcon; label: string; value: string }) {
  return (
    <div className="context-item">
      <Icon size={16} />
      <span><small>{label}</small><strong>{value}</strong></span>
    </div>
  );
}
