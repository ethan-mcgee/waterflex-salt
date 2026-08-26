import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import AlertsPage from './AlertsPage';
import type { AlertDetail, AlertListItem } from './types';

const getAlerts = vi.fn();
const getAlert = vi.fn();
const transitionAlert = vi.fn();

vi.mock('./api', () => ({
  getAlerts: (...args: unknown[]) => getAlerts(...args),
  getAlert: (...args: unknown[]) => getAlert(...args),
  transitionAlert: (...args: unknown[]) => transitionAlert(...args),
}));

vi.mock('../development/DevelopmentIdentity', () => ({
  useDevelopmentIdentity: () => ({ selectedUserId: 'wf-ops-alex' }),
}));

const alert: AlertListItem = {
  alertId: 'alert-1',
  deviceId: 'device-1',
  installationId: 'installation-1',
  serialNumber: 'WF-1001',
  dealerName: 'North Star Water',
  customerDisplayName: 'Pilot Customer',
  locationDisplayName: 'Main plant',
  tankLabel: 'Brine tank',
  status: 'open',
  openedAtUtc: '2026-08-14T18:00:00Z',
  lastEvidenceAtUtc: '2026-08-14T18:05:00Z',
  lastEvidenceFillPercent: 31.5,
  rowVersion: '7',
  ticketStatus: 'created',
  ticketExternalId: 'STUB-low-salt-alert:alert-1',
};

const detail: AlertDetail = {
  alert: { ...alert, status: 'acknowledged', rowVersion: '8' },
  auditHistory: [{
    id: 1,
    eventType: 'acknowledged',
    actorType: 'staff',
    actorId: 'wf-ops-alex',
    reason: null,
    telemetryReadingId: null,
    fillPercent: 31.5,
    occurredAtUtc: '2026-08-14T18:10:00Z',
  }],
};

describe('AlertsPage', () => {
  beforeEach(() => {
    getAlerts.mockResolvedValue({ items: [alert], totalCount: 1, page: 1, pageSize: 50, deadLetterCount: 2 });
    getAlert.mockResolvedValue(detail);
    transitionAlert.mockResolvedValue(detail);
  });

  it('shows dead-letter visibility and audited acknowledgement', async () => {
    render(<AlertsPage />);

    expect(await screen.findByText('WF-1001')).toBeInTheDocument();
    expect(screen.getByText(/1 alerts · 2 dead letters/)).toBeInTheDocument();
    fireEvent.click(screen.getByTitle('Acknowledge'));

    await waitFor(() => expect(transitionAlert).toHaveBeenCalledWith(alert, 'acknowledge', undefined));
    expect(await screen.findByText('Alert audit history')).toBeInTheDocument();
    expect(screen.getByText(/acknowledged/)).toBeInTheDocument();
  });
});
