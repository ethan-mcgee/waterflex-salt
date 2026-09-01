import {
  AlertTriangle,
  ChevronLeft,
  ChevronRight,
  CircleCheck,
  Clock3,
  Gauge,
  Radio,
  RefreshCw,
  Search,
  SlidersHorizontal,
  WifiOff,
} from 'lucide-react';
import {
  startTransition,
  useDeferredValue,
  useEffect,
  useState,
} from 'react';
import type { ReactNode } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import ThemedSelect from '../components/ThemedSelect';
import { useDevelopmentIdentity } from '../development/DevelopmentIdentity';
import { getFleetDealers, getFleetDevices, getFleetSummary } from './api';
import type {
  FleetDealerOption,
  FleetDevice,
  FleetFilters,
  FleetPageResult,
  FleetSort,
  FleetSummary,
  ReportingStatus,
} from './types';

const EMPTY_SUMMARY: FleetSummary = {
  generatedAtUtc: '',
  totalProvisioned: 0,
  active: 0,
  belowThreshold: 0,
  reporting: 0,
  stale: 0,
  offline: 0,
  neverReported: 0,
};

/**
 * Internal sensor fleet table. All filter/sort/page state lives in the URL's search params (not
 * component state) so the view is shareable/bookmarkable; `deferredSearch` uses
 * `useDeferredValue` to keep the search input responsive while the fleet re-fetches. Re-fetches
 * summary, devices, and dealers together whenever any filter, the manual refresh counter, or the
 * signed-in user changes.
 */
export default function FleetPage() {
  const { selectedUserId } = useDevelopmentIdentity();
  const [searchParams, setSearchParams] = useSearchParams();
  const deferredSearch = useDeferredValue(searchParams.get('search') ?? '');
  const reportingStatus = asReportingStatus(searchParams.get('status'));
  const dealerFilter = searchParams.get('dealer') ?? '';
  const fillFilter = searchParams.get('fill');
  const sort = asFleetSort(searchParams.get('sort'));
  const pageNumber = Math.max(1, Number(searchParams.get('page')) || 1);
  const [summary, setSummary] = useState<FleetSummary>(EMPTY_SUMMARY);
  const [fleet, setFleet] = useState<FleetPageResult | null>(null);
  const [dealers, setDealers] = useState<FleetDealerOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [refreshVersion, setRefreshVersion] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    const filters: FleetFilters = {
      search: deferredSearch,
      reportingStatus,
      belowThreshold: fillFilter === 'low' ? true : undefined,
      dealerId: dealerFilter || undefined,
      sort,
      page: pageNumber,
      pageSize: 25,
    };

    setLoading(true);
    setError('');
    Promise.all([
      getFleetSummary(filters, controller.signal),
      getFleetDevices(filters, controller.signal),
      getFleetDealers(controller.signal),
    ]).then(([summaryResult, fleetResult, dealerResults]) => {
      setSummary(summaryResult);
      setFleet(fleetResult);
      setDealers(dealerResults);
      setLoading(false);
    }).catch((reason: unknown) => {
      if (controller.signal.aborted) return;
      setError(reason instanceof Error ? reason.message : 'Unable to load the sensor fleet.');
      setLoading(false);
    });

    return () => controller.abort();
  }, [dealerFilter, deferredSearch, fillFilter, pageNumber, refreshVersion, reportingStatus, selectedUserId, sort]);

  function updateParam(key: string, value?: string) {
    const next = new URLSearchParams(searchParams);
    if (value) next.set(key, value);
    else next.delete(key);
    if (key !== 'page') next.delete('page');
    startTransition(() => setSearchParams(next));
  }

  const totalPages = fleet ? Math.max(1, Math.ceil(fleet.totalCount / fleet.pageSize)) : 1;

  return (
    <section className="fleet-page" aria-labelledby="fleet-heading">
      <header className="fleet-heading">
        <div>
          <span className="eyebrow">Internal operations</span>
          <h1 id="fleet-heading">Sensor fleet</h1>
          <p>Latest salt levels and reporting health across every provisioned installation.</p>
        </div>
        <div className="fleet-heading-actions">
          <span className="fleet-updated">
            {fleet ? `Updated ${formatRelative(fleet.generatedAtUtc, fleet.generatedAtUtc)}` : 'Waiting for data'}
          </span>
          <button
            className="icon-button"
            type="button"
            title="Refresh fleet"
            aria-label="Refresh fleet"
            onClick={() => setRefreshVersion((value) => value + 1)}
          >
            <RefreshCw size={17} className={loading ? 'spin' : ''} />
          </button>
          <Link className="button button-primary fleet-provision" to="/provision">
            Provision sensor
          </Link>
        </div>
      </header>

      <div className="fleet-metrics" aria-label="Fleet summary">
        <Metric label="Provisioned" value={summary.totalProvisioned} icon={<Gauge size={18} />} />
        <Metric label="Reporting" value={summary.reporting} tone="success" icon={<Radio size={18} />} />
        <Metric label="Below 35%" value={summary.belowThreshold} tone="warning" icon={<AlertTriangle size={18} />} />
        <Metric label="Stale" value={summary.stale} tone="warning" icon={<Clock3 size={18} />} />
        <Metric label="Offline" value={summary.offline} tone="danger" icon={<WifiOff size={18} />} />
        <Metric label="Never reported" value={summary.neverReported} icon={<Clock3 size={18} />} />
      </div>

      <div className="fleet-toolbar">
        <label className="fleet-search">
          <Search size={17} />
          <span className="sr-only">Search sensors</span>
          <input
            type="search"
            value={searchParams.get('search') ?? ''}
            onChange={(event) => updateParam('search', event.target.value)}
            placeholder="Search customer, address, tank, or serial"
          />
        </label>
        <div className="fleet-select">
          <span className="sr-only">Dealer</span>
          <ThemedSelect
            value={dealerFilter}
            ariaLabel="Dealer"
            options={[
              { value: '', label: 'All dealers' },
              ...dealers.map((dealer) => ({ value: dealer.externalId, label: dealer.displayName })),
              { value: 'unassigned', label: 'Unassigned' },
            ]}
            onValueChange={(value) => updateParam('dealer', value)}
          />
        </div>
        <div className="fleet-select">
          <SlidersHorizontal size={15} />
          <span className="sr-only">Reporting status</span>
          <ThemedSelect
            value={reportingStatus ?? ''}
            ariaLabel="Reporting status"
            options={[
              { value: '', label: 'All reporting states' },
              { value: 'reporting', label: 'Reporting' },
              { value: 'stale', label: 'Stale' },
              { value: 'offline', label: 'Offline' },
              { value: 'neverReported', label: 'Never reported' },
            ]}
            onValueChange={(value) => updateParam('status', value)}
          />
        </div>
        <div className="fleet-select">
          <span className="sr-only">Fill status</span>
          <ThemedSelect
            value={fillFilter ?? ''}
            ariaLabel="Fill status"
            options={[
              { value: '', label: 'All fill levels' },
              { value: 'low', label: 'Below 35%' },
            ]}
            onValueChange={(value) => updateParam('fill', value)}
          />
        </div>
        <div className="fleet-select fleet-sort">
          <span className="sr-only">Sort sensors</span>
          <ThemedSelect
            value={sort}
            ariaLabel="Sort sensors"
            options={[
              { value: 'attention', label: 'Needs attention' },
              { value: 'lastReported', label: 'Latest report' },
              { value: 'fillAscending', label: 'Lowest fill' },
              { value: 'fillDescending', label: 'Highest fill' },
              { value: 'customer', label: 'Customer' },
            ]}
            onValueChange={(value) => updateParam('sort', value)}
          />
        </div>
      </div>

      <div className="fleet-table-shell">
        {error ? (
          <div className="fleet-message error-message" role="alert">
            <AlertTriangle size={20} />
            <div><strong>Fleet data unavailable</strong><span>{error}</span></div>
            <button className="button button-secondary" type="button" onClick={() => setRefreshVersion((value) => value + 1)}>Retry</button>
          </div>
        ) : loading && !fleet ? (
          <div className="fleet-message"><RefreshCw className="spin" size={20} /> Loading sensor fleet...</div>
        ) : fleet?.items.length === 0 ? (
          <div className="fleet-message">
            <Gauge size={22} />
            <div><strong>No sensors match this view</strong><span>Adjust the filters or provision the first sensor.</span></div>
          </div>
        ) : (
          <div className={loading ? 'fleet-table-wrap is-refreshing' : 'fleet-table-wrap'}>
            <table className="fleet-table">
              <thead>
                <tr>
                  <th>Sensor</th>
                  <th>Fill</th>
                  <th>Reporting</th>
                  <th>Dealer</th>
                  <th>Customer / installation</th>
                  <th>Signal</th>
                  <th>Firmware</th>
                  <th>Last report</th>
                  <th><span className="sr-only">Open</span></th>
                </tr>
              </thead>
              <tbody>
                {fleet?.items.map((device) => (
                  <FleetRow key={device.deviceId} device={device} now={fleet.generatedAtUtc} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {fleet && fleet.totalCount > 0 && (
        <footer className="fleet-pagination">
          <span>{fleet.totalCount} sensor{fleet.totalCount === 1 ? '' : 's'}</span>
          <div>
            <button
              className="icon-button"
              type="button"
              aria-label="Previous page"
              disabled={pageNumber <= 1}
              onClick={() => updateParam('page', String(pageNumber - 1))}
            ><ChevronLeft size={17} /></button>
            <span>Page {pageNumber} of {totalPages}</span>
            <button
              className="icon-button"
              type="button"
              aria-label="Next page"
              disabled={pageNumber >= totalPages}
              onClick={() => updateParam('page', String(pageNumber + 1))}
            ><ChevronRight size={17} /></button>
          </div>
        </footer>
      )}
    </section>
  );
}

function Metric({
  label,
  value,
  icon,
  tone = 'neutral',
}: {
  label: string;
  value: number;
  icon: ReactNode;
  tone?: 'neutral' | 'success' | 'warning' | 'danger';
}) {
  return (
    <div className={`fleet-metric ${tone}`}>
      <span>{icon}</span>
      <div><strong>{value}</strong><small>{label}</small></div>
    </div>
  );
}

function FleetRow({ device, now }: { device: FleetDevice; now: string }) {
  const fill = device.fillPercent === null ? null : Math.max(0, Math.min(100, device.fillPercent));
  return (
    <tr>
      <td>
        <Link className="sensor-link" to={`/fleet/${device.deviceId}`}>{device.serialNumber}</Link>
        <span className="table-secondary">{device.model}</span>
      </td>
      <td>
        {fill === null ? <span className="not-available">No reading</span> : (
          <div className={device.isBelowThreshold ? 'fill-cell is-low' : 'fill-cell'}>
            <strong>{fill.toFixed(1)}%</strong>
            <div className="fill-meter" role="progressbar" aria-label={`${fill.toFixed(1)} percent full`} aria-valuenow={fill} aria-valuemin={0} aria-valuemax={100}>
              <span style={{ width: `${fill}%` }} />
            </div>
          </div>
        )}
      </td>
      <td><ReportingBadge status={device.reportingStatus} /></td>
      <td>
        <strong className="table-primary">{device.dealerName}</strong>
        <span className="table-secondary">{device.dealerExternalId ?? 'Legacy installation'}</span>
      </td>
      <td>
        <strong className="table-primary">{device.customerDisplayName}</strong>
        <span className="table-secondary">{device.locationDisplayName} · {device.tankLabel}</span>
      </td>
      <td>
        {device.sensorStatus === 'faulted' ? (
          <><strong className="table-primary"><AlertTriangle size={13} /> Sensor fault</strong><span className="table-secondary">{formatSensorFault(device.sensorFault)}</span></>
        ) : device.quality === null ? <span className="not-available">—</span> : (
          <><strong className="table-primary">{device.quality}% quality</strong><span className="table-secondary">{device.wifiRssiDbm} dBm</span></>
        )}
      </td>
      <td>
        <span className="firmware-value">{device.firmwareVersion ?? '—'}</span>
        {device.errorFlags.length > 0 && <span className="error-count"><AlertTriangle size={12} /> {device.errorFlags.length}</span>}
      </td>
      <td>
        <span className="last-report" title={device.lastReportedAtUtc ? formatDateTime(device.lastReportedAtUtc) : undefined}>
          {device.lastReportedAtUtc ? formatRelative(device.lastReportedAtUtc, now) : 'Never'}
        </span>
      </td>
      <td><Link className="row-open" to={`/fleet/${device.deviceId}`} aria-label={`Open ${device.serialNumber}`}><ChevronRight size={18} /></Link></td>
    </tr>
  );
}

/** Small labeled badge for a device's reporting status; reused on the device detail page. */
export function ReportingBadge({ status }: { status: ReportingStatus }) {
  const labels: Record<ReportingStatus, string> = {
    reporting: 'Reporting',
    stale: 'Stale',
    offline: 'Offline',
    neverReported: 'Never reported',
  };
  const icons: Record<ReportingStatus, ReactNode> = {
    reporting: <CircleCheck size={13} />,
    stale: <Clock3 size={13} />,
    offline: <WifiOff size={13} />,
    neverReported: <Clock3 size={13} />,
  };
  return <span className={`reporting-badge ${status}`}>{icons[status]}{labels[status]}</span>;
}

/** Formats an ISO timestamp as a locale-aware medium date + short time string. */
export function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value));
}

/** Formats `value` relative to `nowValue` (e.g. "3 minutes ago"), picking the coarsest unit that keeps the number under 60 (or under 48 for hours). */
export function formatRelative(value: string, nowValue: string): string {
  const seconds = Math.round((new Date(value).getTime() - new Date(nowValue).getTime()) / 1000);
  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
  if (Math.abs(seconds) < 60) return formatter.format(seconds, 'second');
  const minutes = Math.round(seconds / 60);
  if (Math.abs(minutes) < 60) return formatter.format(minutes, 'minute');
  const hours = Math.round(minutes / 60);
  if (Math.abs(hours) < 48) return formatter.format(hours, 'hour');
  return formatter.format(Math.round(hours / 24), 'day');
}

function asReportingStatus(value: string | null): ReportingStatus | undefined {
  return value === 'reporting' || value === 'stale' || value === 'offline' || value === 'neverReported'
    ? value
    : undefined;
}

function asFleetSort(value: string | null): FleetSort {
  return value === 'lastReported' || value === 'fillAscending' || value === 'fillDescending' || value === 'customer'
    ? value
    : 'attention';
}

function formatSensorFault(value: FleetDevice['sensorFault']): string {
  if (!value) return 'Unknown sensor fault';
  return value.replace(/([A-Z])/g, ' $1').replace(/^./, (character) => character.toUpperCase());
}
