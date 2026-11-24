import React from 'react';
import type { QueueDeviceLivenessDto } from '../../services/queues';

export type QueueDeviceStateProps = {
  /** The device state from the health block of the run. Null or undefined when the run has no report. */
  liveness: QueueDeviceLivenessDto | null | undefined;
  /** True when the queue has device recovery with the action reboot-instance. */
  recoveryOn: boolean;
};

const formatSince = (iso: string | null | undefined): string => {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
};

/**
 * The device state line in the edit view of a running queue (feature 199, FR-011). It shows nothing when the run has no
 * report. When the device is not live, it also shows the alert state and the recovery state.
 */
export const QueueDeviceState: React.FC<QueueDeviceStateProps> = ({ liveness, recoveryOn }) => {
  if (!liveness) return null;
  if (liveness.state !== 'not_live') {
    return <div className="queue-device-state" role="status">Device: live</div>;
  }
  const since = formatSince(liveness.notLiveSince);
  const reason = liveness.reason ? ` (${liveness.reason})` : '';
  const recovery = recoveryOn
    ? `Recovery: ${liveness.recoveryState ?? 'idle'}, attempts ${liveness.recoveryAttempts ?? 0}`
    : 'Recovery: off';
  return (
    <div className="queue-device-state" role="status">
      <div>{`Device: not live${reason}${since ? ` since ${since}` : ''}`}</div>
      <div>{liveness.alertSent ? 'Alert sent' : 'No alert yet'}</div>
      <div>{recovery}</div>
    </div>
  );
};
