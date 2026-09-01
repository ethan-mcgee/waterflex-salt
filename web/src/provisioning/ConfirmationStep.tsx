import type { CommissioningSessionView } from './types';

/** Terminal success screen, rendered once `session.status === 'completed'` — the sensor has reported its first trustworthy reading. */
export default function ConfirmationStep({ session }: { session: CommissioningSessionView }) {
  return (
    <div className="step-section">
      <div className="section-intro">
        <h2>First trustworthy reading received</h2>
        <p>
          {session.customerDisplayName} · {session.locationDisplayName} · {session.tankLabel}. No token, credential,
          or Wi-Fi field ever reached this screen.
        </p>
      </div>

      <div className="metric-row">
        <div>
          <dt>Tank depth</dt>
          <dd>{session.tankDepthCm} cm</dd>
        </div>
        <div>
          <dt>Activated</dt>
          <dd>{session.activatedAtUtc ? new Date(session.activatedAtUtc).toLocaleTimeString() : '—'}</dd>
        </div>
      </div>
    </div>
  );
}
