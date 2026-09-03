import '@testing-library/jest-dom/vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import FactoryProvisioningPage from './FactoryProvisioningPage';

const configuration = {
  enabled: true,
  model: 'Arduino Nano ESP32',
  approvedFirmwareVersion: 'wf-uart-pilot-0.1',
  configurationVersion: 'factory-v2',
  helperBaseUrl: 'http://127.0.0.1:8765',
  helperProtocolVersion: '2',
};

const detected = {
  status: 'detected',
  devices: [{ port: 'COM4', description: 'Arduino Nano ESP32' }],
};

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.useRealTimers();
  window.localStorage.clear();
});

describe('FactoryProvisioningPage', () => {
  it('enables provisioning only after the approved local helper responds', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url === 'http://127.0.0.1:8765/v1/health') return json({ status: 'ready', protocolVersion: '2' });
      if (url === 'http://127.0.0.1:8765/v1/devices') return json(detected);
      if (url === '/api/v1/factory/devices/active') return notFound();
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect(screen.getByText('Checking for sensor')).toBeInTheDocument();
    expect(await screen.findByText('Connected')).toBeInTheDocument();
    expect(await screen.findByText('Nano detected')).toBeInTheDocument();
    expect(cardIcon('Local helper')).toHaveClass('ready');
    expect(cardIcon('Connected unit')).toHaveClass('ready');
    expect(cardIcon('Acceptance')).not.toHaveClass('ready');
    expect(screen.getByText(/COM4 — Arduino Nano ESP32/)).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: /provision sensor/i })).toBeEnabled();
  });

  it.each([
    [{ status: 'none', devices: [] }, 'No Nano detected'],
    [{ status: 'multiple', devices: [
      { port: 'COM4', description: 'Arduino Nano ESP32' },
      { port: 'COM7', description: 'ESP32 USB JTAG' },
    ] }, 'Multiple Nanos detected — disconnect all but one'],
  ])('blocks provisioning when detection is $status', async (deviceResponse, heading) => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '2' });
      if (url.endsWith('/v1/devices')) return json(deviceResponse);
      if (url === '/api/v1/factory/devices/active') return notFound();
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect(await screen.findByText(heading)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /provision sensor/i })).toBeDisabled();
  });

  it('polls once per second and reflects unplug and replug transitions', async () => {
    let deviceResponse: unknown = detected;
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '2' });
      if (url.endsWith('/v1/devices')) return json(deviceResponse);
      if (url === '/api/v1/factory/devices/active') return notFound();
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);
    expect(await screen.findByText('Nano detected')).toBeInTheDocument();
    expect(cardIcon('Connected unit')).toHaveClass('ready');
    deviceResponse = { status: 'none', devices: [] };
    await waitFor(() => expect(screen.getByText('No Nano detected')).toBeInTheDocument(), { timeout: 1600 });
    expect(cardIcon('Connected unit')).not.toHaveClass('ready');
    deviceResponse = detected;
    await waitFor(() => expect(screen.getByText('Nano detected')).toBeInTheDocument(), { timeout: 1600 });
    expect(cardIcon('Connected unit')).toHaveClass('ready');
    expect(fetchMock.mock.calls.filter(([input]) => String(input).endsWith('/v1/health'))).toHaveLength(1);
    expect(fetchMock.mock.calls.filter(([input]) => String(input).endsWith('/v1/devices')).length).toBeGreaterThanOrEqual(3);
  });

  it('turns the acceptance icon green after all checks pass', async () => {
    window.localStorage.setItem('waterflex-factory-active-job', 'factory-complete-job-0001');
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '2' });
      if (url.endsWith('/v1/devices')) return json(detected);
      if (url === '/api/v1/factory/devices/active') return notFound();
      if (url.endsWith('/v1/jobs/factory-complete-job-0001')) return json({
        idempotencyKey: 'factory-complete-job-0001',
        bootstrapCredentialId: 'wf_boot_complete_0001',
        bootstrapSecretHash: 'safe-hash',
        status: 'completed',
        message: 'All local factory acceptance checks passed.',
        serialNumber: 'WF-NANO-0042',
        evidence: { firmware: true, identity: true, portal: true, sensor: true },
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

    expect(await screen.findByText('All checks passed')).toBeInTheDocument();
    expect(cardIcon('Acceptance')).toHaveClass('ready');
  });

  it('rejects protocol v1 with an update-helper message and does not query devices', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '1' });
      if (url === '/api/v1/factory/devices/active') return notFound();
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect((await screen.findAllByText('Update the factory helper. Protocol 1 is installed; protocol 2 is required.')).length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /provision sensor/i })).toBeDisabled();
    expect(fetchMock.mock.calls.some(([input]) => String(input).endsWith('/v1/devices'))).toBe(false);
  });

  it('shows endpoint failures and keeps provisioning disabled', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '2' });
      if (url.endsWith('/v1/devices')) return Promise.resolve(new Response('{}', { status: 503 }));
      if (url === '/api/v1/factory/devices/active') return notFound();
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect((await screen.findAllByText('Factory helper request failed (503).')).length).toBeGreaterThan(0);
    expect(screen.getByText('Detection unavailable')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /provision sensor/i })).toBeDisabled();
  });

  it('shows an environment-level disable without contacting the helper', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(json({ ...configuration, enabled: false }));

    render(<FactoryProvisioningPage />);

    expect(await screen.findByText('Factory provisioning is disabled in this environment.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /provision sensor/i })).toBeDisabled();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('resumes server registration from a protected prepared helper job', async () => {
    window.localStorage.setItem('waterflex-factory-active-job', 'factory-resume-job-0001');
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url === 'http://127.0.0.1:8765/v1/health') return json({ status: 'ready', protocolVersion: '2' });
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

    expect(await screen.findByText('WF-NANO-0042')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/factory/devices', expect.objectContaining({ method: 'POST' }));
    const startCall = fetchMock.mock.calls.find(([callInput]) =>
      String(callInput).endsWith('/v1/jobs/factory-resume-job-0001/start'));
    expect(JSON.parse(String(startCall?.[1]?.body))).toEqual(
      expect.objectContaining({ flashAuthorizationToken: 'wf_flash_resume_0001.resume-secret' }));
    expect(screen.getByText('Waiting for sensor')).toBeInTheDocument();
  });

  it('keeps retry disabled when a quarantined job does not have exactly one detected Nano', async () => {
    window.localStorage.setItem('waterflex-factory-active-job', 'factory-quarantined-job-0001');
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url.endsWith('/v1/health')) return json({ status: 'ready', protocolVersion: '2' });
      if (url.endsWith('/v1/devices')) return json({ status: 'none', devices: [] });
      if (url === '/api/v1/factory/devices/active') return notFound();
      if (url.endsWith('/v1/jobs/factory-quarantined-job-0001')) return json({
        idempotencyKey: 'factory-quarantined-job-0001',
        bootstrapCredentialId: 'wf_boot_quarantined_0001',
        bootstrapSecretHash: 'safe-hash',
        status: 'failed',
        message: 'Sensor verification failed',
        serialNumber: 'WF-NANO-0042',
        evidence: { firmware: true, identity: true, portal: true, sensor: false },
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

    expect(await screen.findByText('WF-NANO-0042')).toBeInTheDocument();
    expect(screen.getByText('Sensor verification failed')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /retry this sensor/i })).toBeDisabled();
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

function cardIcon(kicker: string) {
  const icon = screen.getByText(kicker).closest('.factory-card')?.querySelector('.factory-card-icon');
  if (!(icon instanceof HTMLElement)) throw new Error(`Missing icon for ${kicker}`);
  return icon;
}
