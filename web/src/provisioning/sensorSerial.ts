const SERIAL_BAUD_RATE = 115200;
const SAMPLE_TARGET = 5;
const READ_TIMEOUT_MS = 12000;
const MINIMUM_DISTANCE_MM = 30;
const MAXIMUM_DISTANCE_MM = 4500;
const MAXIMUM_SAMPLE_SPREAD_MM = 100;

interface SerialPortReader {
  read: () => Promise<ReadableStreamReadResult<Uint8Array>>;
  cancel: () => Promise<void>;
  releaseLock: () => void;
}

interface SerialPortLike {
  readable: { getReader: () => SerialPortReader } | null;
  open: (options: { baudRate: number }) => Promise<void>;
  close: () => Promise<void>;
}

interface SerialApiLike {
  requestPort: () => Promise<SerialPortLike>;
}

export interface SensorDistanceReading {
  distanceMm: number;
  sampleCount: number;
  spreadMm: number;
}

export interface SensorReadProgress {
  sampleCount: number;
  targetSampleCount: number;
  latestDistanceMm: number;
}

export class SensorSerialError extends Error {
  constructor(
    message: string,
    public readonly code:
      | 'unsupported'
      | 'cancelled'
      | 'unavailable'
      | 'timeout'
      | 'unstable',
  ) {
    super(message);
  }
}

export function supportsSensorSerial(): boolean {
  return Boolean(getSerialApi());
}

export async function readSensorDistance(
  onProgress?: (progress: SensorReadProgress) => void,
): Promise<SensorDistanceReading> {
  const serial = getSerialApi();
  if (!serial) {
    throw new SensorSerialError(
      'USB sensor reading requires a Chromium browser with Web Serial support.',
      'unsupported',
    );
  }

  let port: SerialPortLike;
  try {
    port = await serial.requestPort();
  } catch (error) {
    if (error instanceof DOMException && error.name === 'NotFoundError') {
      throw new SensorSerialError('Sensor selection was cancelled.', 'cancelled');
    }
    throw new SensorSerialError('The sensor USB connection could not be opened.', 'unavailable');
  }

  let reader: SerialPortReader | null = null;
  let timedOut = false;
  let timeoutId: number | undefined;

  try {
    await port.open({ baudRate: SERIAL_BAUD_RATE });
    if (!port.readable) {
      throw new SensorSerialError('The sensor did not expose a readable USB stream.', 'unavailable');
    }

    reader = port.readable.getReader();
    timeoutId = window.setTimeout(() => {
      timedOut = true;
      void reader?.cancel();
    }, READ_TIMEOUT_MS);

    const decoder = new TextDecoder();
    const samples: number[] = [];
    let pendingText = '';

    while (samples.length < SAMPLE_TARGET) {
      const { value, done } = await reader.read();
      if (done) break;

      pendingText += decoder.decode(value, { stream: true });
      const lines = pendingText.split(/\r?\n/);
      pendingText = lines.pop() ?? '';

      for (const line of lines) {
        const distanceMm = parseSensorDistanceLine(line);
        if (distanceMm === null) continue;

        samples.push(distanceMm);
        onProgress?.({
          sampleCount: samples.length,
          targetSampleCount: SAMPLE_TARGET,
          latestDistanceMm: distanceMm,
        });
        if (samples.length >= SAMPLE_TARGET) break;
      }
    }

    if (timedOut || samples.length < SAMPLE_TARGET) {
      throw new SensorSerialError(
        `The sensor did not provide ${SAMPLE_TARGET} valid distance samples in time.`,
        'timeout',
      );
    }

    const sortedSamples = [...samples].sort((left, right) => left - right);
    const spreadMm = sortedSamples.at(-1)! - sortedSamples[0];
    if (spreadMm > MAXIMUM_SAMPLE_SPREAD_MM) {
      throw new SensorSerialError(
        'The live sensor reading is unstable. Check its alignment and try again.',
        'unstable',
      );
    }

    return {
      distanceMm: sortedSamples[Math.floor(sortedSamples.length / 2)],
      sampleCount: samples.length,
      spreadMm,
    };
  } catch (error) {
    if (error instanceof SensorSerialError) throw error;
    throw new SensorSerialError('Unable to read the connected sensor.', 'unavailable');
  } finally {
    if (timeoutId !== undefined) window.clearTimeout(timeoutId);
    if (reader) {
      try {
        await reader.cancel();
      } catch {
        // The stream may already be closed after a disconnect or timeout.
      }
      reader.releaseLock();
    }
    try {
      await port.close();
    } catch {
      // Closing an already-disconnected USB device can reject in Chromium.
    }
  }
}

export function parseSensorDistanceLine(line: string): number | null {
  const match = /\bdistance=(\d+)\s*mm\b/i.exec(line);
  if (!match) return null;

  const distanceMm = Number(match[1]);
  return Number.isInteger(distanceMm)
    && distanceMm >= MINIMUM_DISTANCE_MM
    && distanceMm <= MAXIMUM_DISTANCE_MM
    ? distanceMm
    : null;
}

function getSerialApi(): SerialApiLike | undefined {
  return (navigator as Navigator & { serial?: SerialApiLike }).serial;
}