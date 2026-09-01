import { AlertTriangle, Check, CircuitBoard, LoaderCircle, PlugZap, Printer, RotateCcw, ShieldCheck } from 'lucide-react';
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  createFactorySecrets,
  findFactoryDevice,
  getFactoryConfiguration,
  recordFactoryVerification,
  registerFactoryDevice,
  retryFactoryDevice,
  type FactoryConfiguration,
  type FactoryRegistration,
  type FactoryVerification,
} from './api';
import {
  checkHelper,
  clearHelperJob,
  getHelperJob,
  prepareHelperJob,
  startHelperJob,
  type HelperJob,
} from './helper';

const ACTIVE_JOB_KEY = 'waterflex-factory-active-job';

export default function FactoryProvisioningPage() {
  const [configuration, setConfiguration] = useState<FactoryConfiguration | null>(null);
  const [helperReady, setHelperReady] = useState(false);
  const [registration, setRegistration] = useState<FactoryRegistration | null>(null);
  const [helperJob, setHelperJob] = useState<HelperJob | null>(null);
  const [verification, setVerification] = useState<FactoryVerification | null>(null);
  const [activeKey, setActiveKey] = useState(() => window.localStorage.getItem(ACTIVE_JOB_KEY));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const finalizing = useRef(false);

  const refreshHelper = useCallback(async (config: FactoryConfiguration, signal?: AbortSignal) => {
    const health = await checkHelper(config.helperBaseUrl, signal);
    if (health.protocolVersion !== config.helperProtocolVersion) {
      throw new Error(`Factory helper protocol ${health.protocolVersion} is not approved. Expected ${config.helperProtocolVersion}.`);
    }
    setHelperReady(true);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    getFactoryConfiguration(controller.signal)
      .then(async (config) => {
        setConfiguration(config);
        if (!config.enabled) return;
        await refreshHelper(config, controller.signal);
      })
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Factory configuration is unavailable.');
      });
    return () => controller.abort();
  }, [refreshHelper]);

  useEffect(() => {
    if (!configuration || !activeKey) return;
    const controller = new AbortController();
    (async () => {
      let localJob = await getHelperJob(configuration.helperBaseUrl, activeKey, controller.signal);
      let registered: FactoryRegistration;
      try {
        registered = await findFactoryDevice(activeKey, controller.signal);
      } catch {
        if (localJob.status !== 'prepared' || !localJob.bootstrapCredentialId || !localJob.bootstrapSecretHash) throw new Error('The WaterFlex registration for this local job could not be resumed.');
        registered = await registerFactoryDevice({
          idempotencyKey: activeKey,
          model: configuration.model,
          bootstrapCredentialId: localJob.bootstrapCredentialId,
          bootstrapSecretHash: localJob.bootstrapSecretHash,
          firmwareVersion: configuration.approvedFirmwareVersion,
          configurationVersion: configuration.configurationVersion,
        });
      }
      if (registered.status === 'registered' && localJob.status === 'prepared') {
        localJob = await startHelperJob(configuration.helperBaseUrl, activeKey, {
          deviceId: registered.deviceId,
          serialNumber: registered.serialNumber,
          model: registered.model,
          firmwareVersion: configuration.approvedFirmwareVersion,
          configurationVersion: configuration.configurationVersion,
        });
      }
      setRegistration(registered);
      setHelperJob(localJob);
      if (registered.status === 'provisioned' || registered.status === 'quarantined') {
        setVerification({
          deviceId: registered.deviceId,
          serialNumber: registered.serialNumber,
          status: registered.status,
          verifiedAtUtc: registered.verifiedAtUtc ?? registered.registeredAtUtc,
          failureCode: registered.failureCode,
        });
      }
    })().catch((reason: unknown) => {
      if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'The active factory job could not be resumed.');
    });
    return () => controller.abort();
  }, [activeKey, configuration]);

  useEffect(() => {
    if (!configuration || !activeKey || !helperJob || helperJob.status === 'completed' || helperJob.status === 'failed') return;
    const timer = window.setInterval(() => {
      getHelperJob(configuration.helperBaseUrl, activeKey)
        .then(setHelperJob)
        .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : 'Factory helper stopped responding.'));
    }, 1000);
    return () => window.clearInterval(timer);
  }, [activeKey, configuration, helperJob]);

  useEffect(() => {
    if (!configuration || !registration || !helperJob || verification || finalizing.current) return;
    if (helperJob.status !== 'completed' && helperJob.status !== 'failed') return;
    finalizing.current = true;
    const evidence = helperJob.evidence ?? { firmware: false, identity: false, portal: false, sensor: false };
    recordFactoryVerification(registration.deviceId, {
      firmwareVerified: evidence.firmware,
      identityVerified: evidence.identity,
      portalVerified: evidence.portal,
      sensorVerified: evidence.sensor,
      firmwareVersion: configuration.approvedFirmwareVersion,
      failureCode: helperJob.status === 'failed' ? helperJob.failureCode ?? 'factory_helper_failed' : null,
    }).then(setVerification)
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : 'Verification could not be recorded.'))
      .finally(() => { finalizing.current = false; });
  }, [configuration, helperJob, registration, verification]);

  async function startProvisioning() {
    if (!configuration || !helperReady || !configuration.enabled) return;
    setBusy(true);
    setError('');
    setVerification(null);
    try {
      const secrets = createFactorySecrets();
      const prepared = await prepareHelperJob(configuration.helperBaseUrl, {
        idempotencyKey: secrets.idempotencyKey,
        bootstrapCredentialId: secrets.bootstrapCredentialId,
        bootstrapSecret: secrets.bootstrapSecret,
        setupPassphrase: secrets.setupPassphrase,
      });
      window.localStorage.setItem(ACTIVE_JOB_KEY, secrets.idempotencyKey);
      const registered = await registerFactoryDevice({
        idempotencyKey: secrets.idempotencyKey,
        model: configuration.model,
        bootstrapCredentialId: prepared.bootstrapCredentialId,
        bootstrapSecretHash: prepared.bootstrapSecretHash,
        firmwareVersion: configuration.approvedFirmwareVersion,
        configurationVersion: configuration.configurationVersion,
      });
      setActiveKey(secrets.idempotencyKey);
      setRegistration(registered);
      const job = await startHelperJob(configuration.helperBaseUrl, secrets.idempotencyKey, {
        deviceId: registered.deviceId,
        serialNumber: registered.serialNumber,
        model: registered.model,
        firmwareVersion: configuration.approvedFirmwareVersion,
        configurationVersion: configuration.configurationVersion,
      });
      setHelperJob(job);
    } catch (reason) {
      setActiveKey(window.localStorage.getItem(ACTIVE_JOB_KEY));
      setError(reason instanceof Error ? reason.message : 'Factory provisioning could not start.');
    } finally {
      setBusy(false);
    }
  }

  async function retryProvisioning() {
    if (!configuration || !registration || !activeKey) return;
    setBusy(true);
    setError('');
    setVerification(null);
    try {
      await retryFactoryDevice(registration.deviceId);
      const job = await startHelperJob(configuration.helperBaseUrl, activeKey, {
        deviceId: registration.deviceId,
        serialNumber: registration.serialNumber,
        model: registration.model,
        firmwareVersion: configuration.approvedFirmwareVersion,
        configurationVersion: configuration.configurationVersion,
      });
      setHelperJob(job);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Factory provisioning could not be retried.');
    } finally {
      setBusy(false);
    }
  }

  async function finishJob() {
    if (!configuration || !activeKey) return;
    setBusy(true);
    try {
      await clearHelperJob(configuration.helperBaseUrl, activeKey);
      window.localStorage.removeItem(ACTIVE_JOB_KEY);
      setActiveKey(null);
      setRegistration(null);
      setHelperJob(null);
      setVerification(null);
      setError('');
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Protected local job data could not be cleared.');
    } finally {
      setBusy(false);
    }
  }

  const complete = verification?.status === 'provisioned';
  const quarantined = verification?.status === 'quarantined';
  const working = helperJob && !['completed', 'failed'].includes(helperJob.status);

  return (
    <section className="factory-page" aria-labelledby="factory-title">
      <header className="fleet-heading factory-heading">
        <div>
          <span className="eyebrow">Manufacturing</span>
          <h1 id="factory-title">Provision a new sensor</h1>
          <p>Connect one Arduino Nano ESP32. WaterFlex assigns the serial, flashes approved firmware, and verifies the finished unit.</p>
        </div>
        <span className={`status-pill ${complete ? 'success' : working ? 'pending' : 'draft'}`}>
          <span />{complete ? 'Provisioned' : quarantined ? 'Quarantined' : working ? 'In progress' : 'Ready'}
        </span>
      </header>

      {error && <div className="inline-alert error" role="alert"><AlertTriangle size={17} /><span>{error}</span></div>}
      {configuration && !configuration.enabled && (
        <div className="inline-alert warning"><AlertTriangle size={17} /><span>Factory provisioning is disabled in this environment.</span></div>
      )}

      <div className="factory-grid">
        <article className="factory-card">
          <div className={`factory-card-icon ${helperReady ? 'ready' : ''}`}><PlugZap size={22} /></div>
          <div><span className="factory-card-kicker">Local helper</span><h2>{helperReady ? 'Connected' : 'Not connected'}</h2>
            <p>{helperReady ? 'The workstation helper is ready to access USB hardware.' : 'Install and start the WaterFlex factory helper on this workstation.'}</p></div>
        </article>
        <article className="factory-card">
          <div className="factory-card-icon"><CircuitBoard size={22} /></div>
          <div><span className="factory-card-kicker">Connected unit</span><h2>{registration?.serialNumber ?? 'Waiting for sensor'}</h2>
            <p>{helperJob?.message ?? 'Plug one Nano ESP32 into this workstation before starting.'}</p></div>
        </article>
        <article className="factory-card">
          <div className={`factory-card-icon ${complete ? 'ready' : ''}`}><ShieldCheck size={22} /></div>
          <div><span className="factory-card-kicker">Acceptance</span><h2>{complete ? 'All checks passed' : quarantined ? 'Review required' : 'Not yet verified'}</h2>
            <p>{verification ? `Inventory status: ${verification.status}.` : 'Firmware, identity, portal, and sensor checks must all pass.'}</p></div>
        </article>
      </div>

      {helperJob?.evidence && (
        <div className="factory-checks" aria-label="Factory acceptance checks">
          {Object.entries(helperJob.evidence).map(([name, passed]) => (
            <span className={passed ? 'passed' : 'failed'} key={name}>{passed ? <Check size={15} /> : <AlertTriangle size={15} />}{name}</span>
          ))}
        </div>
      )}

      <footer className="factory-actions">
        {!registration && (
          <button className="button button-primary" type="button" disabled={busy || !helperReady || !configuration?.enabled} onClick={startProvisioning}>
            {busy ? <LoaderCircle className="spin" size={17} /> : <CircuitBoard size={17} />}Provision sensor
          </button>
        )}
        {quarantined && (
          <button className="button button-primary" type="button" disabled={busy} onClick={retryProvisioning}><RotateCcw size={17} />Retry this sensor</button>
        )}
        {complete && <button className="button button-secondary" type="button" onClick={() => window.print()}><Printer size={17} />Print label</button>}
        {complete && <button className="button button-primary" type="button" disabled={busy} onClick={finishJob}>Clear and start next</button>}
      </footer>

      {complete && registration && (
        <section className="factory-label" aria-label="Sensor label">
          <strong>WaterFlex</strong><span>{registration.serialNumber}</span><small>{configuration?.approvedFirmwareVersion}</small>
        </section>
      )}
    </section>
  );
}
