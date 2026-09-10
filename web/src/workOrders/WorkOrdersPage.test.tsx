import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import WorkOrdersPage from './WorkOrdersPage';

vi.mock('../components/ThemedSelect', () => ({
  default: ({ value, options, ariaLabel, onValueChange, disabled }: {
    value: string;
    options: { value: string; label: string }[];
    ariaLabel: string;
    onValueChange: (value: string) => void;
    disabled?: boolean;
  }) => <select aria-label={ariaLabel} value={value} disabled={disabled} onChange={event => onValueChange(event.target.value)}>
    {options.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
  </select>,
}));

const order = {
  id: '11111111-1111-1111-1111-111111111111', workOrderNumber: 'WO-000123', status: 'open',
  customerName: 'Baker Family', locationName: 'Main residence', address: '100 Main St',
  tankLocation: 'Primary softener', createdBy: 'Taylor Brooks', createdAtUtc: '2026-09-10T15:00:00Z',
  createdByActorId: 'north-star-admin', completedAtUtc: null, cancelledByActorId: null,
  cancelledBy: null, cancelledAtUtc: null, cancellationReason: null, rowVersion: 7,
};

describe('WorkOrdersPage', () => {
  beforeEach(() => vi.stubGlobal('fetch', vi.fn()));
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

  it('renders the dealer list and generated number after creation', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(json([order]))
      .mockResolvedValueOnce(json(order, 201))
      .mockResolvedValueOnce(json([order]));
    render(<WorkOrdersPage />);
    expect(await screen.findByText('WO-000123')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Customer name'), { target: { value: 'Baker Family' } });
    fireEvent.change(screen.getByLabelText('Location name'), { target: { value: 'Main residence' } });
    fireEvent.change(screen.getByLabelText('Address'), { target: { value: '100 Main St' } });
    fireEvent.change(screen.getByLabelText(/^Tank location/), { target: { value: 'Primary softener' } });
    fireEvent.click(screen.getByRole('button', { name: /create work order/i }));

    expect(await screen.findByText(/Work order created:/)).toHaveTextContent('WO-000123');
    expect(vi.mocked(fetch).mock.calls[1][0]).toBe('/api/v1/work-orders');
  });

  it('requires a cancellation reason and sends the row version', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(json([order]))
      .mockResolvedValueOnce(json({ ...order, status: 'cancelled', rowVersion: 8 }))
      .mockResolvedValueOnce(json([{ ...order, status: 'cancelled', rowVersion: 8 }]));
    render(<WorkOrdersPage />);
    await screen.findByText('WO-000123');
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    fireEvent.click(screen.getByTitle('Confirm cancellation'));
    expect(await screen.findByRole('alert')).toHaveTextContent('A cancellation reason is required.');
    fireEvent.change(screen.getByLabelText('Cancellation reason'), { target: { value: 'Customer postponed' } });
    fireEvent.click(screen.getByTitle('Confirm cancellation'));
    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalledTimes(3));
    expect(JSON.parse(String(vi.mocked(fetch).mock.calls[1][1]?.body))).toEqual({ reason: 'Customer postponed', rowVersion: 7 });
  });

  it('shows API error states', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(json({ title: 'Work orders unavailable' }, 500));
    render(<WorkOrdersPage />);
    expect(await screen.findByRole('alert')).toHaveTextContent('Work orders unavailable');
  });

  it('requires a dealer in administrator preview and scopes list, create, and cancel requests', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(json([{ externalId: 'WF-D-LAKES-WATER', displayName: 'Lakes Water Conditioning' }]))
      .mockResolvedValueOnce(json([order]))
      .mockResolvedValueOnce(json(order, 201))
      .mockResolvedValueOnce(json([order]))
      .mockResolvedValueOnce(json({ ...order, status: 'cancelled', rowVersion: 8 }))
      .mockResolvedValueOnce(json([{ ...order, status: 'cancelled', rowVersion: 8 }]));
    render(<WorkOrdersPage administratorPreview />);

    expect(screen.getByRole('button', { name: /create work order/i })).toBeDisabled();
    expect(screen.getByText('Select a dealer to view work orders.')).toBeInTheDocument();
    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalledTimes(1));
    fireEvent.change(screen.getByLabelText('Active dealer'), { target: { value: 'WF-D-LAKES-WATER' } });
    expect(await screen.findByText('WO-000123')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Customer name'), { target: { value: 'Baker Family' } });
    fireEvent.change(screen.getByLabelText('Location name'), { target: { value: 'Main residence' } });
    fireEvent.change(screen.getByLabelText('Address'), { target: { value: '100 Main St' } });
    fireEvent.change(screen.getByLabelText(/^Tank location/), { target: { value: 'Primary softener' } });
    fireEvent.click(screen.getByRole('button', { name: /create work order/i }));
    await screen.findByText(/Work order created:/);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    fireEvent.change(screen.getByLabelText('Cancellation reason'), { target: { value: 'Customer postponed' } });
    fireEvent.click(screen.getByTitle('Confirm cancellation'));
    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalledTimes(6));

    const urls = vi.mocked(fetch).mock.calls.map(call => call[0]);
    expect(urls.slice(1)).toEqual([
      '/api/v1/work-orders?dealerExternalId=WF-D-LAKES-WATER',
      '/api/v1/work-orders?dealerExternalId=WF-D-LAKES-WATER',
      '/api/v1/work-orders?dealerExternalId=WF-D-LAKES-WATER',
      `/api/v1/work-orders/${order.id}/cancel?dealerExternalId=WF-D-LAKES-WATER`,
      '/api/v1/work-orders?dealerExternalId=WF-D-LAKES-WATER',
    ]);
  });

  it('clears dealer-specific state and refreshes when the preview dealer changes', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(json([
        { externalId: 'WF-D-NORTH-STAR', displayName: 'North Star Water Systems' },
        { externalId: 'WF-D-LAKES-WATER', displayName: 'Lakes Water Conditioning' },
      ]))
      .mockResolvedValueOnce(json([order]))
      .mockResolvedValueOnce(json([]));
    render(<WorkOrdersPage administratorPreview />);
    await screen.findByRole('option', { name: 'North Star Water Systems' });
    fireEvent.change(screen.getByLabelText('Active dealer'), { target: { value: 'WF-D-NORTH-STAR' } });
    await screen.findByText('WO-000123');
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    fireEvent.change(screen.getByLabelText('Cancellation reason'), { target: { value: 'Not for this dealer' } });

    fireEvent.change(screen.getByLabelText('Active dealer'), { target: { value: 'WF-D-LAKES-WATER' } });

    expect(screen.queryByLabelText('Cancellation reason')).not.toBeInTheDocument();
    expect(screen.queryByText('WO-000123')).not.toBeInTheDocument();
    expect(await screen.findByText('No work orders yet.')).toBeInTheDocument();
    expect(vi.mocked(fetch).mock.calls[2][0]).toBe('/api/v1/work-orders?dealerExternalId=WF-D-LAKES-WATER');
  });

  it('keeps the actual dealer-administrator experience unscoped', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(json([order]));
    render(<WorkOrdersPage />);
    await screen.findByText('WO-000123');
    expect(screen.queryByLabelText('Active dealer')).not.toBeInTheDocument();
    expect(vi.mocked(fetch).mock.calls[0][0]).toBe('/api/v1/work-orders');
  });
});

function json(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
}
