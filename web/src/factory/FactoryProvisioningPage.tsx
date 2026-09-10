import { AlertTriangle, Check, CircuitBoard, Cpu, KeyRound, LoaderCircle, PlugZap, Printer, RotateCcw, Server, Settings, ShieldCheck, Tag, Usb, Wifi } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import {
  createFactorySecrets,
  abandonFactoryDevice,
  findActiveFactoryDevice,
  findFactoryDevice,
  getFactoryConfiguration,
  listFactoryStations,
  getFactoryStation,
  createFactoryStationGrant,
  registerFactoryDevice,
  retryFactoryDevice,
  type FactoryConfiguration,
  type FactoryRegistration,
  type FactoryVerification,
  type FactoryStationSummary,
} from './api';
import {
  checkHelper,
  clearHelperJob,
  getHelperLabel,
  getHelperDevices,
  getHelperStation,
  enrollHelperStation,
  getHelperJob,
  isHelperUnavailableError,
  prepareHelperJob,
  startHelperJob,
  type HelperJob,
  type HelperDevices,
  type HelperLabel,
  type HelperStation,
} from './helper';
import { deriveFactoryStep } from './workflow';

const ACTIVE_JOB_KEY = 'waterflex-factory-active-job';

const FACTORY_STEPS = [
  { label: 'Connect sensor', icon: Usb },
  { label: 'Flash firmware', icon: Cpu },
  { label: 'Configure sensor', icon: Settings },
  { label: 'Verify unit', icon: ShieldCheck },
  { label: 'Label sensor', icon: Tag },
] as const;

export default function FactoryProvisioningPage() {
  const [configuration, setConfiguration] = useState<FactoryConfiguration | null>(null);
  const [configurationError, setConfigurationError] = useState('');
  const [helperReady, setHelperReady] = useState(false);
  const [helperDevices, setHelperDevices] = useState<HelperDevices | null>(null);
  const [helperStatusError, setHelperStatusError] = useState('');
  const [helperStation, setHelperStation] = useState<HelperStation | null>(null);
  const [backendStation, setBackendStation] = useState<FactoryStationSummary | null>(null);
  const [registration, setRegistration] = useState<FactoryRegistration | null>(null);
  const [helperJob, setHelperJob] = useState<HelperJob | null>(null);
  const [verification, setVerification] = useState<FactoryVerification | null>(null);
  const [label, setLabel] = useState<HelperLabel | null>(null);
  const [activeKey, setActiveKey] = useState(() => window.localStorage.getItem(ACTIVE_JOB_KEY));
  const [activeChecked, setActiveChecked] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const refreshHelperDevices = useCallback(async (config: FactoryConfiguration, signal?: AbortSignal) => {
    const devices = await getHelperDevices(config.helperBaseUrl, signal);
    setHelperDevices(devices);
    setHelperReady(true);
    setHelperStatusError('');
  }, []);

  const markHelperDisconnected = useCallback((reason: unknown) => {
    setHelperReady(false);
    setHelperDevices(null);
    setHelperStation(null);
    setBackendStation(null);
    setHelperStatusError(reason instanceof Error ? reason.message : 'Factory helper detection is unavailable.');
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    getFactoryConfiguration(controller.signal)
      .then(async (config) => {
        setConfiguration(config);
      })
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) setConfigurationError(reason instanceof Error ? reason.message : 'Factory configuration is unavailable.');
      });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (!configuration?.enabled) return;
    const controller = new AbortController();
    let timer: number | undefined;
    let connected = false;

    const pollHelper = async () => {
      try {
        if (!connected) {
          const health = await checkHelper(configuration.helperBaseUrl, controller.signal);
          if (health.protocolVersion !== configuration.helperProtocolVersion) {
            throw new Error(`Update the factory helper. Protocol ${health.protocolVersion} is installed; protocol ${configuration.helperProtocolVersion} is required.`);
          }
          const localStation = await getHelperStation(configuration.helperBaseUrl, controller.signal);
          setHelperStation(localStation);
          if (localStation.stationId) {
            try {
              setBackendStation(await getFactoryStation(localStation.stationId, controller.signal));
            } catch (reason) {
              if (!controller.signal.aborted) {
                setBackendStation(null);
                setError(reason instanceof Error ? reason.message : 'The workstation identity could not be loaded from WaterFlex.');
              }
            }
          } else {
            setBackendStation(null);
          }
        }
        await refreshHelperDevices(configuration, controller.signal);
        connected = true;
      } catch (reason) {
        if (controller.signal.aborted) return;
        connected = false;
        markHelperDisconnected(reason);
      } finally {
        if (!controller.signal.aborted) timer = window.setTimeout(pollHelper, 1000);
      }
    };

    void pollHelper();
    return () => {
      if (timer !== undefined) window.clearTimeout(timer);
      controller.abort();
    };
  }, [configuration, markHelperDisconnected, refreshHelperDevices]);

  useEffect(() => {
    if (!configuration || !configuration.enabled) return;
    const controller = new AbortController();
    findActiveFactoryDevice(controller.signal)
      .then((active) => {
        if (controller.signal.aborted) return;
        window.localStorage.setItem(ACTIVE_JOB_KEY, active.idempotencyKey);
        setActiveKey(active.idempotencyKey);
      })
      .catch(() => {
        // No non-terminal job for this operator on the backend; fall back to whatever localStorage has.
      })
      .finally(() => {
        if (!controller.signal.aborted) setActiveChecked(true);
      });
    return () => controller.abort();
  }, [configuration]);

  useEffect(() => {
    if (!configuration || !activeKey || !helperReady) return;
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
        if (!registered.flashAuthorizationToken) throw new Error('WaterFlex did not issue a flash authorization for this job.');
        localJob = await startHelperJob(configuration.helperBaseUrl, activeKey, {
          deviceId: registered.deviceId,
          serialNumber: registered.serialNumber,
          model: registered.model,
          firmwareVersion: configuration.approvedFirmwareVersion,
          configurationVersion: configuration.configurationVersion,
          flashAuthorizationToken: registered.flashAuthorizationToken,
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
      if (controller.signal.aborted) return;
      if (isHelperUnavailableError(reason)) markHelperDisconnected(reason);
      else setError(reason instanceof Error ? reason.message : 'The active factory job could not be resumed.');
    });
    return () => controller.abort();
  }, [activeKey, configuration, helperReady, markHelperDisconnected]);

  useEffect(() => {
    if (!configuration || !activeKey || !helperReady || !helperJob || helperJob.status === 'completed' || helperJob.status === 'failed') return;
    const timer = window.setInterval(() => {
      getHelperJob(configuration.helperBaseUrl, activeKey)
        .then(setHelperJob)
        .catch((reason: unknown) => {
          if (isHelperUnavailableError(reason)) markHelperDisconnected(reason);
          else setError(reason instanceof Error ? reason.message : 'Factory helper stopped responding.');
        });
    }, 1000);
    return () => window.clearInterval(timer);
  }, [activeKey, configuration, helperJob, helperReady, markHelperDisconnected]);

  useEffect(() => {
    if (!activeKey || !helperJob || !['completed', 'failed'].includes(helperJob.status)) return;
    const controller = new AbortController();
    findFactoryDevice(activeKey, controller.signal)
      .then((current) => {
        if (controller.signal.aborted) return;
        setRegistration(current);
        if (current.status === 'provisioned' || current.status === 'quarantined') {
          setVerification({ deviceId: current.deviceId, serialNumber: current.serialNumber, status: current.status, verifiedAtUtc: current.verifiedAtUtc ?? current.registeredAtUtc, failureCode: current.failureCode });
        }
      })
      .catch((reason: unknown) => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Factory acceptance has not been recorded by the helper.'); });
    return () => controller.abort();
  }, [activeKey, helperJob]);

  useEffect(() => {
    if (!configuration || !activeKey || !helperReady || verification?.status !== 'provisioned') return;
    getHelperLabel(configuration.helperBaseUrl, activeKey)
      .then(setLabel)
      .catch((reason: unknown) => {
        if (isHelperUnavailableError(reason)) markHelperDisconnected(reason);
        else setError(reason instanceof Error ? reason.message : 'The completed label could not be retrieved from the helper.');
      });
  }, [activeKey, configuration, helperReady, markHelperDisconnected, verification]);

  async function startProvisioning() {
    if (!configuration || !helperReady || helperStation?.enrollmentStatus !== 'enrolled' || backendStation?.revokedAtUtc || helperDevices?.status !== 'detected' || helperDevices.devices.length !== 1 || !configuration.enabled) return;
    setBusy(true);
    setError('');
    setVerification(null);
    setLabel(null);
    setHelperJob(null);
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
      if (!registered.flashAuthorizationToken) throw new Error('WaterFlex did not issue a flash authorization for this job.');
      setActiveKey(secrets.idempotencyKey);
      setRegistration(registered);
      const job = await startHelperJob(configuration.helperBaseUrl, secrets.idempotencyKey, {
        deviceId: registered.deviceId,
        serialNumber: registered.serialNumber,
        model: registered.model,
        firmwareVersion: configuration.approvedFirmwareVersion,
        configurationVersion: configuration.configurationVersion,
        flashAuthorizationToken: registered.flashAuthorizationToken,
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
    if (!configuration || !registration || !activeKey || !helperReady || helperDevices?.status !== 'detected' || helperDevices.devices.length !== 1) return;
    setBusy(true);
    setError('');
    setVerification(null);
    setLabel(null);
    setHelperJob(null);
    try {
      const retried = await retryFactoryDevice(registration.deviceId);
      if (!retried.flashAuthorizationToken) throw new Error('WaterFlex did not issue a flash authorization for this job.');
      setRegistration(retried);
      const job = await startHelperJob(configuration.helperBaseUrl, activeKey, {
        deviceId: retried.deviceId,
        serialNumber: retried.serialNumber,
        model: retried.model,
        firmwareVersion: configuration.approvedFirmwareVersion,
        configurationVersion: configuration.configurationVersion,
        flashAuthorizationToken: retried.flashAuthorizationToken,
      });
      setHelperJob(job);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Factory provisioning could not be retried.');
    } finally {
      setBusy(false);
    }
  }

  async function finishJob() {
    if (!configuration || !activeKey || !label) return;
    setBusy(true);
    try {
      await clearHelperJob(configuration.helperBaseUrl, activeKey);
      window.localStorage.removeItem(ACTIVE_JOB_KEY);
      setActiveKey(null);
      setRegistration(null);
      setHelperJob(null);
      setVerification(null);
      setLabel(null);
      setError('');
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Protected local job data could not be cleared.');
    } finally {
      setBusy(false);
    }
  }

  async function enrollStation() {
    if (!configuration || !helperStation) return;
    setBusy(true); setError('');
    try {
      const displayName = helperStation.proposedWorkstationName;
      const grant = await createFactoryStationGrant({ displayName, publicKey: helperStation.publicKey, thumbprint: helperStation.publicKeyThumbprint });
      const enrolled = await enrollHelperStation(configuration.helperBaseUrl, { grantToken: grant.grantToken, displayName });
      setHelperStation(enrolled);
      const stations = await listFactoryStations();
      setBackendStation(stations.find((station) => station.stationId === enrolled.stationId) ?? null);
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'This workstation could not be enrolled.'); }
    finally { setBusy(false); }
  }

  async function abandonJob() {
    if (!registration) return;
    setBusy(true);
    try {
      await abandonFactoryDevice(registration.deviceId, 'hardware_failure');
      if (configuration && activeKey) await clearHelperJob(configuration.helperBaseUrl, activeKey);
      window.localStorage.removeItem(ACTIVE_JOB_KEY);
      setActiveKey(null);
      setRegistration(null);
      setHelperJob(null);
      setVerification(null);
      setLabel(null);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Factory job could not be abandoned.');
    } finally {
      setBusy(false);
    }
  }

  const complete = verification?.status === 'provisioned';
  const quarantined = verification?.status === 'quarantined';
  const working = helperJob && !['completed', 'failed'].includes(helperJob.status);
  const exactlyOneDevice = helperReady && helperDevices?.status === 'detected' && helperDevices.devices.length === 1;
  const stationActive = helperStation?.enrollmentStatus === 'enrolled' && backendStation && !backendStation.revokedAtUtc;
  const detectedDevice = exactlyOneDevice ? helperDevices.devices[0] : null;
  const deviceHeading = !helperReady
    ? helperStatusError ? 'Detection unavailable' : 'Checking for sensor'
    : helperDevices?.status === 'none'
      ? 'No Nano detected'
      : helperDevices?.status === 'multiple'
        ? 'Multiple Nanos detected. Disconnect all but one'
        : detectedDevice ? 'Nano detected' : 'Checking for sensor';
  const deviceMessage = detectedDevice
    ? `${detectedDevice.port} · ${detectedDevice.description}. USB presence only; this does not show whether the unit was previously provisioned.`
    : helperDevices?.status === 'multiple'
      ? `${helperDevices.devices.length} matching USB serial devices are connected. USB detection cannot identify prior provisioning.`
      : helperDevices?.status === 'none'
        ? 'Plug one Nano ESP32 into this workstation. USB detection cannot identify prior provisioning.'
        : helperStatusError || 'Checking the workstation for a matching USB serial device.';
  const activeStep = deriveFactoryStep(helperJob?.status ?? null, registration?.status ?? null, Boolean(label));
  const correctiveAction = !configuration
    ? configurationError || 'Wait while this page connects to the WaterFlex API.'
    : !configuration.enabled
      ? 'Factory provisioning is disabled in this environment. Contact a WaterFlex administrator.'
      : !helperReady
        ? helperStatusError || 'Open WaterFlex Factory Helper from the Windows Start menu. This page will connect automatically.'
        : helperStation?.enrollmentStatus === 'unenrolled'
          ? 'Enroll this workstation before connecting a production sensor.'
          : backendStation?.revokedAtUtc
            ? 'This workstation has been revoked. Contact a WaterFlex administrator before continuing.'
            : helperDevices?.status === 'none'
              ? 'Connect exactly one Arduino Nano ESP32 to this workstation with a USB data cable.'
              : helperDevices?.status === 'multiple'
                ? 'Disconnect extra sensors so exactly one Arduino Nano ESP32 remains connected.'
                : null;
  const blockingCorrection = activeStep === 0 ? correctiveAction : null;

  const evidence = helperJob?.evidence;
  const acceptanceChecks = evidence ? [
    { label: 'Firmware', passed: evidence.firmware },
    { label: 'Device identity', passed: evidence.identity },
    { label: 'Setup portal startup', passed: evidence.portalStartup ?? evidence.portal },
    { label: 'Setup portal', passed: evidence.portal },
    { label: 'Sensor response', passed: evidence.sensor },
  ] : [];

  return (
    <section className="factory-page" aria-labelledby="factory-title">
      <div className="factory-workflow-layout">
        <nav className="factory-step-rail" aria-label="Factory provisioning steps">
          <div className="rail-title">Provisioning</div>
          <ol>
            {FACTORY_STEPS.map((step, index) => {
              const Icon = step.icon;
              const isComplete = index < activeStep;
              const isActive = index === activeStep;
              return (
                <li key={step.label} className={index < FACTORY_STEPS.length - 1 ? 'has-line' : ''}>
                  <div className={isActive ? 'active' : isComplete ? 'complete' : 'future'} aria-current={isActive ? 'step' : undefined}>
                    <span className="step-icon">{isComplete ? <Check size={16} /> : <Icon size={17} />}</span>
                    <span><small>Step {index + 1}</small><strong>{step.label}</strong></span>
                  </div>
                </li>
              );
            })}
          </ol>
        </nav>

        <main className="factory-workflow-main">
          <header className="workflow-heading factory-heading">
            <div><span className="eyebrow">Manufacturing</span><h1 id="factory-title">{FACTORY_STEPS[activeStep].label}</h1></div>
            <span className={`status-pill ${complete ? 'success' : working || registration ? 'pending' : 'draft'}`}>
              <span />{complete ? 'Provisioned' : quarantined ? 'Quarantined' : working || registration ? 'In progress' : 'Ready'}
            </span>
          </header>

          <div className="factory-mobile-progress" aria-label={`Step ${activeStep + 1} of ${FACTORY_STEPS.length}: ${FACTORY_STEPS[activeStep].label}`}>
            <span>Step {activeStep + 1} of {FACTORY_STEPS.length}</span><strong>{FACTORY_STEPS[activeStep].label}</strong>
            <div><span style={{ width: `${((activeStep + 1) / FACTORY_STEPS.length) * 100}%` }} /></div>
          </div>

          <div className="factory-connectivity-compact" aria-label="Connectivity summary">
            <span className={configuration?.enabled ? 'ready' : ''}><Server size={14} />API</span>
            <span className={helperReady ? 'ready' : ''}><PlugZap size={14} />Helper</span>
            <span className={stationActive ? 'ready' : ''}><KeyRound size={14} />Station</span>
            <span className={exactlyOneDevice ? 'ready' : ''}><Usb size={14} />USB</span>
          </div>

          <div className="factory-task">
            {error && <div className="inline-alert error" role="alert"><AlertTriangle size={17} /><span>{error}</span></div>}

            {blockingCorrection ? (
              <div className="factory-task-section">
                <div className="factory-task-icon"><PlugZap size={25} /></div>
                <span className="eyebrow">Action required</span>
                <h2>{deviceHeading}</h2>
                <div className="inline-alert warning" role="alert"><AlertTriangle size={17} /><span>{blockingCorrection}</span></div>
                {helperStation?.enrollmentStatus === 'unenrolled' && (
                  <button className="button button-primary" type="button" disabled={busy} onClick={enrollStation}>
                    {busy ? <LoaderCircle className="spin" size={17} /> : <ShieldCheck size={17} />}Enroll this workstation
                  </button>
                )}
              </div>
            ) : activeStep === 0 ? (
              <div className="factory-task-section">
                <div className="factory-task-icon ready"><CircuitBoard size={25} /></div>
                <span className="eyebrow">Sensor ready</span>
                <h2>Start automated provisioning</h2>
                <p>Keep this sensor connected until WaterFlex finishes every stage and displays its printable label.</p>
                <ol className="factory-guidance">
                  <li><span>1</span>Confirm only one Nano is connected on <strong>{detectedDevice?.port}</strong>.</li>
                  <li><span>2</span>Leave the USB cable connected during flashing and verification.</li>
                  <li><span>3</span>Click Provision sensor. The workflow advances automatically.</li>
                </ol>
              </div>
            ) : activeStep < 4 ? (
              <div className="factory-task-section">
                <div className={`factory-task-icon ${quarantined ? 'failed' : 'working'}`}>{quarantined ? <AlertTriangle size={25} /> : <LoaderCircle className="spin" size={25} />}</div>
                <span className="eyebrow">{registration?.serialNumber ?? 'Assigning serial'}</span>
                <h2>{quarantined ? 'Unit requires review' : helperJob?.message ?? 'Synchronizing with WaterFlex'}</h2>
                <p>{quarantined ? `The unit is quarantined${verification?.failureCode ? `: ${verification.failureCode}` : '.'}` : 'Keep the sensor connected. This page will continue automatically.'}</p>
                {correctiveAction && <div className="inline-alert warning" role="alert"><AlertTriangle size={17} /><span>{correctiveAction}</span></div>}
                {acceptanceChecks.length > 0 && (
                  <div className="factory-acceptance" aria-label="Factory acceptance checks">
                    {acceptanceChecks.map((check) => (
                      <div className={check.passed ? 'passed' : 'failed'} key={check.label}>
                        {check.passed ? <Check size={17} /> : <AlertTriangle size={17} />}
                        <span><strong>{check.label}</strong><small>{check.passed ? 'Passed' : 'Failed'}</small></span>
                      </div>
                    ))}
                    {evidence && <p>{evidence.sensorSampleCount} sensor samples · range {evidence.sensorMinimumMm ?? 'n/a'} to {evidence.sensorMaximumMm ?? 'n/a'} mm</p>}
                  </div>
                )}
              </div>
            ) : (
              <div className="factory-task-section factory-label-task">
                <div className="factory-task-icon ready"><Check size={25} /></div>
                <span className="eyebrow">Acceptance complete</span>
                <h2>Print and attach the sensor label</h2>
                <p>Confirm the label is readable and firmly attached before clearing the protected job data.</p>
                {correctiveAction && <div className="inline-alert warning" role="alert"><AlertTriangle size={17} /><span>{correctiveAction}</span></div>}
                {registration && (
                  <section className="factory-label" aria-label="Sensor label">
                    <strong>WaterFlex</strong><span>{label?.serialNumber ?? registration.serialNumber}</span><small>{label?.firmwareVersion ?? configuration?.approvedFirmwareVersion}</small>
                    {label && <><small>{label.setupNetwork}</small><small>{label.setupPassphrase}</small></>}
                  </section>
                )}
              </div>
            )}
          </div>

          <footer className="factory-actions">
            {!registration && <span />}
            {!registration && (
              <button className="button button-primary" type="button" disabled={busy || !stationActive || !exactlyOneDevice || !activeChecked || !configuration?.enabled} onClick={startProvisioning}>
                {busy ? <LoaderCircle className="spin" size={17} /> : <CircuitBoard size={17} />}Provision sensor
              </button>
            )}
            {quarantined && <button className="button button-secondary" type="button" disabled={busy} onClick={abandonJob}>Scrap this sensor</button>}
            {quarantined && <button className="button button-primary" type="button" disabled={busy || !exactlyOneDevice} onClick={retryProvisioning}><RotateCcw size={17} />Retry this sensor</button>}
            {complete && <button className="button button-secondary" type="button" disabled={!label} onClick={() => window.print()}><Printer size={17} />Print label</button>}
            {complete && <button className="button button-primary" type="button" disabled={busy || !label} onClick={finishJob}>Label attached, clear and start next</button>}
          </footer>
        </main>

        <aside className="factory-connectivity" aria-label="Connectivity">
          <div className="context-heading"><Wifi size={18} /><span>Connectivity</span></div>
          <ConnectivityItem icon={Server} label="WaterFlex API" value={!configuration ? configurationError ? 'Unavailable' : 'Checking' : configuration.enabled ? 'Connected' : 'Disabled'} ready={Boolean(configuration?.enabled)} detail={configurationError || undefined} />
          <ConnectivityItem icon={PlugZap} label="Local Factory Helper" value={helperReady ? `Connected · v${helperStation?.helperVersion ?? 'checking'}` : 'Not connected'} ready={helperReady} detail={helperStatusError || undefined} />
          <ConnectivityItem icon={KeyRound} label="Workstation" value={backendStation?.displayName ?? (helperStation?.enrollmentStatus === 'unenrolled' ? 'Enrollment required' : 'Checking')} ready={Boolean(stationActive)} detail={backendStation?.revokedAtUtc ? 'Revoked' : helperStation ? `${helperStation.displayName ?? helperStation.proposedWorkstationName} · ${helperStation.keyProviderType === 'tpm' ? 'TPM-backed key' : 'Software key fallback'} · ${helperStation.publicKeyThumbprint.slice(0, 12)}…` : undefined} />
          <ConnectivityItem icon={Usb} label="USB sensor" value={registration?.serialNumber ?? deviceHeading} ready={exactlyOneDevice} detail={helperJob?.message ?? deviceMessage} />
        </aside>
      </div>
    </section>
  );
}

function ConnectivityItem({ icon: Icon, label, value, ready, detail }: { icon: typeof Server; label: string; value: string; ready: boolean; detail?: string }) {
  return (
    <div className="factory-connectivity-item">
      <Icon size={17} />
      <span><small>{label}</small><strong>{value}</strong>{detail && <em>{detail}</em>}</span>
      <i className={ready ? 'ready' : ''} aria-label={ready ? 'Ready' : 'Attention required'} />
    </div>
  );
}
