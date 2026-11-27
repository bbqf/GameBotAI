import type { ChipState } from '../ui/StatusChip';
import type { MonitoredSessionState } from '../../services/monitoringApi';

const chipTable: Record<MonitoredSessionState, { state: ChipState; text: string }> = {
  live: { state: 'done', text: 'Live' },
  no_answer: { state: 'failed', text: 'No answer' },
  restarting: { state: 'paused', text: 'Restarting' },
  stopping: { state: 'stopped', text: 'Stopping' }
};

/** The chip state and text for a session state. An unknown state shows its own value. */
export const sessionChip = (state: string): { state: ChipState; text: string } =>
  chipTable[state as MonitoredSessionState] ?? { state: 'unknown', text: state };

export type DeviceKind = 'live' | 'not_live' | 'none';

/** The device state of the owner queue as a simple kind. */
export const deviceKind = (liveness: 'live' | 'not_live' | null | undefined): DeviceKind =>
  liveness === 'live' || liveness === 'not_live' ? liveness : 'none';

/** The start time as a short local date and time. A value that is not a date gives "—". */
export const startedText = (iso: string | null | undefined, locale?: string): string => {
  const date = new Date(iso ?? '');
  if (Number.isNaN(date.getTime())) return '—';
  return new Intl.DateTimeFormat(locale, { dateStyle: 'short', timeStyle: 'short' }).format(date);
};
