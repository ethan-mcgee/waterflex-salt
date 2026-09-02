import '@testing-library/jest-dom/vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import FactoryProvisioningPage from './FactoryProvisioningPage';

const configuration = {
  enabled: true,
  model: 'Arduino Nano ESP32',
  approvedFirmwareVersion: 'wf-uart-pilot-0.1',
  configurationVersion: 'factory-v2',
  helperBaseUrl: 'http://127.0.0.1:8765',
  helperProtocolVersion: '1',
};

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  window.localStorage.clear();
});

describe('FactoryProvisioningPage', () => {
  it('enables provisioning only after the approved local helper responds', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url === '/api/v1/factory/configuration') return json(configuration);
      if (url === 'http://127.0.0.1:8765/v1/health') return json({ status: 'ready', protocolVersion: '1' });
      if (url === '/api/v1/factory/devices/active') return notFound();
      throw new Error(`Unexpected URL ${url}`);
    });

    render(<FactoryProvisioningPage />);

    expect(await screen.findByText('Connected')).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: /provision sensor/i })).toBeEnabled();
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
      if (url === 'http://127.0.0.1:8765/v1/health') return json({ status: 'ready', protocolVersion: '1' });
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
