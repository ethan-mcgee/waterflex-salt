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

/**
 * Technician-facing screen for commissioning a sensor against an installation work order.
 *
 * This component drives a **two-tier state machine**:
 *
 * 1. **Pre-session tier** — before a backend commissioning session exists, navigation is purely
 *    local and tracked by the `step` state (`PreSessionStep`: 'workOrder' | 'sensor'). The
 *    technician looks up a work order, then enters a serial number and tank depth. Nothing is
 *    persisted server-side yet, so "Back"/"Continue" just flip `step`.
 * 2. **Server-driven tier** — once {@link createWorkOrderCommissioningSession} succeeds, `session`
 *    becomes non-null and control shifts entirely to `session.status` as reported by the backend
 *    (pendingSensor, activatedAwaitingHealth, awaitingFirstTelemetry, completed, or a terminal
 *    failure: expired/cancelled/failed). From this point the technician can no longer navigate
 *    backward through `step` — the UI just reflects whatever the session is currently doing,
 *    refreshed by the poller below, until it reaches a terminal state.
 *
 * The 5-item visual step rail (`RailStepId`) is not stored as its own state — it is derived from
 * `(step, session)` on every render via {@link computeRailIndex} so the two tiers can never drift
 * out of sync with the rail.
 */
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

  // Server-driven tier: while a session exists and hasn't reached a terminal state (completed,
  // or one of TERMINAL_FAILURE_STATUSES), poll the backend every POLL_INTERVAL_MS and replace
  // `session` wholesale with the latest view. This is what advances the UI through
  // pendingSensor -> activatedAwaitingHealth -> awaitingFirstTelemetry -> completed without any
  // technician input. The interval is torn down and re-created whenever the session id or status
  // changes, and stops entirely once a terminal status is reached.
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

  /** Pre-session step: looks up the work order by number and populates `workOrder`, or sets `workOrderError`. */
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

  /**
   * Transition point from the pre-session tier to the server-driven tier: creates the
   * commissioning session for the entered serial/tank depth. On success this sets `session`,
   * which hands control of the UI to `session.status` and starts the poller above.
   */
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

  /**
   * Cancels the active session server-side (best-effort) and clears local `session` state,
   * dropping the UI back to the pre-session tier at whatever `step` it last had.
   */
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

  /** Resets all local and session state so the technician can commission another sensor from scratch. */
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
            ) : session.status === 'activatedAwaitingHealth' || session.status === 'awaitingFirstTelemetry' ? (
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

/**
 * Derives the active index into `RAIL_STEPS` (0-4) from the two-tier state, so the visual rail
 * never has state of its own to fall out of sync with `step`/`session`. Pre-session tier maps
 * 'workOrder' | 'sensor' to indices 0/1; once a session exists, its status maps to the
 * remaining rail steps (wifi/activating/complete), collapsing every non-terminal, non-telemetry
 * status (e.g. pendingSensor) into index 2.
 */
function computeRailIndex(step: PreSessionStep, session: CommissioningSessionView | null): number {
  if (!session) {
    return step === 'workOrder' ? 0 : 1;
  }
  switch (session.status) {
    case 'completed':
      return 4;
    case 'awaitingFirstTelemetry':
    case 'activatedAwaitingHealth':
      return 3;
    default:
      return 2;
  }
}

/** Maps the current two-tier state to the workflow's page heading. */
function headingFor(step: PreSessionStep, session: CommissioningSessionView | null): string {
  if (!session) {
    return step === 'workOrder' ? 'Find the installation' : 'Assign a sensor';
  }
  switch (session.status) {
    case 'pendingSensor':
      return "Join the sensor's network";
    case 'awaitingFirstTelemetry':
    case 'activatedAwaitingHealth':
      return 'Finishing automatically';
    case 'completed':
      return `${session.serialNumber} is live`;
    default:
      return 'Reservation ended';
  }
}

/** Renders the small status pill in the workflow header for the current session status (or "Draft" pre-session). */
function statusPillFor(session: CommissioningSessionView | null) {
  if (!session) {
    return <span className="status-pill draft"><span /> Draft</span>;
  }
  switch (session.status) {
    case 'pendingSensor':
      return <span className="status-pill pending"><span /> Reserved</span>;
    case 'awaitingFirstTelemetry':
    case 'activatedAwaitingHealth':
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
