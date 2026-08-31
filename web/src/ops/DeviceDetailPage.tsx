import {
  Activity,
  AlertTriangle,
  ArrowLeft,
  Box,
  CalendarClock,
  Cpu,
  Gauge,
  KeyRound,
  MapPin,
  RefreshCw,
  UserRound,
} from 'lucide-react';
import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useDevelopmentIdentity } from '../development/DevelopmentIdentity';
import { getFleetDevice, getFleetHistory, getFleetReadings } from './api';
import { formatDateTime, ReportingBadge } from './FleetPage';
import type { FleetDevice, FleetDeviceDetail, FleetHistoryPoint, FleetReading } from './types';

export default function DeviceDetailPage() {
  const { selectedUserId } = useDevelopmentIdentity();
  const { deviceId = '' } = useParams();
  const [detail, setDetail] = useState<FleetDeviceDetail | null>(null);
  const [readings, setReadings] = useState<FleetReading[]>([]);
  const [historyPoints, setHistoryPoints] = useState<FleetHistoryPoint[]>([]);
  const [range, setRange] = useState<'24h' | '7d' | '30d'>('24h');
  const [detailLoading, setDetailLoading] = useState(true);
  const [detailError, setDetailError] = useState('');
  const [historyLoading, setHistoryLoading] = useState(true);
  const [historyError, setHistoryError] = useState('');
  const [historyRequestVersion, setHistoryRequestVersion] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setDetail(null);
    setDetailLoading(true);
    setDetailError('');
    getFleetDevice(deviceId, controller.signal).then((detailResult) => {
      setDetail(detailResult);
      setDetailLoading(false);
    }).catch((reason: unknown) => {
      if (controller.signal.aborted) return;
      setDetailError(reason instanceof Error ? reason.message : 'Unable to load sensor detail.');
      setDetailLoading(false);
    });
    return () => controller.abort();
  }, [deviceId, selectedUserId]);

  useEffect(() => {
    const controller = new AbortController();
    setReadings([]);
    setHistoryPoints([]);
    setHistoryLoading(true);
    setHistoryError('');
    const request = range === '24h'
      ? getFleetReadings(deviceId, range, controller.signal).then((readingResult) => {
        setReadings(readingResult);
      })
      : getFleetHistory(deviceId, range, 'auto', controller.signal).then((historyResult) => {
        setHistoryPoints(historyResult.points);
      });
    request.then(() => {
      setHistoryLoading(false);
    }).catch(() => {
      if (controller.signal.aborted) return;
      setHistoryError('Reading history could not be loaded. Check your connection and try again.');
      setHistoryLoading(false);
    });
    return () => controller.abort();
  }, [deviceId, range, selectedUserId, historyRequestVersion]);

  if (detailLoading && !detail) {
    return <div className="detail-state"><RefreshCw className="spin" size={22} /> Loading sensor detail...</div>;
  }

  if (detailError || !detail) {
    return (
      <div className="detail-state error-message" role="alert">
        <AlertTriangle size={22} />
        <div><strong>Sensor detail unavailable</strong><span>{detailError || 'This sensor was not found.'}</span></div>
        <Link className="button button-secondary" to="/fleet">Return to fleet</Link>
      </div>
    );
  }

  const device = detail.device;
  return (
    <section className="device-detail" aria-labelledby="device-heading">
      <Link className="detail-back" to="/fleet"><ArrowLeft size={16} /> Sensor fleet</Link>
      <header className="device-heading">
        <div>
          <span className="eyebrow">{device.dealerName} · {device.lifecycleStatus}</span>
          <h1 id="device-heading">{device.serialNumber}</h1>
          <p>{device.customerDisplayName} · {device.locationDisplayName}</p>
        </div>
        <ReportingBadge status={device.reportingStatus} />
      </header>

      <div className="device-overview">
        <section className={device.isBelowThreshold ? 'level-summary is-low' : 'level-summary'}>
          <div className="level-reading">
            <span>Latest fill</span>
            <strong>{device.fillPercent === null ? '—' : `${device.fillPercent.toFixed(1)}%`}</strong>
            <small>{device.isBelowThreshold ? 'Below 35% threshold' : device.fillPercent === null ? 'Awaiting first reading' : 'Above delivery threshold'}</small>
          </div>
          <div className="level-column" aria-hidden="true">
            <span style={{ height: `${Math.max(0, Math.min(100, device.fillPercent ?? 0))}%` }} />
          </div>
        </section>

        <section className="detail-panel">
          <h2><MapPin size={16} /> Installation</h2>
          <DetailRow label="Customer" value={device.customerDisplayName} />
          <DetailRow label="Account" value={device.accountNumber ?? 'Not available'} />
          <DetailRow label="Location" value={device.locationDisplayName} />
          <DetailRow label="Tank" value={device.tankLabel} />
          <DetailRow label="Installed" value={formatDateTime(detail.installedAtUtc)} />
        </section>

        <section className="detail-panel">
          <h2><Cpu size={16} /> Device health</h2>
          <DetailRow label="Sensor" value={device.sensorStatus === 'faulted' ? `Faulted: ${formatSensorFault(device.sensorFault)}` : device.sensorStatus} />
          <DetailRow label="Health heartbeat" value={device.lastHealthReportedAtUtc ? formatDateTime(device.lastHealthReportedAtUtc) : 'Never'} />
          <DetailRow label="Clock" value={device.clockSynchronized ? 'Synchronized' : 'Not synchronized'} />
          <DetailRow label="Queued readings" value={device.queuedReadingCount.toString()} />
          <DetailRow label="Dropped readings" value={device.droppedReadingCount.toString()} />
          <DetailRow label="Quality" value={device.quality === null ? 'No reading' : `${device.quality}%`} />
          <DetailRow label="Wi-Fi" value={device.wifiRssiDbm === null ? 'No reading' : `${device.wifiRssiDbm} dBm`} />
          <DetailRow label="Firmware" value={device.firmwareVersion ?? 'No reading'} />
          <DetailRow label="Raw distance" value={device.rawDistanceMm === null ? 'No reading' : `${device.rawDistanceMm} mm`} />
          <DetailRow label="Errors" value={device.errorFlags.length ? device.errorFlags.join(', ') : 'None reported'} />
        </section>
      </div>

      <section className="history-section">
        <div className="section-heading-row">
          <div><span className="eyebrow">Diagnostics</span><h2>Reading history</h2></div>
          <div className="range-control" aria-label="Reading range">
            {(['24h', '7d', '30d'] as const).map((value) => (
              <button key={value} type="button" className={range === value ? 'active' : ''} onClick={() => setRange(value)}>{value}</button>
            ))}
          </div>
        </div>
        {historyLoading ? (
          <div className="history-empty"><RefreshCw className="spin" size={20} /> Loading reading history...</div>
        ) : historyError ? (
          <div className="history-error" role="alert">
            <AlertTriangle size={20} />
            <div><strong>Reading history unavailable</strong><span>{historyError}</span></div>
            <button className="button button-secondary" type="button" onClick={() => setHistoryRequestVersion((value) => value + 1)}>Retry</button>
          </div>
        ) : readings.length === 0 && historyPoints.length === 0 ? (
          <div className="history-empty"><Activity size={20} /> No readings received in this range.</div>
        ) : (
          <div className="history-table-wrap">
            <table className="history-table">
              <thead><tr><th>Observed</th><th>Fill</th><th>Distance</th><th>Quality</th><th>Wi-Fi</th><th>Firmware</th></tr></thead>
              <tbody>
                {range === '24h'
                  ? readings.slice().reverse().map((reading) => (
                    <tr key={reading.readingId}>
                      <td>{formatDateTime(reading.timestampUtc)}{!reading.usesObservedTimestamp && <small>Received time</small>}</td>
                      <td><strong>{reading.fillPercent.toFixed(1)}%</strong></td>
                      <td>{reading.rawDistanceMm} mm</td>
                      <td>{reading.quality}%</td>
                      <td>{reading.wifiRssiDbm} dBm</td>
                      <td>{reading.firmwareVersion}</td>
                    </tr>
                  ))
                  : historyPoints.slice().reverse().map((point) => (
                    <tr key={point.bucketStartUtc}>
                      <td>{formatDateTime(point.bucketStartUtc)}<small>{point.readingCount} readings</small></td>
                      <td><strong>{point.fillPercentLatest.toFixed(1)}%</strong><small>{point.fillPercentMin.toFixed(1)}–{point.fillPercentMax.toFixed(1)}%</small></td>
                      <td>{point.rawDistanceMmAverage.toFixed(0)} mm<small>{point.rawDistanceMmMin}–{point.rawDistanceMmMax}</small></td>
                      <td>{point.worstQuality}%<small>Worst</small></td>
                      <td>{point.wifiRssiDbmAverage.toFixed(0)} dBm<small>{point.wifiRssiDbmMin}–{point.wifiRssiDbmMax}</small></td>
                      <td>{point.latestFirmwareVersion}{point.errorCount > 0 && <small>{point.errorCount} errors</small>}</td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <div className="detail-metadata">
        <MetaItem icon={<Gauge size={16} />} label="Calibration" value={detail.calibrationVersion ? `Version ${detail.calibrationVersion} · ${(detail.tankDepthMm ?? 0) / 10} cm depth` : 'Not available'} />
        <MetaItem icon={<KeyRound size={16} />} label="Credential" value={detail.hasActiveCredential ? 'Active' : 'Unavailable'} />
        <MetaItem icon={<UserRound size={16} />} label="Installed by" value={detail.installedBy ?? 'Not recorded'} />
        <MetaItem icon={<Box size={16} />} label="Model" value={device.model} />
        <MetaItem icon={<CalendarClock size={16} />} label="Commissioned" value={detail.commissionedAtUtc ? formatDateTime(detail.commissionedAtUtc) : 'Not recorded'} />
      </div>
    </section>
  );
}

function DetailRow({ label, value }: { label: string; value: string }) {
  return <div className="detail-row"><span>{label}</span><strong>{value}</strong></div>;
}

function MetaItem({ icon, label, value }: { icon: ReactNode; label: string; value: string }) {
  return <div className="meta-item"><span>{icon}</span><div><small>{label}</small><strong>{value}</strong></div></div>;
}

function formatSensorFault(value: FleetDevice['sensorFault']): string {
  if (!value) return 'unknown sensor fault';
  return value.replace(/([A-Z])/g, ' $1').toLowerCase();
}
