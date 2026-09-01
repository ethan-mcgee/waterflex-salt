import { Check, Cpu, Wifi } from 'lucide-react';
import type { CommissioningSessionView } from './types';

/** Rendered while `session.status` is 'activatedAwaitingHealth' or 'awaitingFirstTelemetry': a passive progress display with no input — the parent's poller advances `session` automatically. */
export default function SessionPollingStep({ session }: { session: CommissioningSessionView }) {
  const remaining = formatRemaining(session.expiresAtUtc);

  return (
    <div className="step-section">
      <div className="section-intro">
        <h2>{session.serialNumber} is connecting</h2>
        <p>Nothing left for you to enter. The sensor is finishing activation on its own — this updates the moment it reports in.</p>
      </div>

      {remaining && (
        <div className="timer-block">
          <span className="timer">{remaining}</span>
          <span className="timer-label">Reservation expires — releases automatically, no cleanup needed</span>
        </div>
      )}

      <div className="progress-log">
        <div className="progress-row">
          <span><Wifi size={14} /> Joined site Wi-Fi</span>
          <span className="progress-tag done"><Check size={11} /> Done</span>
        </div>
        <div className="progress-row">
          <span><Cpu size={14} /> Activating with WaterFlex</span>
          <span className="progress-tag wait">In progress</span>
        </div>
      </div>
    </div>
  );
}

/** Formats the time remaining until `expiresAtUtc` as `m:ss`, or null once it has passed. */
function formatRemaining(expiresAtUtc: string): string | null {
  const remainingMs = new Date(expiresAtUtc).getTime() - Date.now();
  if (!Number.isFinite(remainingMs) || remainingMs <= 0) {
    return null;
  }
  const totalSeconds = Math.floor(remainingMs / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}
