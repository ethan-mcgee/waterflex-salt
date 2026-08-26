import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import AlertsPage from './AlertsPage';
import type { AlertDetail, AlertListItem, DeliveryTicketDetail } from './types';

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

const ticket: DeliveryTicketDetail = {
  status: 'created',
  externalTicketId: 'STUB-low-salt-alert:alert-1',
  requestedAtUtc: '2026-08-14T18:05:00Z',
  externalCreatedAtUtc: '2026-08-14T18:06:00Z',
  resolvedAtUtc: null,
  lastError: null,
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
  ticket,
};

describe('AlertsPage', () => {
  afterEach(() => {
    cleanup();
  });

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

  it('renders alert and ticket status badges', async () => {
    render(<AlertsPage />);

    expect(await screen.findByText('Open')).toBeInTheDocument();
    expect(screen.getByText('Created')).toBeInTheDocument();
  });

  it('shows delivery ticket detail, including dates, when an alert is opened', async () => {
    render(<AlertsPage />);

    fireEvent.click(await screen.findByText('Open'));

    const heading = await screen.findByRole('heading', { name: 'Delivery ticket' });
    const panel = within(heading.closest('.ticket-detail') as HTMLElement);
    expect(panel.getByText('STUB-low-salt-alert:alert-1')).toBeInTheDocument();
    expect(panel.getByText('Requested')).toBeInTheDocument();
    expect(panel.getByText('Created in RouteFlex')).toBeInTheDocument();
    expect(panel.queryByText('Resolved')).not.toBeInTheDocument();
  });

  it('surfaces the delivery ticket error when it has failed', async () => {
    getAlert.mockResolvedValue({
      ...detail,
      ticket: {
        status: 'failed',
        externalTicketId: null,
        requestedAtUtc: '2026-08-14T18:05:00Z',
        externalCreatedAtUtc: null,
        resolvedAtUtc: null,
        lastError: 'RouteFlex gateway returned HTTP 503.',
      },
    });
    render(<AlertsPage />);

    fireEvent.click(await screen.findByText('Open'));

    expect(await screen.findByText('Not yet created')).toBeInTheDocument();
    expect(screen.getByText('RouteFlex gateway returned HTTP 503.')).toBeInTheDocument();
  });

  it('pages through alerts using the Previous/Next controls', async () => {
    getAlerts.mockResolvedValue({ items: [alert], totalCount: 120, page: 1, pageSize: 50, deadLetterCount: 0 });
    render(<AlertsPage />);

    expect(await screen.findByText('Page 1 of 3')).toBeInTheDocument();
    expect(screen.getByLabelText('Previous page')).toBeDisabled();
    expect(screen.getByLabelText('Next page')).not.toBeDisabled();

    fireEvent.click(screen.getByLabelText('Next page'));
    await waitFor(() => expect(getAlerts).toHaveBeenLastCalledWith(undefined, 2, expect.anything()));
    expect(await screen.findByText('Page 2 of 3')).toBeInTheDocument();
    expect(screen.getByLabelText('Previous page')).not.toBeDisabled();
  });
});
