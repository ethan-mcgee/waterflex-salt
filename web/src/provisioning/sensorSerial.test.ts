import { afterEach, describe, expect, it } from 'vitest';
import {
  parseSensorDistanceLine,
  readSensorDistance,
  SensorSerialError,
} from './sensorSerial';

function setSerial(value: unknown) {
  Object.defineProperty(navigator, 'serial', { value, configurable: true });
}

describe('sensorSerial', () => {
  afterEach(() => setSerial(undefined));

  it('accepts only explicit in-range distance lines', () => {
    expect(parseSensorDistanceLine('distance=875 mm')).toBe(875);
    expect(parseSensorDistanceLine('sensor read error fault=stuckLow')).toBeNull();
    expect(parseSensorDistanceLine('distance=29 mm')).toBeNull();
    expect(parseSensorDistanceLine('distance=4501 mm')).toBeNull();
  });

  it('reports unsupported browsers without inventing a bench reading', async () => {
    setSerial(undefined);
    await expect(readSensorDistance()).rejects.toMatchObject<Partial<SensorSerialError>>({ code: 'unsupported' });
  });

  it('distinguishes a cancelled device picker from a sensor reading', async () => {
    setSerial({ requestPort: () => Promise.reject(new DOMException('cancelled', 'NotFoundError')) });
    await expect(readSensorDistance()).rejects.toMatchObject<Partial<SensorSerialError>>({ code: 'cancelled' });
  });
});
