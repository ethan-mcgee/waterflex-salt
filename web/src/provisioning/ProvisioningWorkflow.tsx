import {
  ArrowLeft,
  ArrowRight,
  Building2,
  Check,
  CheckCircle2,
  ClipboardCheck,
  Copy,
  Cpu,
  Gauge,
  LoaderCircle,
  MapPin,
  RefreshCw,
  Search,
  ShieldCheck,
  Usb,
  Wifi,
  Wrench,
  XCircle,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import {
  useDeferredValue,
  useEffect,
  useState,
} from 'react';
import { useDevelopmentIdentity } from '../development/DevelopmentIdentity';
import ThemedSelect from '../components/ThemedSelect';
import { ApiError, commissionSensor, searchCustomers } from './api';
import {
  readSensorDistance,
  SensorSerialError,
  supportsSensorSerial,
} from './sensorSerial';
import type {
  SensorDistanceReading,
  SensorReadProgress,
} from './sensorSerial';
import type {
  CommissionSensorRequest,
  CommissionSensorResponse,
  WaterFlexCustomerOption,
  WaterFlexLocationOption,
  WaterFlexTankOption,
} from './types';

type StepId = 'account' | 'location' | 'sensor' | 'calibration' | 'review';

interface StepDefinition {
  id: StepId;
  label: string;
  shortLabel: string;
  icon: LucideIcon;
}

interface SensorForm {
  serialNumber: string;
  hardwareId: string;
  model: string;
  workOrderId: string;
}

const STEPS: StepDefinition[] = [
  { id: 'account', label: 'Customer account', shortLabel: 'Account', icon: Building2 },
  { id: 'location', label: 'Site and tank', shortLabel: 'Site', icon: MapPin },
  { id: 'sensor', label: 'Sensor identity', shortLabel: 'Sensor', icon: Cpu },
  { id: 'calibration', label: 'Tank calibration', shortLabel: 'Calibration', icon: Gauge },
  { id: 'review', label: 'Review and issue', shortLabel: 'Review', icon: ClipboardCheck },
];

const INITIAL_SENSOR: SensorForm = {
  serialNumber: '',
  hardwareId: '',
  model: 'Arduino Nano ESP32',
  workOrderId: '',
};

export default function ProvisioningWorkflow() {
  const { currentUser, selectedUserId } = useDevelopmentIdentity();
  const [step, setStep] = useState<StepId>('account');
  const [furthestStep, setFurthestStep] = useState(0);
  const [query, setQuery] = useState('');
  const deferredQuery = useDeferredValue(query);
  const [customers, setCustomers] = useState<WaterFlexCustomerOption[]>([]);
  const [directoryLoading, setDirectoryLoading] = useState(true);
  const [directoryError, setDirectoryError] = useState('');
  const [customerId, setCustomerId] = useState('');
  const [locationId, setLocationId] = useState('');
  const [tankId, setTankId] = useState('');
  const [sensor, setSensor] = useState<SensorForm>(INITIAL_SENSOR);
  const [tankDepth, setTankDepth] = useState('150');
  const [sensorReading, setSensorReading] = useState<SensorDistanceReading | null>(null);
  const [sensorReadProgress, setSensorReadProgress] = useState<SensorReadProgress | null>(null);
  const [sensorReadError, setSensorReadError] = useState('');
  const [readingSensor, setReadingSensor] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState('');
  const [result, setResult] = useState<CommissionSensorResponse | null>(null);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    if (currentUser?.role !== 'dealerTechnician') {
      setCustomers([]);
      setDirectoryLoading(false);
      setDirectoryError('Select a dealer technician identity in the header to provision a sensor.');
      return () => controller.abort();
    }

    setDirectoryLoading(true);
    setDirectoryError('');
    searchCustomers(deferredQuery, controller.signal)
      .then(setCustomers)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setDirectoryError(error instanceof Error ? error.message : 'Customer directory is unavailable.');
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setDirectoryLoading(false);
        }
      });

    return () => controller.abort();
  }, [currentUser?.role, deferredQuery, selectedUserId]);

  useEffect(() => {
    window.scrollTo({ top: 0 });
  }, [step, result]);

  const currentIndex = STEPS.findIndex((candidate) => candidate.id === step);
  const selectedCustomer = customers.find((customer) => customer.waterFlexCustomerId === customerId) ?? null;
  const selectedLocation = selectedCustomer?.locations.find(
    (location) => location.waterFlexLocationId === locationId,
  ) ?? null;
  const selectedTank = selectedLocation?.tanks.find((tank) => tank.waterFlexAssetId === tankId) ?? null;
  const tankDepthCm = Number(tankDepth);
  const currentDistanceCm = sensorReading ? sensorReading.distanceMm / 10 : Number.NaN;
  const calibrationValid = Number.isFinite(tankDepthCm)
    && tankDepthCm >= 10
    && tankDepthCm <= 450
    && sensorReading !== null
    && currentDistanceCm <= tankDepthCm;
  const sensorValid = sensor.serialNumber.trim().length >= 4
    && normalizeHardwareId(sensor.hardwareId).length === 12;

  const canContinue = step === 'account'
    ? Boolean(selectedCustomer)
    : step === 'location'
      ? Boolean(selectedLocation && selectedTank)
      : step === 'sensor'
        ? sensorValid
        : step === 'calibration'
          ? calibrationValid
          : true;

  function advance() {
    if (!canContinue || currentIndex >= STEPS.length - 1) {
      return;
    }
    const nextIndex = currentIndex + 1;
    setFurthestStep((current) => Math.max(current, nextIndex));
    setStep(STEPS[nextIndex].id);
  }

  function back() {
    if (currentIndex > 0) {
      setStep(STEPS[currentIndex - 1].id);
    }
  }

  function selectCustomer(customer: WaterFlexCustomerOption) {
    setCustomerId(customer.waterFlexCustomerId);
    setLocationId('');
    setTankId('');
    clearSensorReading();
  }

  function selectLocation(location: WaterFlexLocationOption) {
    setLocationId(location.waterFlexLocationId);
    setTankId('');
    clearSensorReading();
  }

  function selectTank(tank: WaterFlexTankOption) {
    setTankId(tank.waterFlexAssetId);
    clearSensorReading();
  }

  function updateSensor(nextSensor: SensorForm) {
    if (nextSensor.serialNumber !== sensor.serialNumber
      || nextSensor.hardwareId !== sensor.hardwareId
      || nextSensor.model !== sensor.model) {
      clearSensorReading();
    }
    setSensor(nextSensor);
  }

  function clearSensorReading() {
    setSensorReading(null);
    setSensorReadProgress(null);
    setSensorReadError('');
  }

  async function captureSensorReading() {
    setReadingSensor(true);
    setSensorReadError('');
    setSensorReading(null);
    setSensorReadProgress(null);

    try {
      setSensorReading(await readSensorDistance(setSensorReadProgress));
    } catch (error) {
      setSensorReadError(error instanceof SensorSerialError
        ? error.message
        : 'Unable to read the connected sensor.');
    } finally {
      setReadingSensor(false);
    }
  }

  function useBenchReading() {
    const depthMm = Number(tankDepth) * 10;
    if (!Number.isFinite(depthMm) || depthMm < 100 || depthMm > 4500) {
      setSensorReadError('Enter a valid tank depth before using a bench estimate.');
      return;
    }

    setSensorReadError('');
    setSensorReadProgress(null);
    setSensorReading({
      distanceMm: Math.round(depthMm / 2),
      sampleCount: 0,
      spreadMm: 0,
      source: 'bench',
    });
  }

  async function submitCommissioning() {
    if (!selectedCustomer
      || !selectedLocation
      || !selectedTank
      || !sensorValid
      || !sensorReading
      || !calibrationValid) {
      return;
    }

    const request: CommissionSensorRequest = {
      waterFlexCustomerId: selectedCustomer.waterFlexCustomerId,
      waterFlexLocationId: selectedLocation.waterFlexLocationId,
      waterFlexAssetId: selectedTank.waterFlexAssetId,
      serialNumber: sensor.serialNumber.trim(),
      hardwareId: normalizeHardwareId(sensor.hardwareId),
      model: sensor.model,
      waterFlexWorkOrderId: sensor.workOrderId.trim() || null,
      tankDepthCm,
      currentDistanceCm,
    };

    setSubmitting(true);
    setSubmitError('');
    try {
      setResult(await commissionSensor(request));
    } catch (error) {
      setSubmitError(error instanceof ApiError ? error.message : 'Sensor commissioning failed.');
    } finally {
      setSubmitting(false);
    }
  }

  async function copyToken() {
    if (!result) {
      return;
    }
    await navigator.clipboard.writeText(result.deviceToken);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 2200);
  }

  function resetWorkflow() {
    setStep('account');
    setFurthestStep(0);
    setQuery('');
    setCustomerId('');
    setLocationId('');
    setTankId('');
    setSensor(INITIAL_SENSOR);
    setTankDepth('150');
    clearSensorReading();
    setSubmitError('');
    setResult(null);
    setCopied(false);
  }

  if (result) {
    return (
      <CompletionScreen
        result={result}
        hardwareId={normalizeHardwareId(sensor.hardwareId)}
        copied={copied}
        onCopy={copyToken}
        onRestart={resetWorkflow}
      />
    );
  }

  return (
    <div className="workflow-layout">
      <StepRail
        currentIndex={currentIndex}
        furthestStep={furthestStep}
        onSelect={(selectedStep) => setStep(selectedStep)}
      />

      <section className="workflow-main" aria-labelledby="workflow-title">
        <header className="workflow-heading">
          <div>
            <span className="eyebrow">Installation record</span>
            <h1 id="workflow-title">{STEPS[currentIndex].label}</h1>
          </div>
          <span className="draft-status"><span /> Draft</span>
        </header>

        <div className="mobile-progress" aria-label={`Step ${currentIndex + 1} of ${STEPS.length}`}>
          <span>Step {currentIndex + 1} of {STEPS.length}</span>
          <strong>{STEPS[currentIndex].shortLabel}</strong>
          <div><span style={{ width: `${((currentIndex + 1) / STEPS.length) * 100}%` }} /></div>
        </div>

        <div className="step-content" key={step}>
          {step === 'account' && (
            <CustomerStep
              query={query}
              customers={customers}
              selectedCustomerId={customerId}
              loading={directoryLoading}
              error={directoryError}
              onQueryChange={setQuery}
              onSelect={selectCustomer}
            />
          )}
          {step === 'location' && selectedCustomer && (
            <LocationStep
              customer={selectedCustomer}
              selectedLocationId={locationId}
              selectedTankId={tankId}
              onSelectLocation={selectLocation}
              onSelectTank={selectTank}
            />
          )}
          {step === 'sensor' && (
            <SensorStep sensor={sensor} onChange={updateSensor} />
          )}
          {step === 'calibration' && (
            <CalibrationStep
              tankDepth={tankDepth}
              sensorReading={sensorReading}
              sensorReadProgress={sensorReadProgress}
              sensorReadError={sensorReadError}
              readingSensor={readingSensor}
              valid={calibrationValid}
              onTankDepthChange={setTankDepth}
              onReadSensor={captureSensorReading}
              onUseBenchReading={useBenchReading}
            />
          )}
          {step === 'review' && selectedCustomer && selectedLocation && selectedTank && (
            <ReviewStep
              customer={selectedCustomer}
              location={selectedLocation}
              tank={selectedTank}
              sensor={sensor}
              technicianName={currentUser?.displayName ?? 'Not selected'}
              dealerName={currentUser?.dealerName ?? 'Not selected'}
              tankDepthCm={tankDepthCm}
              currentDistanceCm={currentDistanceCm}
              error={submitError}
              submitting={submitting}
              onEdit={setStep}
              onSubmit={submitCommissioning}
            />
          )}
        </div>

        {step !== 'review' && (
          <footer className="workflow-actions">
            <button className="button button-secondary" type="button" onClick={back} disabled={currentIndex === 0}>
              <ArrowLeft size={18} /> Back
            </button>
            <button className="button button-primary" type="button" onClick={advance} disabled={!canContinue}>
              Continue <ArrowRight size={18} />
            </button>
          </footer>
        )}
      </section>

      <aside className="job-context" aria-label="Current installation">
        <div className="context-heading">
          <Wrench size={18} />
          <span>Current installation</span>
        </div>
        <ContextItem icon={Building2} label="Account" value={selectedCustomer?.displayName ?? 'Not selected'} />
        <ContextItem icon={MapPin} label="Location" value={selectedLocation?.displayName ?? 'Not selected'} />
        <ContextItem icon={Gauge} label="Tank" value={selectedTank?.label ?? 'Not selected'} />
        <ContextItem icon={Cpu} label="Sensor" value={sensor.serialNumber || 'Not entered'} />
        <div className="context-rule" />
        <div className="connection-state">
          <span><Wifi size={15} /> WaterFlex API</span>
          <strong className={directoryError ? 'connection-error' : ''}>
            {directoryError ? 'Unavailable' : directoryLoading ? 'Checking' : 'Connected'}
          </strong>
        </div>
      </aside>
    </div>
  );
}

function StepRail({
  currentIndex,
  furthestStep,
  onSelect,
}: {
  currentIndex: number;
  furthestStep: number;
  onSelect: (step: StepId) => void;
}) {
  return (
    <nav className="step-rail" aria-label="Provisioning steps">
      <div className="rail-title">Provisioning</div>
      <ol>
        {STEPS.map((step, index) => {
          const Icon = step.icon;
          const complete = index < currentIndex;
          const available = index <= furthestStep;
          return (
            <li key={step.id} className={index < STEPS.length - 1 ? 'has-line' : ''}>
              <button
                type="button"
                className={index === currentIndex ? 'active' : complete ? 'complete' : ''}
                disabled={!available}
                aria-current={index === currentIndex ? 'step' : undefined}
                onClick={() => onSelect(step.id)}
              >
                <span className="step-icon">{complete ? <Check size={16} /> : <Icon size={17} />}</span>
                <span>
                  <small>0{index + 1}</small>
                  <strong>{step.shortLabel}</strong>
                </span>
              </button>
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

function CustomerStep({
  query,
  customers,
  selectedCustomerId,
  loading,
  error,
  onQueryChange,
  onSelect,
}: {
  query: string;
  customers: WaterFlexCustomerOption[];
  selectedCustomerId: string;
  loading: boolean;
  error: string;
  onQueryChange: (value: string) => void;
  onSelect: (customer: WaterFlexCustomerOption) => void;
}) {
  return (
    <div className="step-section">
      <div className="section-intro">
        <h2>Select the WaterFlex account</h2>
        <p>Search by customer, account number, location, or address.</p>
      </div>
      <label className="search-field">
        <Search size={19} />
        <input
          type="search"
          value={query}
          onChange={(event) => onQueryChange(event.target.value)}
          placeholder="Search WaterFlex"
          autoFocus
        />
        {loading && <LoaderCircle className="spin" size={18} aria-label="Loading customers" />}
      </label>

      {error && (
        <div className="inline-alert error" role="alert">
          <XCircle size={18} /> <span>{error}</span>
        </div>
      )}

      <div className="selection-list" aria-live="polite">
        {!loading && customers.length === 0 && (
          <div className="empty-state">No WaterFlex accounts match this search.</div>
        )}
        {customers.map((customer) => (
          <button
            type="button"
            key={customer.waterFlexCustomerId}
            className={`selection-row ${selectedCustomerId === customer.waterFlexCustomerId ? 'selected' : ''}`}
            aria-pressed={selectedCustomerId === customer.waterFlexCustomerId}
            onClick={() => onSelect(customer)}
          >
            <span className="selection-symbol"><Building2 size={20} /></span>
            <span className="selection-copy">
              <strong>{customer.displayName}</strong>
              <small>Account {customer.accountNumber} · {customer.locations.length} {customer.locations.length === 1 ? 'location' : 'locations'}</small>
            </span>
            <span className="radio-indicator"><Check size={15} /></span>
          </button>
        ))}
      </div>
    </div>
  );
}

function LocationStep({
  customer,
  selectedLocationId,
  selectedTankId,
  onSelectLocation,
  onSelectTank,
}: {
  customer: WaterFlexCustomerOption;
  selectedLocationId: string;
  selectedTankId: string;
  onSelectLocation: (location: WaterFlexLocationOption) => void;
  onSelectTank: (tank: WaterFlexTankOption) => void;
}) {
  const selectedLocation = customer.locations.find(
    (location) => location.waterFlexLocationId === selectedLocationId,
  );

  return (
    <div className="step-section">
      <div className="section-intro">
        <h2>Choose the service location</h2>
        <p>{customer.displayName} · Account {customer.accountNumber}</p>
      </div>
      <div className="location-grid">
        <div>
          <div className="field-label">Location</div>
          <div className="selection-list compact">
            {customer.locations.map((location) => (
              <button
                type="button"
                key={location.waterFlexLocationId}
                className={`selection-row ${selectedLocationId === location.waterFlexLocationId ? 'selected' : ''}`}
                aria-pressed={selectedLocationId === location.waterFlexLocationId}
                onClick={() => onSelectLocation(location)}
              >
                <span className="selection-symbol"><MapPin size={19} /></span>
                <span className="selection-copy">
                  <strong>{location.displayName}</strong>
                  <small>{location.addressSummary}</small>
                </span>
                <span className="radio-indicator"><Check size={15} /></span>
              </button>
            ))}
          </div>
        </div>

        <div>
          <div className="field-label">Tank</div>
          {!selectedLocation && <div className="empty-state bordered">Select a location to view tanks.</div>}
          {selectedLocation && (
            <div className="selection-list compact">
              {selectedLocation.tanks.map((tank) => (
                <button
                  type="button"
                  key={tank.waterFlexAssetId}
                  className={`selection-row ${selectedTankId === tank.waterFlexAssetId ? 'selected' : ''}`}
                  aria-pressed={selectedTankId === tank.waterFlexAssetId}
                  onClick={() => onSelectTank(tank)}
                >
                  <span className="selection-symbol"><Gauge size={19} /></span>
                  <span className="selection-copy">
                    <strong>{tank.label}</strong>
                    <small>{tank.capacityPounds ? `${tank.capacityPounds} lb capacity` : 'Capacity not recorded'}</small>
                  </span>
                  <span className="radio-indicator"><Check size={15} /></span>
                </button>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function SensorStep({
  sensor,
  onChange,
}: {
  sensor: SensorForm;
  onChange: (sensor: SensorForm) => void;
}) {
  const setField = (field: keyof SensorForm, value: string) => onChange({ ...sensor, [field]: value });
  const normalizedHardwareId = normalizeHardwareId(sensor.hardwareId);

  return (
    <div className="step-section">
      <div className="section-intro">
        <h2>Record the controller identity</h2>
        <p>Use the serial label and ESP32 hardware ID from the assembly test record.</p>
      </div>
      <div className="form-grid two-column">
        <label className="form-field">
          <span>WaterFlex serial</span>
          <input
            value={sensor.serialNumber}
            onChange={(event) => setField('serialNumber', event.target.value.toUpperCase())}
            placeholder="WF-NANO-0001"
            autoComplete="off"
          />
          <small>Printed on the enclosure QR label.</small>
        </label>
        <label className="form-field">
          <span>ESP32 hardware ID</span>
          <input
            value={sensor.hardwareId}
            onChange={(event) => setField('hardwareId', event.target.value.toUpperCase())}
            placeholder="A1:B2:C3:D4:E5:F6"
            autoComplete="off"
            aria-invalid={sensor.hardwareId.length > 0 && normalizedHardwareId.length !== 12}
          />
          <small>12 hexadecimal characters; separators are optional.</small>
        </label>
        <div className="form-field">
          <span>Controller model</span>
          <ThemedSelect
            value={sensor.model}
            ariaLabel="Controller model"
            options={[
              { value: 'Arduino Nano ESP32', label: 'Arduino Nano ESP32' },
              { value: 'WaterFlex ESP32 Pilot', label: 'WaterFlex ESP32 Pilot' },
            ]}
            onValueChange={(value) => setField('model', value)}
          />
        </div>
        <label className="form-field span-two">
          <span>WaterFlex work order <em>Optional</em></span>
          <input
            value={sensor.workOrderId}
            onChange={(event) => setField('workOrderId', event.target.value.toUpperCase())}
            placeholder="WO-82417"
            autoComplete="off"
          />
        </label>
      </div>
    </div>
  );
}

function CalibrationStep({
  tankDepth,
  sensorReading,
  sensorReadProgress,
  sensorReadError,
  readingSensor,
  valid,
  onTankDepthChange,
  onReadSensor,
  onUseBenchReading,
}: {
  tankDepth: string;
  sensorReading: SensorDistanceReading | null;
  sensorReadProgress: SensorReadProgress | null;
  sensorReadError: string;
  readingSensor: boolean;
  valid: boolean;
  onTankDepthChange: (value: string) => void;
  onReadSensor: () => void;
  onUseBenchReading: () => void;
}) {
  const depth = Number(tankDepth);
  const distance = sensorReading ? sensorReading.distanceMm / 10 : 0;
  const depthValid = Number.isFinite(depth) && depth >= 10 && depth <= 450;
  const readingWithinTank = sensorReading === null || distance <= depth;
  const materialDepth = valid ? depth - distance : 0;
  const fillPercent = valid ? Math.max(0, Math.min(100, materialDepth / depth * 100)) : 0;
  const thresholdDistance = valid ? Math.round(depth * 0.65) : null;
  const serialSupported = supportsSensorSerial();

  return (
    <div className="step-section calibration-layout">
      <div>
        <div className="section-intro">
          <h2>Measure tank depth and capture the sensor</h2>
          <p>The tank depth is measured once. Surface distance is captured directly from the connected sensor.</p>
        </div>
        <div className="form-grid calibration-fields">
          <label className="form-field">
            <span>Usable tank depth</span>
            <div className="number-input"><input type="number" min="10" max="450" step="0.1" value={tankDepth} onChange={(event) => onTankDepthChange(event.target.value)} /><b>cm</b></div>
            <small>Sensor face to the inside bottom of the tank.</small>
          </label>
          <div className="form-field sensor-capture-field">
            <span>Live sensor distance</span>
            <div className={`sensor-capture ${sensorReading ? 'captured' : ''} ${sensorReadError ? 'has-error' : ''}`}>
              <div className="sensor-capture-value" aria-live="polite">
                <Usb size={19} />
                <div>
                  <strong>
                    {sensorReading
                      ? `${(sensorReading.distanceMm / 10).toFixed(1)} cm`
                      : readingSensor && sensorReadProgress
                        ? `${(sensorReadProgress.latestDistanceMm / 10).toFixed(1)} cm`
                        : readingSensor
                          ? 'Connecting'
                          : 'Not captured'}
                  </strong>
                  <small>
                    {sensorReading
                      ? sensorReading.source === 'bench'
                        ? 'Bench estimate · replace after sensor installation'
                        : `${sensorReading.sampleCount} samples · ${sensorReading.spreadMm} mm spread`
                      : readingSensor && sensorReadProgress
                        ? `Sample ${sensorReadProgress.sampleCount} of ${sensorReadProgress.targetSampleCount}`
                        : serialSupported
                          ? 'USB sensor ready'
                          : 'Web Serial unavailable'}
                  </small>
                </div>
              </div>
              <button
                className="button button-secondary sensor-read-button"
                type="button"
                disabled={readingSensor || !serialSupported}
                onClick={onReadSensor}
              >
                {readingSensor
                  ? <><LoaderCircle className="spin" size={16} /> Reading</>
                  : sensorReading
                    ? <><RefreshCw size={16} /> Read again</>
                    : <><Usb size={16} /> Read sensor</>}
              </button>
              <button
                className="button button-secondary sensor-read-button"
                type="button"
                disabled={readingSensor || !depthValid}
                onClick={onUseBenchReading}
              >
                <Wrench size={16} /> Use bench estimate
              </button>
            </div>
            {sensorReadError && <small className="sensor-read-error" role="alert">{sensorReadError}</small>}
            {sensorReading?.source === 'bench' && (
              <small className="sensor-read-warning" role="status">
                This enables controller connectivity testing only. Recommission calibration from live sensor samples before using fill levels operationally.
              </small>
            )}
          </div>
        </div>
        {!depthValid && (
          <div className="inline-alert warning" role="alert">
            <Gauge size={18} />
            <span>Use a tank depth between 10 and 450 cm.</span>
          </div>
        )}
        {depthValid && !readingWithinTank && (
          <div className="inline-alert warning" role="alert">
            <Gauge size={18} />
            <span>The live sensor distance exceeds tank depth. Check the depth measurement and sensor alignment.</span>
          </div>
        )}
        {valid && (
          <>
            <div className="formula-line">
              <span>Fill = (tank depth − sensor distance) ÷ tank depth</span>
              <strong>{fillPercent.toFixed(1)}%</strong>
            </div>
            <dl className="calibration-metrics">
              <div><dt>Current material depth</dt><dd>{materialDepth.toFixed(1)} cm</dd></div>
              <div><dt>35% trigger reading</dt><dd>{thresholdDistance?.toFixed(1)} cm</dd></div>
              <div><dt>Initial fill</dt><dd>{fillPercent.toFixed(1)}%</dd></div>
            </dl>
          </>
        )}
      </div>
      <CalibrationGraphic tankDepth={depth} currentDistance={distance} valid={valid} />
    </div>
  );
}

function CalibrationGraphic({ tankDepth, currentDistance, valid }: { tankDepth: number; currentDistance: number; valid: boolean }) {
  const surfacePosition = valid ? Math.max(8, Math.min(90, currentDistance / tankDepth * 100)) : 35;
  return (
    <figure className="tank-figure">
      <figcaption>Tank level profile</figcaption>
      <div className="tank-shell">
        <div className="sensor-head"><span /><span /><span /></div>
        <div className="salt-fill" style={{ top: `${surfacePosition + 1}%`, bottom: '7px' }} />
        <div className="measure-line water-line" style={{ top: `${surfacePosition}%` }}><span>CURRENT SURFACE</span><b>{valid ? currentDistance : '—'} cm</b></div>
        <div className="measure-line full-line tank-bottom-line"><span>TANK BOTTOM</span><b>{valid ? tankDepth : '—'} cm</b></div>
      </div>
    </figure>
  );
}

function ReviewStep({
  customer,
  location,
  tank,
  sensor,
  technicianName,
  dealerName,
  tankDepthCm,
  currentDistanceCm,
  error,
  submitting,
  onEdit,
  onSubmit,
}: {
  customer: WaterFlexCustomerOption;
  location: WaterFlexLocationOption;
  tank: WaterFlexTankOption;
  sensor: SensorForm;
  technicianName: string;
  dealerName: string;
  tankDepthCm: number;
  currentDistanceCm: number;
  error: string;
  submitting: boolean;
  onEdit: (step: StepId) => void;
  onSubmit: () => void;
}) {
  return (
    <div className="step-section review-section">
      <div className="section-intro">
        <h2>Verify the installation record</h2>
        <p>Commissioning writes the customer mapping, calibration, and credential in one transaction.</p>
      </div>
      <div className="review-groups">
        <ReviewGroup title="WaterFlex assignment" icon={MapPin} onEdit={() => onEdit('location')}>
          <ReviewRow label="Account" value={`${customer.displayName} · ${customer.accountNumber}`} />
          <ReviewRow label="Location" value={location.displayName} />
          <ReviewRow label="Address" value={location.addressSummary} />
          <ReviewRow label="Tank" value={tank.label} />
        </ReviewGroup>
        <ReviewGroup title="Sensor and installer" icon={Cpu} onEdit={() => onEdit('sensor')}>
          <ReviewRow label="Serial" value={sensor.serialNumber} mono />
          <ReviewRow label="Hardware ID" value={normalizeHardwareId(sensor.hardwareId)} mono />
          <ReviewRow label="Model" value={sensor.model} />
          <ReviewRow label="Technician" value={technicianName} />
          <ReviewRow label="Dealer" value={dealerName} />
          <ReviewRow label="Work order" value={sensor.workOrderId || 'Not recorded'} />
        </ReviewGroup>
        <ReviewGroup title="Calibration" icon={Gauge} onEdit={() => onEdit('calibration')}>
          <ReviewRow label="Tank depth" value={`${tankDepthCm} cm`} />
          <ReviewRow label="Sensor reading" value={`${currentDistanceCm} cm`} />
          <ReviewRow label="Initial fill" value={`${((tankDepthCm - currentDistanceCm) / tankDepthCm * 100).toFixed(1)}%`} />
        </ReviewGroup>
      </div>
      <div className="security-note">
        <ShieldCheck size={20} />
        <div><strong>Credential handoff</strong><span>The device token appears once after commissioning and is never stored in plaintext.</span></div>
      </div>
      {error && <div className="inline-alert error" role="alert"><XCircle size={18} /><span>{error}</span></div>}
      <div className="review-actions">
        <button className="button button-secondary" type="button" onClick={() => onEdit('calibration')} disabled={submitting}><ArrowLeft size={18} /> Back</button>
        <button className="button button-primary commission-button" type="button" onClick={onSubmit} disabled={submitting}>
          {submitting ? <><LoaderCircle className="spin" size={18} /> Commissioning</> : <><ShieldCheck size={18} /> Commission sensor</>}
        </button>
      </div>
    </div>
  );
}

function CompletionScreen({
  result,
  hardwareId,
  copied,
  onCopy,
  onRestart,
}: {
  result: CommissionSensorResponse;
  hardwareId: string;
  copied: boolean;
  onCopy: () => void;
  onRestart: () => void;
}) {
  const hardwareSuffix = hardwareId.slice(-6);
  const browserHost = window.location.hostname;
  const telemetryHost = browserHost === 'localhost' || browserHost === '127.0.0.1'
    ? '<this-computer-LAN-IP>'
    : 'telemetry-staging.saltmonitor.dev';
  const telemetryUrl = telemetryHost.startsWith('<')
    ? `http://${telemetryHost}:5188/api/v1/device/telemetry`
    : `https://${telemetryHost}/api/v1/device/telemetry`;

  return (
    <section className="completion-screen" aria-labelledby="complete-title">
      <div className="success-mark"><CheckCircle2 size={34} /></div>
      <span className="eyebrow">Commissioning complete</span>
      <h1 id="complete-title">{result.serialNumber} is active</h1>
      <p>{result.customerDisplayName} · {result.locationDisplayName} · {result.tankLabel}</p>

      <div className="credential-panel">
        <div className="credential-heading">
          <span><ShieldCheck size={18} /> Device credential</span>
          <strong>Shown once</strong>
        </div>
        <code>{result.deviceToken}</code>
        <button className="button button-primary" type="button" onClick={onCopy}>
          {copied ? <><Check size={18} /> Copied</> : <><Copy size={18} /> Copy token</>}
        </button>
      </div>

      <div className="completion-details">
        <div><small>Device ID</small><code>{result.deviceId}</code></div>
        <div><small>Installation ID</small><code>{result.installationId}</code></div>
        <div><small>Initial fill</small><strong>{result.initialFillPercent.toFixed(1)}%</strong></div>
      </div>

      <section className="device-handoff" aria-labelledby="device-handoff-title">
        <div className="device-handoff-heading">
          <Wifi size={19} />
          <div><small>ESP32 handoff</small><h2 id="device-handoff-title">Connect this sensor</h2></div>
        </div>
        <ol>
          <li><span>1</span><div>Hold D2 for 5 seconds, then join <code>WaterFlex-{hardwareSuffix}</code>.</div></li>
          <li><span>2</span><div>Open <code>http://192.168.4.1/</code> and enter the site's 2.4 GHz Wi-Fi.</div></li>
          <li><span>3</span><div>Set the telemetry URL to <code>{telemetryUrl}</code> and paste the token shown above.</div></li>
          <li><span>4</span><div>Restart the sensor after status reports connected. Its configuration persists until a 15-second D2 reset.</div></li>
        </ol>
        {telemetryHost.startsWith('<') && (
          <div className="inline-alert warning">
            <Wifi size={18} />
            <span>Replace the LAN-IP placeholder with this computer's address on the same Wi-Fi network.</span>
          </div>
        )}
      </section>

      <div className="next-checks">
        <span><Check size={16} /> WaterFlex assignment saved</span>
        <span><Check size={16} /> Device credential issued</span>
        <span><Check size={16} /> Calibration version 1 active</span>
      </div>

      <button className="button button-secondary" type="button" onClick={onRestart}><RefreshCw size={18} /> Commission another sensor</button>
    </section>
  );
}

function ReviewGroup({
  title,
  icon: Icon,
  onEdit,
  children,
}: {
  title: string;
  icon: LucideIcon;
  onEdit: () => void;
  children: React.ReactNode;
}) {
  return (
    <section className="review-group">
      <header><span><Icon size={18} /> {title}</span><button type="button" onClick={onEdit}>Edit</button></header>
      <dl>{children}</dl>
    </section>
  );
}

function ReviewRow({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return <div><dt>{label}</dt><dd className={mono ? 'mono' : ''}>{value}</dd></div>;
}

function ContextItem({ icon: Icon, label, value }: { icon: LucideIcon; label: string; value: string }) {
  return (
    <div className="context-item">
      <Icon size={16} />
      <span><small>{label}</small><strong>{value}</strong></span>
    </div>
  );
}

function normalizeHardwareId(value: string) {
  return value.replace(/[:\s-]/g, '').toUpperCase();
}
