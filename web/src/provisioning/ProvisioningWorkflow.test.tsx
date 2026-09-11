import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ProvisioningWorkflow from './ProvisioningWorkflow';

const workOrder = {
  workOrderNumber: 'WO-000123',
  customerDisplayName: 'Ada Lovelace',
  locationDisplayName: 'Main residence',
  addressSummary: '100 Main St, Madison, WI 53703',
  tankLocation: 'Old basement label',
};

const session = {
  sessionId: '11111111-1111-1111-1111-111111111111',
  deviceId: '22222222-2222-2222-2222-222222222222',
  serialNumber: 'WF-NANO-0412',
  status: 'pendingSensor',
  createdAtUtc: '2026-09-10T15:00:00Z',
  expiresAtUtc: '2026-09-10T15:30:00Z',
  dealerName: 'North Star Water Systems',
  customerDisplayName: 'Ada Lovelace',
  locationDisplayName: 'Main residence',
  addressSummary: '100 Main St, Madison, WI 53703',
  tankLabel: 'Garage softener',
  tankDepthCm: 150,
  activatedAtUtc: null,
  completedAtUtc: null,
  failureCode: null,
};

describe('ProvisioningWorkflow tank location', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
  });
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

  it('prefills, allows editing, and submits the technician tank location', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(json(workOrder))
      .mockResolvedValueOnce(json(session, 201));
    render(<ProvisioningWorkflow />);

    await lookup('WO-000123');
    fireEvent.click(screen.getByRole('button', { name: /continue/i }));
    const tankInput = screen.getByPlaceholderText('Primary softener');
    expect(tankInput).toHaveValue('Old basement label');
    fireEvent.change(tankInput, { target: { value: '  Garage softener  ' } });
    fireEvent.change(screen.getByPlaceholderText('WF-NANO-0412'), { target: { value: 'wf-nano-0412' } });
    fireEvent.click(screen.getByRole('button', { name: /reserve sensor/i }));

    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalledTimes(2));
    expect(JSON.parse(String(vi.mocked(fetch).mock.calls[1][1]?.body))).toEqual({
      workOrderNumber: 'WO-000123', serialNumber: 'WF-NANO-0412', tankLocation: 'Garage softener', tankDepthCm: 150,
    });
  });

  it('starts blank for a new work order and blocks reservation until entered', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(json({ ...workOrder, tankLocation: null, locationDisplayName: null }));
    render(<ProvisioningWorkflow />);

    await lookup('WO-000124');
    expect(screen.getAllByText('Ada Lovelace').length).toBeGreaterThan(0);
    fireEvent.click(screen.getByRole('button', { name: /continue/i }));
    fireEvent.change(screen.getByPlaceholderText('WF-NANO-0412'), { target: { value: 'WF-NANO-0412' } });
    expect(screen.getByPlaceholderText('Primary softener')).toHaveValue('');
    expect(screen.getByRole('button', { name: /reserve sensor/i })).toBeDisabled();
    fireEvent.change(screen.getByPlaceholderText('Primary softener'), { target: { value: 'Utility closet' } });
    expect(screen.getByRole('button', { name: /reserve sensor/i })).toBeEnabled();
  });

  it('resets saved tank state when the work order number changes', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(json(workOrder));
    render(<ProvisioningWorkflow />);

    await lookup('WO-000123');
    fireEvent.click(screen.getByRole('button', { name: /continue/i }));
    expect(screen.getByPlaceholderText('Primary softener')).toHaveValue('Old basement label');
    fireEvent.change(screen.getByPlaceholderText('Primary softener'), { target: { value: 'Edited value' } });
    fireEvent.click(screen.getByRole('button', { name: /back/i }));
    fireEvent.change(screen.getByPlaceholderText('WO-000123'), { target: { value: 'WO-000999' } });

    expect(screen.queryByText(/Ada Lovelace/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /continue/i })).toBeDisabled();
  });
});

async function lookup(number: string) {
  fireEvent.change(screen.getByPlaceholderText('WO-000123'), { target: { value: number } });
  fireEvent.click(screen.getByRole('button', { name: /look up/i }));
  await screen.findAllByText(/Ada Lovelace/);
}

function json(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
}
