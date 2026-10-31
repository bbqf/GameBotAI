import type { QueueDetailDto, QueueDto, QueueMonitorDto, QueueMonitorItemDto } from '../../services/queues';
import type { QueueTemplateDetail, QueueTemplateEntryDto } from '../../services/queueTemplates';

/** One scheduled entry of a queue, ready to show (data-model.md, ScheduleRow). */
export type ScheduleRow = {
  key: string;
  /** Position of the entry in the template (used by the enable switch). */
  index: number;
  name: string;
  enabled: boolean;
  nextAt: string | null;
  timingText: string;
  repeats: boolean;
};

const pad = (n: number): string => String(n).padStart(2, '0');

/** Clock time in the local zone, for example "04:05". */
export const formatClock = (date: Date): string => `${pad(date.getHours())}:${pad(date.getMinutes())}`;

/** Short relative text, for example "in 5 min" or "2 h 10 min ago". `now` is an argument so tests stay stable. */
export const formatRelative = (date: Date, now: Date): string => {
  const diffMs = date.getTime() - now.getTime();
  const future = diffMs >= 0;
  const totalMin = Math.floor(Math.abs(diffMs) / 60000);
  if (totalMin < 1) return future ? 'in under 1 min' : 'under 1 min ago';
  const hours = Math.floor(totalMin / 60);
  const minutes = totalMin % 60;
  const span = hours > 0 ? (minutes > 0 ? `${hours} h ${minutes} min` : `${hours} h`) : `${minutes} min`;
  return future ? `in ${span}` : `${span} ago`;
};

const timeText = (iso: string, now: Date): string => {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return 'time not known';
  return `${formatClock(date)} (${formatRelative(date, now)})`;
};

const scheduleText = (entry: QueueTemplateEntryDto): string => {
  switch (entry.scheduleType) {
    case 'Timer':
      if (entry.timerTimeOfDay) return `Daily at ${entry.timerTimeOfDay.slice(0, 5)}`;
      if (entry.timerRelativeOffset) return `Every ${entry.timerRelativeOffset}`;
      return 'not scheduled';
    case 'OncePerRun': return 'Once each run';
    case 'EveryStep': return 'Every step';
    case 'AtQueueStart': return 'At queue start';
    case 'BeforeEachRun': return 'Before each run';
    default: return 'not scheduled';
  }
};

const monitorItems = (monitor: QueueMonitorDto | null | undefined): QueueMonitorItemDto[] => {
  if (!monitor || !monitor.running) return [];
  const items = [...(monitor.current ? [monitor.current] : []), ...(monitor.upcoming ?? [])];
  return items.sort((a, b) => a.order - b.order);
};

type BaseEntry = { sequenceId: string; name: string; enabled: boolean; schedule: string };

const baseEntries = (template: QueueTemplateDetail | null | undefined, detail: QueueDetailDto | null | undefined): BaseEntry[] => {
  if (template) {
    return template.entries.map((e) => ({
      sequenceId: e.sequenceId,
      name: e.sequenceName ?? e.sequenceId ?? 'Unknown sequence',
      enabled: e.enabled ?? true,
      schedule: scheduleText(e)
    }));
  }
  return (detail?.entries ?? []).map((e) => ({
    sequenceId: e.sequenceId,
    name: e.sequenceName ?? e.sequenceId ?? 'Unknown sequence',
    enabled: true,
    schedule: 'not scheduled'
  }));
};

/**
 * Build the ordered schedule list: enabled rows with a next time (by time), then enabled rows
 * without a time (template order), then disabled rows (template order).
 */
export const buildScheduleRows = (
  monitor: QueueMonitorDto | null | undefined,
  template: QueueTemplateDetail | null | undefined,
  detail: QueueDetailDto | null | undefined,
  now: Date = new Date()
): ScheduleRow[] => {
  const entries = baseEntries(template, detail);
  const pending = monitorItems(monitor);

  const rows = entries.map((entry, index) => {
    let nextAt: string | null = null;
    let timingText = entry.schedule;
    let repeats = false;
    if (entry.enabled) {
      const at = pending.findIndex((item) => item.sequenceId === entry.sequenceId);
      if (at >= 0) {
        const [item] = pending.splice(at, 1);
        repeats = item.repeats;
        if (item.expectedAt && !Number.isNaN(new Date(item.expectedAt).getTime())) {
          nextAt = item.expectedAt;
          timingText = timeText(item.expectedAt, now);
        } else if (item.relativeLabel) {
          timingText = item.relativeLabel;
        }
      }
    }
    const row: ScheduleRow = {
      key: `${entry.sequenceId}-${index}`,
      index,
      name: entry.name,
      enabled: entry.enabled,
      nextAt,
      timingText,
      repeats
    };
    return { row, index };
  });

  const group = (r: { row: ScheduleRow }) => (!r.row.enabled ? 2 : r.row.nextAt ? 0 : 1);
  return rows
    .sort((a, b) => {
      const ga = group(a);
      const gb = group(b);
      if (ga !== gb) return ga - gb;
      if (ga === 0) return new Date(a.row.nextAt as string).getTime() - new Date(b.row.nextAt as string).getTime() || a.index - b.index;
      return a.index - b.index;
    })
    .map((r) => r.row);
};

const nextUpcomingTime = (monitor: QueueMonitorDto | null | undefined): string | null => {
  if (!monitor || !monitor.running) return null;
  const times = (monitor.upcoming ?? [])
    .map((i) => i.expectedAt)
    .filter((t): t is string => !!t && !Number.isNaN(new Date(t).getTime()))
    .sort((a, b) => new Date(a).getTime() - new Date(b).getTime());
  return times[0] ?? null;
};

/**
 * The activity text of a queue. The Queues list and the phone card both use it, so they show the same text.
 * The first rule that fits wins: stopped, paused, pausing, a running sequence, the next time, nothing scheduled, waiting.
 */
export const buildQueueActivity = (
  queue: Pick<QueueDto, 'status' | 'operatorPause'>,
  monitor: QueueMonitorDto | null | undefined
): string => {
  if (queue.status !== 'Running') return 'Stopped';
  if (queue.operatorPause === 'paused') return 'Paused';
  if (queue.operatorPause === 'pausing') return 'Pausing';
  // No monitor data (not loaded yet, or the call failed): the status is all that is known.
  if (!monitor) return 'Running';
  const current = monitor.current;
  if (current && current.scheduleKind !== 'IdlePause') {
    return `Running: ${current.sequenceName ?? current.sequenceId}`;
  }
  const next = nextUpcomingTime(monitor);
  if (next) return `Next run at ${formatClock(new Date(next))}`;
  if (monitor?.nothingScheduled) return 'Nothing scheduled';
  return 'Waiting';
};

/** The "Now" text of the queue detail. */
export const describeNow = (monitor: QueueMonitorDto | null | undefined): string => {
  if (!monitor) return 'No data yet';
  if (!monitor.running) return 'Stopped';
  const current = monitor.current;
  if (current) {
    if (current.scheduleKind === 'IdlePause') {
      return current.expectedAt && !Number.isNaN(new Date(current.expectedAt).getTime())
        ? `Idle pause, resumes at ${formatClock(new Date(current.expectedAt))}`
        : 'Idle pause';
    }
    return `Running: ${current.sequenceName ?? current.sequenceId}`;
  }
  const next = nextUpcomingTime(monitor);
  if (next) return `Waiting until ${formatClock(new Date(next))}`;
  if (monitor.nothingScheduled) return 'Nothing scheduled';
  return 'Waiting';
};
