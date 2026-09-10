import type { FactoryRegistration } from './api';
import type { HelperJob } from './helper';

export function deriveFactoryStep(
  helperStatus: HelperJob['status'] | null,
  registrationStatus: FactoryRegistration['status'] | null,
  hasLabel: boolean,
): number {
  if (registrationStatus === 'provisioned' || hasLabel) return 4;
  if (registrationStatus === 'quarantined' || registrationStatus === 'failed' || helperStatus === 'verifying' || helperStatus === 'completed' || helperStatus === 'failed') return 3;
  if (helperStatus === 'provisioning') return 2;
  if (registrationStatus === 'registered' || helperStatus === 'prepared' || helperStatus === 'queued' || helperStatus === 'flashing') return 1;
  return 0;
}
