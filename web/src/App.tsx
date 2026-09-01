import { AlertTriangle, CircuitBoard, Droplets, Eye, ExternalLink, Gauge, LogOut, RadioTower, Users } from 'lucide-react';
import { useState } from 'react';
import { Link, Navigate, NavLink, Route, Routes, useLocation } from 'react-router-dom';
import { DevelopmentIdentitySelector, useDevelopmentIdentity } from './development/DevelopmentIdentity';
import type { StaffRole } from './development/DevelopmentIdentity';
import ThemedSelect from './components/ThemedSelect';
import DeviceDetailPage from './ops/DeviceDetailPage';
import FleetPage from './ops/FleetPage';
import AlertsPage from './ops/AlertsPage';
import ProvisioningWorkflow from './provisioning/ProvisioningWorkflow';
import StaffPage from './staff/StaffPage';
import FactoryProvisioningPage from './factory/FactoryProvisioningPage';

const VIEW_AS_STORAGE_KEY = 'waterflex-view-as-role';
const VIEW_AS_OPTIONS: { value: string; label: string }[] = [
  { value: 'reset', label: 'My view (Administrator)' },
  { value: 'waterFlexEmployee', label: 'WaterFlex Employee' },
  { value: 'factoryWorker', label: 'Factory Worker' },
  { value: 'dealerAdministrator', label: 'Dealer Administrator' },
  { value: 'dealerTechnician', label: 'Dealer Technician' },
];
const VIEW_AS_LABELS: Record<string, string> = {
  waterFlexEmployee: 'WaterFlex Employee',
  factoryWorker: 'Factory Worker',
  dealerAdministrator: 'Dealer Administrator',
  dealerTechnician: 'Dealer Technician',
};

export default function App() {
  const location = useLocation();
  const { currentUser } = useDevelopmentIdentity();
  const isAdmin = currentUser?.role === 'waterFlexAdministrator';
  const [viewAsRole, setViewAsRole] = useState<StaffRole | null>(
    () => (window.sessionStorage.getItem(VIEW_AS_STORAGE_KEY) as StaffRole | null));
  const effectiveRole = isAdmin && viewAsRole ? viewAsRole : currentUser?.role;

  function changeViewAsRole(value: string) {
    const next = value === 'reset' ? null : (value as StaffRole);
    if (next) window.sessionStorage.setItem(VIEW_AS_STORAGE_KEY, next);
    else window.sessionStorage.removeItem(VIEW_AS_STORAGE_KEY);
    setViewAsRole(next);
  }

  const canOperateFleet = effectiveRole === 'waterFlexEmployee' || effectiveRole === 'waterFlexAdministrator' || effectiveRole === 'dealerAdministrator';
  const canProvision = effectiveRole === 'dealerTechnician' || effectiveRole === 'dealerAdministrator';
  const canManageStaff = effectiveRole === 'dealerAdministrator' || effectiveRole === 'waterFlexAdministrator';
  const canFactoryProvision = effectiveRole === 'factoryWorker' || effectiveRole === 'waterFlexAdministrator';
  const home = canOperateFleet ? '/fleet' : canFactoryProvision ? '/factory/provision' : canProvision ? '/provision' : '/fleet';
  const section = location.pathname.startsWith('/factory') ? 'Factory provisioning' : location.pathname.startsWith('/provision') ? 'Sensor provisioning' : location.pathname.startsWith('/staff') ? 'Staff administration' : 'Fleet operations';

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
          {canFactoryProvision && <NavLink to="/factory/provision"><CircuitBoard size={16} /> Factory</NavLink>}
          {canManageStaff && <NavLink to="/staff"><Users size={16} /> Staff</NavLink>}
        </nav>
        <div className="header-context">
          <span className="environment-badge">Pilot</span>
          <span className="header-divider" />
          <span>{section}</span>
        </div>
        <nav className="header-actions" aria-label="Resources">
          {isAdmin && (
            <div className="identity-selector" title="Preview a different role's view">
              <Eye size={16} />
              <ThemedSelect
                value={viewAsRole ?? 'reset'}
                options={VIEW_AS_OPTIONS}
                ariaLabel="Preview as role"
                onValueChange={changeViewAsRole}
              />
            </div>
          )}
          <DevelopmentIdentitySelector />
          {import.meta.env.DEV && (
            <a href="http://localhost:5188/swagger" target="_blank" rel="noreferrer"><ExternalLink size={17} /> API</a>
          )}
          {/* Signs out of the Cloudflare Access session cookie so a different invited email/role can sign back in. */}
          <a href="/cdn-cgi/access/logout" title="Sign out"><LogOut size={19} /><span>Sign out</span></a>
        </nav>
      </header>
      {isAdmin && viewAsRole && (
        <div className="view-as-banner">
          <Eye size={14} /> Previewing as {VIEW_AS_LABELS[viewAsRole]} — data and actions still use your administrator access.
          <button type="button" onClick={() => changeViewAsRole('reset')}>Return to my view</button>
        </div>
      )}
      <main className="app-main">
        <Routes>
          <Route index element={<Navigate to={home} replace />} />
          <Route path="fleet" element={canOperateFleet ? <FleetPage /> : <Navigate to={home} replace />} />
          <Route path="fleet/:deviceId" element={canOperateFleet ? <DeviceDetailPage /> : <Navigate to={home} replace />} />
          <Route path="alerts" element={canOperateFleet ? <AlertsPage /> : <Navigate to={home} replace />} />
          <Route path="provision" element={canProvision ? <ProvisioningWorkflow /> : <Navigate to={home} replace />} />
          <Route path="factory/provision" element={canFactoryProvision ? <FactoryProvisioningPage /> : <Navigate to={home} replace />} />
          <Route path="staff" element={canManageStaff ? <StaffPage /> : <Navigate to={home} replace />} />
          <Route path="*" element={<Navigate to={home} replace />} />
        </Routes>
      </main>
    </div>
  );
}
