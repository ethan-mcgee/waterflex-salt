import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import DeviceDetailPage from './DeviceDetailPage';
import { getFleetDevice, getFleetReadings } from './api';
import type { FleetDeviceDetail, FleetReading } from './types';

vi.mock('./api', () => ({
  getFleetDevice: vi.fn(),
  getFleetReadings: vi.fn(),
}));

vi.mock('../development/DevelopmentIdentity', () => ({
  useDevelopmentIdentity: () => ({ selectedUserId: 'wf-ops-alex' }),
}));

const detail: FleetDeviceDetail = {
  device: {
    deviceId: 'd281bf96-5d9e-405e-a7cc-872ff0152b35',
    installationId: 'installation-id',
    serialNumber: 'WF-TEST-001',
    hardwareId: 'HARDWARE001',
    model: 'Nano ESP32',
    lifecycleStatus: 'Active',
    dealerExternalId: null,
    dealerName: 'WaterFlex',
    customerDisplayName: 'Test Customer',
    accountNumber: '1001',
    locationDisplayName: 'Utility room',
    addressSummary: null,
    tankLabel: 'Softener',
    capacityPounds: 200,
    fillPercent: 75,
    isBelowThreshold: false,
    reportingStatus: 'reporting',
    lastReportedAtUtc: '2026-08-13T12:00:00Z',
    rawDistanceMm: 500,
    quality: 90,
    wifiRssiDbm: -55,
    firmwareVersion: '1.0.0',
    errorFlags: [],
  },
  registeredAtUtc: '2026-08-01T12:00:00Z',
  commissionedAtUtc: '2026-08-01T12:00:00Z',
  installedAtUtc: '2026-08-01T12:00:00Z',
  installedBy: 'Alex Morgan',
  waterFlexWorkOrderId: null,
  calibrationVersion: 1,
  tankDepthMm: 1500,
  commissioningDistanceMm: 500,
  calibrationEffectiveFromUtc: '2026-08-01T12:00:00Z',
  hasActiveCredential: true,
  credentialLastUsedAtUtc: null,
  rowVersion: '1',
};

const reading: FleetReading = {
  readingId: 1,
  timestampUtc: '2026-08-13T12:00:00Z',
  usesObservedTimestamp: true,
  receivedAtUtc: '2026-08-13T12:00:00Z',
  fillPercent: 75,
  rawDistanceMm: 500,
  quality: 90,
  wifiRssiDbm: -55,
  firmwareVersion: '1.0.0',
  errorFlags: [],
};

describe('DeviceDetailPage', () => {
  afterEach(cleanup);

  beforeEach(() => {
    vi.mocked(getFleetDevice).mockReset();
    vi.mocked(getFleetReadings).mockReset();
  });

  it('keeps sensor detail visible when reading history fails', async () => {
    vi.mocked(getFleetDevice).mockResolvedValue(detail);
    vi.mocked(getFleetReadings).mockRejectedValue(new TypeError('Load failed'));

    renderPage();

    expect(await screen.findByRole('heading', { name: 'WF-TEST-001' })).toBeVisible();
    expect(await screen.findByText('Reading history unavailable')).toBeVisible();
    expect(screen.queryByText('Sensor detail unavailable')).not.toBeInTheDocument();
  });

  it('retries only reading history and displays the recovered result', async () => {
    vi.mocked(getFleetDevice).mockResolvedValue(detail);
    vi.mocked(getFleetReadings)
      .mockRejectedValueOnce(new TypeError('Load failed'))
      .mockResolvedValueOnce([reading]);

    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(getFleetReadings).toHaveBeenCalledTimes(2));
    expect(getFleetDevice).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(screen.queryByText('Reading history unavailable')).not.toBeInTheDocument());
    expect(screen.getAllByText('75.0%').length).toBeGreaterThan(1);
  });

  it('uses the full-page error when sensor detail fails', async () => {
    vi.mocked(getFleetDevice).mockRejectedValue(new Error('Not found'));
    vi.mocked(getFleetReadings).mockResolvedValue([]);

    renderPage();

    expect(await screen.findByText('Sensor detail unavailable')).toBeVisible();
    expect(screen.getByText('Not found')).toBeVisible();
  });

  it('loads the 24-hour range initially', async () => {
    vi.mocked(getFleetDevice).mockResolvedValue(detail);
    vi.mocked(getFleetReadings).mockResolvedValue([]);

    renderPage();

    await waitFor(() => expect(getFleetReadings).toHaveBeenCalledWith(
      detail.device.deviceId,
      '24h',
      expect.any(AbortSignal),
    ));
  });
});

function renderPage() {
  return render(
    <MemoryRouter initialEntries={[`/fleet/${detail.device.deviceId}`]}>
      <Routes>
        <Route path="/fleet/:deviceId" element={<DeviceDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}
