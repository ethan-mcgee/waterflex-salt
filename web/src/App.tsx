import { AlertTriangle, Droplets, ExternalLink, Gauge, HelpCircle, RadioTower, Users } from 'lucide-react';
import { Link, Navigate, NavLink, Route, Routes, useLocation } from 'react-router-dom';
import { DevelopmentIdentitySelector, useDevelopmentIdentity } from './development/DevelopmentIdentity';
import DeviceDetailPage from './ops/DeviceDetailPage';
import FleetPage from './ops/FleetPage';
import AlertsPage from './ops/AlertsPage';
import ProvisioningWorkflow from './provisioning/ProvisioningWorkflow';
import StaffPage from './staff/StaffPage';

export default function App() {
  const location = useLocation();
  const { currentUser } = useDevelopmentIdentity();
  const canOperateFleet = currentUser?.role === 'waterFlexEmployee' || currentUser?.role === 'waterFlexAdministrator';
  const canProvision = currentUser?.role === 'dealerTechnician' || currentUser?.role === 'dealerAdministrator';
  const canManageStaff = currentUser?.role === 'dealerAdministrator' || currentUser?.role === 'waterFlexAdministrator';
  const home = canOperateFleet ? '/fleet' : canProvision ? '/provision' : '/fleet';
  const section = location.pathname.startsWith('/provision') ? 'Sensor provisioning' : location.pathname.startsWith('/staff') ? 'Staff administration' : 'Fleet operations';

  return (
    <div className="app-shell">
      <header className="app-header">
        <Link className="brand" to="/fleet" aria-label="WaterFlex FieldOps home">
          <span className="brand-mark"><Droplets size={22} /></span>
          <span><strong>WaterFlex</strong><small>FieldOps</small></span>
        </Link>
        <nav className="primary-nav" aria-label="FieldOps sections">
          {canOperateFleet && <NavLink to="/fleet"><Gauge size={16} /> Fleet</NavLink>}
          {canOperateFleet && <NavLink to="/alerts"><AlertTriangle size={16} /> Alerts</NavLink>}
          {canProvision && <NavLink to="/provision"><RadioTower size={16} /> Provision</NavLink>}
          {canManageStaff && <NavLink to="/staff"><Users size={16} /> Staff</NavLink>}
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
          <Route index element={<Navigate to={home} replace />} />
          <Route path="fleet" element={canOperateFleet ? <FleetPage /> : <Navigate to={home} replace />} />
          <Route path="fleet/:deviceId" element={canOperateFleet ? <DeviceDetailPage /> : <Navigate to={home} replace />} />
          <Route path="alerts" element={canOperateFleet ? <AlertsPage /> : <Navigate to={home} replace />} />
          <Route path="provision" element={canProvision ? <ProvisioningWorkflow /> : <Navigate to={home} replace />} />
          <Route path="staff" element={canManageStaff ? <StaffPage /> : <Navigate to={home} replace />} />
          <Route path="*" element={<Navigate to={home} replace />} />
        </Routes>
      </main>
    </div>
  );
}
