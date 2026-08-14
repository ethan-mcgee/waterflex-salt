import { AlertTriangle, Droplets, ExternalLink, Gauge, HelpCircle, RadioTower } from 'lucide-react';
import { Link, Navigate, NavLink, Route, Routes, useLocation } from 'react-router-dom';
import { DevelopmentIdentitySelector } from './development/DevelopmentIdentity';
import DeviceDetailPage from './ops/DeviceDetailPage';
import FleetPage from './ops/FleetPage';
import AlertsPage from './ops/AlertsPage';
import ProvisioningWorkflow from './provisioning/ProvisioningWorkflow';

export default function App() {
  const location = useLocation();
  const section = location.pathname.startsWith('/provision') ? 'Sensor provisioning' : 'Fleet operations';

  return (
    <div className="app-shell">
      <header className="app-header">
        <Link className="brand" to="/fleet" aria-label="WaterFlex FieldOps home">
          <span className="brand-mark"><Droplets size={22} /></span>
          <span><strong>WaterFlex</strong><small>FieldOps</small></span>
        </Link>
        <nav className="primary-nav" aria-label="FieldOps sections">
          <NavLink to="/fleet"><Gauge size={16} /> Fleet</NavLink>
          <NavLink to="/alerts"><AlertTriangle size={16} /> Alerts</NavLink>
          <NavLink to="/provision"><RadioTower size={16} /> Provision</NavLink>
        </nav>
        <div className="header-context">
          <span className="environment-badge">Pilot</span>
          <span className="header-divider" />
          <span>{section}</span>
        </div>
        <nav className="header-actions" aria-label="Resources">
          <DevelopmentIdentitySelector />
          {import.meta.env.DEV && (
            <a href="http://localhost:5188/swagger" target="_blank" rel="noreferrer"><ExternalLink size={17} /> API</a>
          )}
          <a href="mailto:support@waterflex.com" title="Field support"><HelpCircle size={19} /><span>Support</span></a>
        </nav>
      </header>
      <main className="app-main">
        <Routes>
          <Route index element={<Navigate to="/fleet" replace />} />
          <Route path="fleet" element={<FleetPage />} />
          <Route path="fleet/:deviceId" element={<DeviceDetailPage />} />
          <Route path="alerts" element={<AlertsPage />} />
          <Route path="provision" element={<ProvisioningWorkflow />} />
          <Route path="*" element={<Navigate to="/fleet" replace />} />
        </Routes>
      </main>
    </div>
  );
}
