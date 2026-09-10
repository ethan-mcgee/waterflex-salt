import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import App from './App';

const identity = vi.hoisted(() => ({
  currentUser: {
    userId: 'wf-admin-avery', displayName: 'Avery Patel', role: 'waterFlexAdministrator',
    dealerExternalId: null, dealerName: null,
  },
}));

vi.mock('./development/DevelopmentIdentity', () => ({
  useDevelopmentIdentity: () => ({ currentUser: identity.currentUser }),
  DevelopmentIdentitySelector: () => null,
}));
vi.mock('./workOrders/WorkOrdersPage', () => ({
  default: ({ administratorPreview }: { administratorPreview?: boolean }) =>
    <div>Work orders page preview: {String(administratorPreview)}</div>,
}));
vi.mock('./ops/FleetPage', () => ({ default: () => <div>Fleet page</div> }));
vi.mock('./ops/AlertsPage', () => ({ default: () => <div>Alerts page</div> }));
vi.mock('./ops/DeviceDetailPage', () => ({ default: () => <div>Device detail page</div> }));
vi.mock('./provisioning/ProvisioningWorkflow', () => ({ default: () => <div>Provisioning page</div> }));
vi.mock('./staff/StaffPage', () => ({ default: () => <div>Staff page</div> }));
vi.mock('./factory/FactoryProvisioningPage', () => ({ default: () => <div>Factory page</div> }));

describe('App work-order preview routing', () => {
  afterEach(() => {
    cleanup();
    window.sessionStorage.clear();
  });

  it('shows navigation and permits the route during dealer-administrator preview', () => {
    window.sessionStorage.setItem('waterflex-view-as-role', 'dealerAdministrator');

    render(<MemoryRouter initialEntries={['/work-orders']}><App /></MemoryRouter>);

    expect(screen.getByRole('link', { name: /work orders/i })).toBeInTheDocument();
    expect(screen.getByText('Work orders page preview: true')).toBeInTheDocument();
  });

  it('hides work-order management in the administrator own-role view', () => {
    render(<MemoryRouter initialEntries={['/fleet']}><App /></MemoryRouter>);

    expect(screen.queryByRole('link', { name: /work orders/i })).not.toBeInTheDocument();
  });
});
