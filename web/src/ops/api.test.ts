import { afterEach, describe, expect, it, vi } from 'vitest';
import { getFleetReadings, OpsApiError } from './api';

describe('getFleetReadings', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('requests the selected range with the 50-reading limit', async () => {
    const fetchMock = vi.fn().mockResolvedValue(okResponse([]));
    vi.stubGlobal('fetch', fetchMock);

    await getFleetReadings('device/id', '24h');

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/ops/devices/device%2Fid/readings?range=24h&limit=50',
      expect.objectContaining({ headers: expect.any(Object) }),
    );
  });

  it('retries a network failure once after 750 milliseconds', async () => {
    vi.useFakeTimers();
    const fetchMock = vi.fn()
      .mockRejectedValueOnce(new TypeError('Load failed'))
      .mockResolvedValueOnce(okResponse([]));
    vi.stubGlobal('fetch', fetchMock);

    const resultPromise = getFleetReadings('device-id', '7d');
    await vi.advanceTimersByTimeAsync(750);

    await expect(resultPromise).resolves.toEqual([]);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('retries a server error once', async () => {
    vi.useFakeTimers();
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(errorResponse(503, 'Unavailable'))
      .mockResolvedValueOnce(okResponse([]));
    vi.stubGlobal('fetch', fetchMock);

    const resultPromise = getFleetReadings('device-id', '24h');
    await vi.advanceTimersByTimeAsync(750);

    await expect(resultPromise).resolves.toEqual([]);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('does not retry a client error', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 403,
      json: vi.fn().mockResolvedValue({ title: 'Forbidden' }),
    } as unknown as Response);
    vi.stubGlobal('fetch', fetchMock);

    await expect(getFleetReadings('device-id', '30d')).rejects.toEqual(
      expect.objectContaining<Partial<OpsApiError>>({ status: 403 }),
    );
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});

function okResponse(body: unknown): Response {
  return {
    ok: true,
    status: 200,
    json: vi.fn().mockResolvedValue(body),
  } as unknown as Response;
}

function errorResponse(status: number, title: string): Response {
  return {
    ok: false,
    status,
    json: vi.fn().mockResolvedValue({ title }),
  } as unknown as Response;
}
