import { Wifi } from 'lucide-react';
import type { CommissioningSessionView } from './types';

/** Rendered while `session.status === 'pendingSensor'`: instructs the technician to join the sensor's own Wi-Fi AP and hand its captive portal the site Wi-Fi credentials — the one manual, off-app step in the flow. */
export default function WifiHandoffStep({ session }: { session: CommissioningSessionView }) {
  const apName = session.serialNumber;

  return (
    <div className="step-section">
      <div className="inline-alert warning">
        <Wifi size={15} />
        <span><strong>Leave the app for a moment.</strong> This is the only step you do by hand — everything else finishes on its own.</span>
      </div>

      <div className="portal-panel">
        <div className="portal-chrome">192.168.4.1 · sensor portal (not this app)</div>
        <div className="portal-body">
          <ol className="mini-steps">
            <li><span className="num">1</span>Join Wi-Fi network <code>{apName}</code> — printed on the sensor label</li>
            <li><span className="num">2</span>This setup page opens automatically</li>
            <li><span className="num">3</span>Enter the site&rsquo;s 2.4 GHz Wi-Fi name and password on the sensor&rsquo;s own page</li>
          </ol>
          <div className="portal-divider" />
          <p className="step-note">
            The sensor confirms the connection itself once it joins — there is nothing further to enter here.
          </p>
        </div>
      </div>
    </div>
  );
}
