import '@testing-library/jest-dom/vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import FactoryProvisioningPage from './FactoryProvisioningPage';
import { deriveFactoryStep } from './workflow';

const configuration = {
  enabled: true,
  model: 'Arduino Nano ESP32',
  approvedFirmwareVersion: 'wf-uart-pilot-0.2',
  configurationVersion: 'factory-v2',
  helperBaseUrl: 'http://127.0.0.1:8765',
  helperProtocolVersion: '4',
};

const detected = {
  status: 'detected',
  devices: [{ port: 'COM4', description: 'Arduino Nano ESP32' }],
};
const helperStation = { helperVersion: '4.0.0', protocolVersion: '4', enrollmentStatus: 'enrolled', proposedWorkstationName: 'TEST-PC', stationId: '22222222-2222-2222-2222-222222222222', displayName: 'Test Station', publicKeyThumbprint: 'a'.repeat(64), publicKey: 'test', keyProviderType: 'software' };
const backendStation = { stationId: helperStation.stationId, displayName: 'Test Station', thumbprint: helperStation.publicKeyThumbprint, keyProviderType: 'software', helperVersion: '4.0.0', protocolVersion: '4', enrolledAtUtc: '2026-09-01T00:00:00Z', lastSeenAtUtc: '2026-09-01T00:00:00Z', revokedAtUtc: null };
const helperNotRunning = 'WaterFlex Factory Helper is not running. Open WaterFlex Factory Helper from the Windows Start menu, then leave it running while you provision sensors. This page will connect automatically.';

function stationResponse(url: string) {
  if (url.endsWith('/v1/station')) return json(helperStation);
  if (url === `/api/v1/factory/stations/${helperStation.stationId}`) return json(backendStation);
  return null;
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.useRealTimers();
  window.localStorage.clear();
});

describe('FactoryProvisioningPage', () => {
  it.each([
    [null, null, false, 0],
    ['prepared', null, false, 1],
    ['queued', 'registered', false, 1],
    ['flashing', 'registered', false, 1],
    ['provisioning', 'registered', false, 2],
    ['verifying', 'registered', false, 3],
    ['completed', 'registered', false, 3],
    ['failed', 'quarantined', false, 3],
    ['completed', 'provisioned', false, 4],
  ] as const)('maps helper %s and backend %s to step %s', (helperStatus, registrationStatus, hasLabel, expected) => {
    expect(deriveFactoryStep(helperStatus, registrationStatus, hasLabel)).toBe(expected);
  });

  it('enables provisioning only after the approved local helper responds', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (stationResponse(url)) return stationResponse(url)!;
      if (url === 'http://127.0.0.1:8765/v1/health') return json({ status: 'ready', protocolVersion: '4' });
      if (url === 'http://127.0.0.1:8765/v1/devices') return json(detected);
      if (url === '/api/v1/factory/devices/active') return notFound();
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect(screen.getAllByText('Checking for sensor').length).toBeGreaterThan(0);
    expect((await screen.findAllByText(/Connected/)).length).toBeGreaterThan(0);
    expect((await screen.findAllByText('Nano detected')).length).toBeGreaterThan(0);
    expect(screen.getByText(/COM4 · Arduino Nano ESP32/)).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: /provision sensor/i })).toBeEnabled();
  });

  it.each([
    [{ status: 'none', devices: [] }, 'No Nano detected'],
    [{ status: 'multiple', devices: [
      { port: 'COM4', description: 'Arduino Nano ESP32' },
      { port: 'COM7', description: 'ESP32 USB JTAG' },
    ] }, 'Multiple Nanos detected. Disconnect all but one'],
  ])('blocks provisioning when detection is $status', async (deviceResponse, heading) => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (stationResponse(url)) return stationResponse(url)!;
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '4' });
      if (url.endsWith('/v1/devices')) return json(deviceResponse);
      if (url === '/api/v1/factory/devices/active') return notFound();
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect((await screen.findAllByText(heading)).length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /provision sensor/i })).toBeDisabled();
  });

  it('polls once per second and reflects unplug and replug transitions', async () => {
    let deviceResponse: unknown = detected;
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (stationResponse(url)) return stationResponse(url)!;
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '4' });
      if (url.endsWith('/v1/devices')) return json(deviceResponse);
      if (url === '/api/v1/factory/devices/active') return notFound();
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);
    expect((await screen.findAllByText('Nano detected')).length).toBeGreaterThan(0);
    deviceResponse = { status: 'none', devices: [] };
    await waitFor(() => expect(screen.getAllByText('No Nano detected').length).toBeGreaterThan(0), { timeout: 1600 });
    deviceResponse = detected;
    await waitFor(() => expect(screen.getAllByText('Nano detected').length).toBeGreaterThan(0), { timeout: 1600 });
    expect(fetchMock.mock.calls.filter(([input]) => String(input).endsWith('/v1/health'))).toHaveLength(1);
    expect(fetchMock.mock.calls.filter(([input]) => String(input).endsWith('/v1/devices')).length).toBeGreaterThanOrEqual(3);
  });

  it('turns the acceptance icon green after all checks pass', async () => {
    window.localStorage.setItem('waterflex-factory-active-job', 'factory-complete-job-0001');
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (stationResponse(url)) return stationResponse(url)!;
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '4' });
      if (url.endsWith('/v1/devices')) return json(detected);
      if (url === '/api/v1/factory/devices/active') return notFound();
      if (url.endsWith('/v1/jobs/factory-complete-job-0001/label')) return json({
        serialNumber: 'WF-NANO-0042',
        setupNetwork: 'WaterFlex-0042',
        setupPassphrase: 'setup-secret',
        firmwareVersion: configuration.approvedFirmwareVersion,
        configurationVersion: configuration.configurationVersion,
      });
      if (url.endsWith('/v1/jobs/factory-complete-job-0001') && init?.method === 'DELETE') return json({ cleared: true });
      if (url.endsWith('/v1/jobs/factory-complete-job-0001')) return json({
        idempotencyKey: 'factory-complete-job-0001',
        bootstrapCredentialId: 'wf_boot_complete_0001',
        bootstrapSecretHash: 'safe-hash',
        status: 'completed',
        message: 'All local factory acceptance checks passed.',
        serialNumber: 'WF-NANO-0042',
        evidence: { firmware: true, identity: true, portal: true, portalStartup: true, sensor: true, sensorSampleCount: 4, sensorMinimumMm: 102, sensorMaximumMm: 108, sensorFailureCategories: [] },
        failureCode: null,
      });
      if (url === '/api/v1/factory/devices/by-idempotency/factory-complete-job-0001') return json({
        deviceId: '11111111-1111-1111-1111-111111111111',
        idempotencyKey: 'factory-complete-job-0001',
        serialNumber: 'WF-NANO-0042',
        model: configuration.model,
        registeredAtUtc: '2026-09-01T00:00:00Z',
        bootstrapCredentialId: 'wf_boot_complete_0001',
        status: 'provisioned',
        verifiedAtUtc: '2026-09-01T00:01:00Z',
        failureCode: null,
        flashAuthorizationToken: null,
      });
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect(await screen.findByRole('heading', { name: 'Label sensor' })).toBeInTheDocument();
    expect(await screen.findByText('Acceptance complete')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /print label/i })).toBeEnabled();
    fireEvent.click(screen.getByRole('button', { name: /label attached/i }));
    await waitFor(() => expect(window.localStorage.getItem('waterflex-factory-active-job')).toBeNull());
    expect(fetchMock.mock.calls.some(([input, callInit]) => String(input).endsWith('/v1/jobs/factory-complete-job-0001') && callInit?.method === 'DELETE')).toBe(true);
  });

  it('offers workstation enrollment in the guided task area', async () => {
    const unenrolledStation = { ...helperStation, enrollmentStatus: 'unenrolled', stationId: null, displayName: null };
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '4' });
      if (url.endsWith('/v1/station')) return json(unenrolledStation);
      if (url.endsWith('/v1/devices')) return json(detected);
      if (url === '/api/v1/factory/devices/active') return notFound();
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect(await screen.findByText('Enroll this workstation before connecting a production sensor.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /enroll this workstation/i })).toBeEnabled();
    expect(screen.getByRole('button', { name: /provision sensor/i })).toBeDisabled();
  });

  it('advances to Flash firmware when automated provisioning starts', async () => {
    vi.spyOn(globalThis.crypto, 'randomUUID').mockReturnValueOnce('11111111-2222-4333-8444-555555555555').mockReturnValueOnce('aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee');
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (stationResponse(url)) return stationResponse(url)!;
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '4' });
      if (url.endsWith('/v1/devices')) return json(detected);
      if (url === '/api/v1/factory/devices/active') return notFound();
      if (url.endsWith('/v1/jobs') && init?.method === 'POST') {
        const inputBody = JSON.parse(String(init.body));
        return json({ ...inputBody, bootstrapSecretHash: 'safe-hash', status: 'prepared', message: 'Prepared', serialNumber: null, evidence: null, failureCode: null });
      }
      if (url === '/api/v1/factory/devices' && init?.method === 'POST') return json({ deviceId: 'device-42', idempotencyKey: '11111111-2222-4333-8444-555555555555', serialNumber: 'WF-NANO-0042', model: configuration.model, registeredAtUtc: '2026-09-01T00:00:00Z', bootstrapCredentialId: 'credential', status: 'registered', verifiedAtUtc: null, failureCode: null, flashAuthorizationToken: 'authorization' });
      if (url.includes('/v1/jobs/') && url.endsWith('/start')) return json({ idempotencyKey: '11111111-2222-4333-8444-555555555555', bootstrapCredentialId: 'credential', bootstrapSecretHash: 'safe-hash', status: 'queued', message: 'Waiting to flash', serialNumber: 'WF-NANO-0042', evidence: null, failureCode: null });
      if (url.includes('/v1/jobs/')) return json({ idempotencyKey: '11111111-2222-4333-8444-555555555555', bootstrapCredentialId: 'credential', bootstrapSecretHash: 'safe-hash', status: 'queued', message: 'Waiting to flash', serialNumber: 'WF-NANO-0042', evidence: null, failureCode: null });
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);
    const provision = await screen.findByRole('button', { name: /provision sensor/i });
    await waitFor(() => expect(provision).toBeEnabled());
    fireEvent.click(provision);

    expect(await screen.findByRole('heading', { name: 'Flash firmware' })).toBeInTheDocument();
    expect(screen.getAllByText('WF-NANO-0042').length).toBeGreaterThan(0);
  });

  it('rejects protocol v1 with an update-helper message and does not query devices', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (stationResponse(url)) return stationResponse(url)!;
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '1' });
      if (url === '/api/v1/factory/devices/active') return notFound();
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect((await screen.findAllByText('Update the factory helper. Protocol 1 is installed; protocol 4 is required.')).length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /provision sensor/i })).toBeDisabled();
    expect(fetchMock.mock.calls.some(([input]) => String(input).endsWith('/v1/devices'))).toBe(false);
  });

  it('shows endpoint failures and keeps provisioning disabled', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (stationResponse(url)) return stationResponse(url)!;
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '4' });
      if (url.endsWith('/v1/devices')) return Promise.resolve(new Response('{}', { status: 503 }));
      if (url === '/api/v1/factory/devices/active') return notFound();
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect((await screen.findAllByText('Factory helper request failed (503).')).length).toBeGreaterThan(0);
    expect(screen.getAllByText('Detection unavailable').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /provision sensor/i })).toBeDisabled();
  });

  it('shows a distinct WaterFlex API failure and does not contact the helper', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({ detail: 'Factory API is unavailable.' }), { status: 503, headers: { 'Content-Type': 'application/json' } }));

    render(<FactoryProvisioningPage />);

    expect((await screen.findAllByText('Factory API is unavailable.')).length).toBeGreaterThan(0);
    expect(screen.getByText('Unavailable')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /provision sensor/i })).toBeDisabled();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('blocks a revoked workstation with the exact recovery action', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '4' });
      if (url.endsWith('/v1/station')) return json(helperStation);
      if (url === `/api/v1/factory/stations/${helperStation.stationId}`) return json({ ...backendStation, revokedAtUtc: '2026-09-10T12:00:00Z' });
      if (url.endsWith('/v1/devices')) return json(detected);
      if (url === '/api/v1/factory/devices/active') return notFound();
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect(await screen.findByText('This workstation has been revoked. Contact a WaterFlex administrator before continuing.')).toBeInTheDocument();
    expect(screen.getByText('Revoked')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /provision sensor/i })).toBeDisabled();
  });

  it('explains a local connection failure without exposing the browser fetch error', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url === '/api/v1/factory/devices/active') return notFound();
      if (url.endsWith('/v1/health')) throw new TypeError('Failed to fetch');
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect((await screen.findAllByText(helperNotRunning)).length).toBeGreaterThan(0);
    expect(screen.queryByText(/Failed to fetch/i)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /provision sensor/i })).toBeDisabled();
  });

  it('treats a health endpoint 404 as a helper that is not running', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url === '/api/v1/factory/devices/active') return notFound();
      if (url.endsWith('/v1/health')) return notFound();
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect((await screen.findAllByText(helperNotRunning)).length).toBeGreaterThan(0);
    expect(screen.queryByText('Factory helper request failed (404).')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /provision sensor/i })).toBeDisabled();
  });

  it('connects automatically when the helper starts after the page loads', async () => {
    let healthCalls = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url === '/api/v1/factory/devices/active') return notFound();
      if (stationResponse(url)) return stationResponse(url)!;
      if (url.endsWith('/v1/health')) {
        healthCalls += 1;
        if (healthCalls === 1) throw new TypeError('Failed to fetch');
        return json({ status: 'ready', protocolVersion: '4' });
      }
      if (url.endsWith('/v1/devices')) return json(detected);
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect((await screen.findAllByText(helperNotRunning)).length).toBeGreaterThan(0);
    await waitFor(() => expect(screen.getByRole('button', { name: /provision sensor/i })).toBeEnabled(), { timeout: 1800 });
    expect(screen.getAllByText('Nano detected').length).toBeGreaterThan(0);
    expect(screen.getByText('Test Station')).toBeInTheDocument();
    expect(healthCalls).toBe(2);
  });

  it('detects a stopped helper and reconnects after it returns', async () => {
    let healthCalls = 0;
    let deviceCalls = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url === '/api/v1/factory/devices/active') return notFound();
      if (stationResponse(url)) return stationResponse(url)!;
      if (url.endsWith('/v1/health')) {
        healthCalls += 1;
        return json({ status: 'ready', protocolVersion: '4' });
      }
      if (url.endsWith('/v1/devices')) {
        deviceCalls += 1;
        if (deviceCalls === 2) throw new TypeError('Failed to fetch');
        return json(detected);
      }
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect((await screen.findAllByText('Nano detected')).length).toBeGreaterThan(0);
    expect((await screen.findAllByText(helperNotRunning, {}, { timeout: 1800 })).length).toBeGreaterThan(0);
    await waitFor(() => expect(screen.getByRole('button', { name: /provision sensor/i })).toBeEnabled(), { timeout: 1800 });
    expect(screen.getAllByText('Nano detected').length).toBeGreaterThan(0);
    expect(healthCalls).toBe(2);
  });

  it('does not resume an active local job until the helper is available', async () => {
    window.localStorage.setItem('waterflex-factory-active-job', 'factory-paused-job-0001');
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url === '/api/v1/factory/devices/active') return notFound();
      if (url.endsWith('/v1/health')) throw new TypeError('Failed to fetch');
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect((await screen.findAllByText(helperNotRunning)).length).toBeGreaterThan(0);
    expect(fetchMock.mock.calls.some(([input]) => String(input).includes('/v1/jobs/'))).toBe(false);
    expect(screen.queryByText(/active factory job could not be resumed/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Factory helper request failed/i)).not.toBeInTheDocument();
  });

  it('shows an environment-level disable without contacting the helper', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => json({ ...configuration, enabled: false }));

    render(<FactoryProvisioningPage />);

    expect(await screen.findByText(/Factory provisioning is disabled in this environment/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /provision sensor/i })).toBeDisabled();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('resumes server registration from a protected prepared helper job', async () => {
    window.localStorage.setItem('waterflex-factory-active-job', 'factory-resume-job-0001');
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (stationResponse(url)) return stationResponse(url)!;
      if (url === 'http://127.0.0.1:8765/v1/health') return json({ status: 'ready', protocolVersion: '4' });
      if (url === 'http://127.0.0.1:8765/v1/devices') return json(detected);
      if (url.endsWith('/v1/jobs/factory-resume-job-0001') && !init?.method) return json({
        idempotencyKey: 'factory-resume-job-0001',
        bootstrapCredentialId: 'wf_boot_resume_0001',
        bootstrapSecretHash: 'safe-hash',
        status: 'prepared',
        message: 'Protected',
        serialNumber: null,
        evidence: null,
        failureCode: null,
      });
      if (url === '/api/v1/factory/devices/by-idempotency/factory-resume-job-0001') {
        return notFound();
      }
      if (url === '/api/v1/factory/devices/active') return notFound();
      if (url === '/api/v1/factory/devices' && init?.method === 'POST') return json({
        deviceId: '11111111-1111-1111-1111-111111111111',
        idempotencyKey: 'factory-resume-job-0001',
        serialNumber: 'WF-NANO-0042',
        model: configuration.model,
        registeredAtUtc: '2026-09-01T00:00:00Z',
        bootstrapCredentialId: 'wf_boot_resume_0001',
        status: 'registered',
        verifiedAtUtc: null,
        failureCode: null,
        flashAuthorizationToken: 'wf_flash_resume_0001.resume-secret',
      });
      if (url.endsWith('/v1/jobs/factory-resume-job-0001/start') && init?.method === 'POST') return json({
        idempotencyKey: 'factory-resume-job-0001',
        bootstrapCredentialId: 'wf_boot_resume_0001',
        bootstrapSecretHash: 'safe-hash',
        status: 'queued',
        message: 'Waiting for sensor',
        serialNumber: 'WF-NANO-0042',
        evidence: null,
        failureCode: null,
      });
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect((await screen.findAllByText('WF-NANO-0042')).length).toBeGreaterThan(0);
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/factory/devices', expect.objectContaining({ method: 'POST' }));
    const startCall = fetchMock.mock.calls.find(([callInput]) =>
      String(callInput).endsWith('/v1/jobs/factory-resume-job-0001/start'));
    expect(JSON.parse(String(startCall?.[1]?.body))).toEqual(
      expect.objectContaining({ flashAuthorizationToken: 'wf_flash_resume_0001.resume-secret' }));
    expect(screen.getAllByText('Waiting for sensor').length).toBeGreaterThan(0);
  });

  it('keeps retry disabled when a quarantined job does not have exactly one detected Nano', async () => {
    window.localStorage.setItem('waterflex-factory-active-job', 'factory-quarantined-job-0001');
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (stationResponse(url)) return stationResponse(url)!;
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '4' });
      if (url.endsWith('/v1/devices')) return json({ status: 'none', devices: [] });
      if (url === '/api/v1/factory/devices/active') return notFound();
      if (url.endsWith('/v1/jobs/factory-quarantined-job-0001')) return json({
        idempotencyKey: 'factory-quarantined-job-0001',
        bootstrapCredentialId: 'wf_boot_quarantined_0001',
        bootstrapSecretHash: 'safe-hash',
        status: 'failed',
        message: 'Sensor verification failed',
        serialNumber: 'WF-NANO-0042',
        evidence: { firmware: true, identity: true, portal: true, portalStartup: true, sensor: false, sensorSampleCount: 3, sensorMinimumMm: 100, sensorMaximumMm: 900, sensorFailureCategories: ['unstable'] },
        failureCode: 'factory_helper_failed',
      });
      if (url === '/api/v1/factory/devices/by-idempotency/factory-quarantined-job-0001') return json({
        deviceId: '11111111-1111-1111-1111-111111111111',
        idempotencyKey: 'factory-quarantined-job-0001',
        serialNumber: 'WF-NANO-0042',
        model: configuration.model,
        registeredAtUtc: '2026-09-01T00:00:00Z',
        bootstrapCredentialId: 'wf_boot_quarantined_0001',
        status: 'quarantined',
        verifiedAtUtc: '2026-09-01T00:01:00Z',
        failureCode: 'factory_helper_failed',
        flashAuthorizationToken: null,
      });
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect((await screen.findAllByText('WF-NANO-0042')).length).toBeGreaterThan(0);
    expect(screen.getByText('Sensor verification failed')).toBeInTheDocument();
    expect(screen.getByText('Setup portal startup').closest('.passed')).toBeInTheDocument();
    expect(screen.getByText('Sensor response').closest('.failed')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /retry this sensor/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /scrap this sensor/i })).toBeEnabled();
  });
});

function json(body: unknown) {
  return Promise.resolve(new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  }));
}

function notFound() {
  return Promise.resolve(new Response('{}', { status: 404, headers: { 'Content-Type': 'application/json' } }));
}
