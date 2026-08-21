import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DevelopmentIdentityProvider, useDevelopmentIdentity } from './DevelopmentIdentity';

const administrator = {
  userId: 'staff-1',
  displayName: 'WaterFlex Administrator',
  role: 'waterFlexAdministrator' as const,
  dealerExternalId: null,
  dealerName: null,
};

function CurrentUser() {
  const { currentUser } = useDevelopmentIdentity();
  return <div>{currentUser?.displayName}</div>;
}

describe('production staff identity', () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
    window.localStorage.clear();
  });

  it('renders an active staff session', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status: 'active', user: administrator,
    }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    render(<DevelopmentIdentityProvider mode="production"><CurrentUser /></DevelopmentIdentityProvider>);

    expect(await screen.findByText('WaterFlex Administrator')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/staff/session', expect.objectContaining({
      headers: { 'X-Requested-With': 'XMLHttpRequest' },
    }));
  });

  it('automatically activates an invited staff session without sending an invitation id', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ status: 'activationRequired', user: null }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(administrator), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    render(<DevelopmentIdentityProvider mode="production"><CurrentUser /></DevelopmentIdentityProvider>);

    expect(await screen.findByText('WaterFlex Administrator')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/v1/staff/activate', expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({ 'X-WaterFlex-Request': 'console' }),
    }));
    expect(fetchMock.mock.calls[1][1]).not.toHaveProperty('body');
  });

  it('shows an actionable message instead of a blank shell when access is unauthorized', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 401 })));

    render(<DevelopmentIdentityProvider mode="production"><CurrentUser /></DevelopmentIdentityProvider>);

    expect(await screen.findByText('WaterFlex access has not been provisioned')).toBeInTheDocument();
  });

  it('lets the user retry a temporary identity-service failure', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 500 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ status: 'active', user: administrator }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    render(<DevelopmentIdentityProvider mode="production"><CurrentUser /></DevelopmentIdentityProvider>);

    fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));

    await waitFor(() => expect(screen.getByText('WaterFlex Administrator')).toBeInTheDocument());
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
