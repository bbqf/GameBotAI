import { buildQueueActivity, buildScheduleRows, describeNow, formatRelative } from '../queueActivity';
import type { QueueDetailDto, QueueMonitorDto, QueueMonitorItemDto } from '../../../services/queues';
import type { QueueTemplateDetail } from '../../../services/queueTemplates';

const NOW = new Date(2026, 9, 13, 10, 0, 0);
const at = (h: number, m: number) => new Date(2026, 9, 13, h, m, 0).toISOString();

const item = (over: Partial<QueueMonitorItemDto>): QueueMonitorItemDto => ({
  sequenceId: 's1', sequenceName: 'Help All', stale: false, scheduleKind: 'TimerTimeOfDay', reason: '',
  expectedAt: null, relativeLabel: null, repeats: false, order: 0, ...over
});

const monitor = (over: Partial<QueueMonitorDto> = {}): QueueMonitorDto => ({
  queueId: 'q1', name: 'Daily', running: true, cycleExecution: false, runStartedAt: null,
  current: null, upcoming: [], nothingScheduled: false, lastOutcome: null, ...over
});

const entry = (id: string, name: string, over: Record<string, unknown> = {}) => ({
  sequenceId: id, sequenceName: name, stale: false, scheduleType: 'OncePerRun' as const,
  timerTimeOfDay: null, timerRelativeOffset: null, enabled: true, ...over
});

const template = (entries: ReturnType<typeof entry>[]): QueueTemplateDetail => ({
  id: 't1', name: 'tpl', entryCount: entries.length, createdAt: null, updatedAt: null, entries
});

describe('formatRelative', () => {
  it('formats future and past times', () => {
    expect(formatRelative(new Date(2026, 9, 13, 10, 5), NOW)).toBe('in 5 min');
    expect(formatRelative(new Date(2026, 9, 13, 12, 10), NOW)).toBe('in 2 h 10 min');
    expect(formatRelative(new Date(2026, 9, 13, 12, 0), NOW)).toBe('in 2 h');
    expect(formatRelative(new Date(2026, 9, 13, 9, 50), NOW)).toBe('10 min ago');
    expect(formatRelative(new Date(2026, 9, 13, 10, 0, 20), NOW)).toBe('in under 1 min');
  });
});

describe('buildScheduleRows', () => {
  const tpl = template([
    entry('s1', 'Alpha'),
    entry('s2', 'Beta', { scheduleType: 'Timer', timerTimeOfDay: '04:00:00' }),
    entry('s3', 'Gamma', { enabled: false }),
    entry('s4', 'Delta', { scheduleType: 'Timer', timerTimeOfDay: '11:00:00' })
  ]);

  it('orders timed rows, then untimed rows, then disabled rows', () => {
    const m = monitor({
      upcoming: [
        item({ sequenceId: 's4', expectedAt: at(11, 0), order: 1 }),
        item({ sequenceId: 's2', expectedAt: at(10, 30), order: 2 })
      ]
    });
    const rows = buildScheduleRows(m, tpl, null, NOW);
    expect(rows.map((r) => r.name)).toEqual(['Beta', 'Delta', 'Alpha', 'Gamma']);
    expect(rows[0].timingText).toContain('10:30');
    expect(rows[0].timingText).toContain('in 30 min');
    expect(rows[3].enabled).toBe(false);
    expect(rows[3].nextAt).toBeNull();
  });

  it('shows no next time for a stopped queue', () => {
    const m = monitor({ running: false, upcoming: [item({ sequenceId: 's2', expectedAt: at(10, 30) })] });
    const rows = buildScheduleRows(m, tpl, null, NOW);
    expect(rows.every((r) => r.nextAt === null)).toBe(true);
    expect(rows.map((r) => r.name)).toEqual(['Alpha', 'Beta', 'Delta', 'Gamma']);
    expect(rows.find((r) => r.name === 'Beta')?.timingText).toBe('Daily at 04:00');
    expect(rows.find((r) => r.name === 'Alpha')?.timingText).toBe('Once each run');
  });

  it('uses the detail entries when the template is missing', () => {
    const detail = {
      entries: [{ entryId: 'e1', sequenceId: 's1', sequenceName: 'Help All', stale: false }, { entryId: 'e2', sequenceId: 's9', sequenceName: null, stale: false }]
    } as QueueDetailDto;
    const rows = buildScheduleRows(null, null, detail, NOW);
    expect(rows.map((r) => r.name)).toEqual(['Help All', 's9']);
    expect(rows[0].timingText).toBe('not scheduled');
  });

  it('uses the relative label when there is no absolute time', () => {
    const m = monitor({ upcoming: [item({ sequenceId: 's1', relativeLabel: 'up next' })] });
    const rows = buildScheduleRows(m, tpl, null, NOW);
    expect(rows.find((r) => r.name === 'Alpha')?.timingText).toBe('up next');
  });
});

describe('describeNow and buildQueueActivity', () => {
  it('describes a running sequence', () => {
    const m = monitor({ current: item({ sequenceName: 'Help All', scheduleKind: 'OncePerRun' }) });
    expect(describeNow(m)).toBe('Running: Help All');
    expect(buildQueueActivity({ status: 'Running' }, m)).toBe('Running: Help All');
  });

  it('describes an idle pause', () => {
    const m = monitor({ current: item({ scheduleKind: 'IdlePause', expectedAt: at(10, 45) }) });
    expect(describeNow(m)).toBe('Idle pause, resumes at 10:45');
  });

  it('describes waiting and the next time', () => {
    const m = monitor({ upcoming: [item({ expectedAt: at(11, 5) })] });
    expect(describeNow(m)).toBe('Waiting until 11:05');
    expect(buildQueueActivity({ status: 'Running' }, m)).toBe('Next run at 11:05');
  });

  it('describes nothing scheduled and a stopped queue', () => {
    expect(describeNow(monitor({ nothingScheduled: true }))).toBe('Nothing scheduled');
    expect(describeNow(monitor({ running: false }))).toBe('Stopped');
    expect(buildQueueActivity({ status: 'Stopped' }, monitor({ running: false }))).toBe('Stopped');
    expect(describeNow(null)).toBe('No data yet');
  });
});

describe('buildQueueActivity (feature 173)', () => {
  const running = { status: 'Running' as const };

  it('gives Stopped for a stopped queue, even with a running monitor', () => {
    expect(buildQueueActivity({ status: 'Stopped' }, monitor())).toBe('Stopped');
    expect(buildQueueActivity({ status: 'Stopped', operatorPause: 'paused' }, monitor())).toBe('Stopped');
  });

  it('gives Paused and Pausing from the pause state', () => {
    expect(buildQueueActivity({ ...running, operatorPause: 'paused' }, monitor())).toBe('Paused');
    expect(buildQueueActivity({ ...running, operatorPause: 'pausing' }, monitor())).toBe('Pausing');
  });

  it('shows the pause before the running sequence', () => {
    const m = monitor({ current: item({ sequenceName: 'Help All', scheduleKind: 'OncePerRun' }) });
    expect(buildQueueActivity({ ...running, operatorPause: 'pausing' }, m)).toBe('Pausing');
    expect(buildQueueActivity({ ...running, operatorPause: 'paused' }, m)).toBe('Paused');
  });

  it('gives the running sequence, with the sequence ID when the name is missing', () => {
    const named = monitor({ current: item({ sequenceName: 'Help All', scheduleKind: 'OncePerRun' }) });
    const unnamed = monitor({ current: item({ sequenceId: 'seq-9', sequenceName: null, scheduleKind: 'OncePerRun' }) });
    expect(buildQueueActivity({ ...running, operatorPause: 'none' }, named)).toBe('Running: Help All');
    expect(buildQueueActivity(running, unnamed)).toBe('Running: seq-9');
  });

  it('does not call an idle pause a running sequence', () => {
    const m = monitor({ current: item({ scheduleKind: 'IdlePause' }), upcoming: [item({ expectedAt: at(11, 5) })] });
    expect(buildQueueActivity(running, m)).toBe('Next run at 11:05');
  });

  it('gives the next run time, the earliest first', () => {
    const m = monitor({ upcoming: [item({ expectedAt: at(12, 0) }), item({ expectedAt: at(11, 5) })] });
    expect(buildQueueActivity(running, m)).toBe('Next run at 11:05');
  });

  it('gives Nothing scheduled when the monitor says so', () => {
    expect(buildQueueActivity(running, monitor({ nothingScheduled: true }))).toBe('Nothing scheduled');
  });

  it('gives Waiting when nothing else matches', () => {
    expect(buildQueueActivity(running, monitor())).toBe('Waiting');
  });

  it('gives the status only when the monitor data is missing', () => {
    expect(buildQueueActivity(running, null)).toBe('Running');
    expect(buildQueueActivity(running, undefined)).toBe('Running');
    expect(buildQueueActivity({ ...running, operatorPause: 'paused' }, null)).toBe('Paused');
  });
});

describe('buildScheduleRows entry states (feature 184)', () => {
  const tpl = template([
    entry('s1', 'Alpha', { entryId: 'e1' }),
    entry('s2', 'Beta', { entryId: 'e2' }),
    entry('s3', 'Gamma', { entryId: 'e3' }),
    entry('s4', 'Delta', { entryId: 'e4', enabled: false }),
    entry('s5', 'Old')
  ]);
  const states = { e1: 'idle', e2: 'queued', e3: 'running', e4: 'idle' } as const;

  it('gives each row the entry ID and the entry state from the monitor', () => {
    const rows = buildScheduleRows(monitor({ entryStates: { ...states } }), tpl, null, NOW);
    const byName = Object.fromEntries(rows.map((r) => [r.name, r]));

    expect(byName.Alpha).toMatchObject({ entryId: 'e1', entryState: 'idle' });
    expect(byName.Beta).toMatchObject({ entryId: 'e2', entryState: 'queued' });
    expect(byName.Gamma).toMatchObject({ entryId: 'e3', entryState: 'running' });
  });

  it('gives the summary text "Queued to run next" for queued and "Running" for running', () => {
    const rows = buildScheduleRows(monitor({ entryStates: { ...states } }), tpl, null, NOW);
    const byName = Object.fromEntries(rows.map((r) => [r.name, r]));

    expect(byName.Beta.timingText).toBe('Queued to run next');
    expect(byName.Gamma.timingText).toBe('Running');
    expect(byName.Alpha.timingText).toBe('Once each run');
  });

  it('starts the summary of a scheduled entry with "Scheduled"', () => {
    const rows = buildScheduleRows(monitor({ entryStates: { e1: 'scheduled' } }), tpl, null, NOW);

    expect(rows.find((r) => r.name === 'Alpha')?.timingText).toBe('Scheduled · Once each run');
  });

  it('gives no state to a disabled row, to a row of an old template with no entry ID, and when the queue does not run', () => {
    const running = buildScheduleRows(monitor({ entryStates: { ...states } }), tpl, null, NOW);
    expect(running.find((r) => r.name === 'Delta')).toMatchObject({ entryId: 'e4', entryState: null });
    expect(running.find((r) => r.name === 'Old')).toMatchObject({ entryId: null, entryState: null });

    const stopped = buildScheduleRows(monitor({ running: false, entryStates: { ...states } }), tpl, null, NOW);
    expect(stopped.every((r) => r.entryState === null)).toBe(true);
  });

  it('gives no state when the service sends no entryStates', () => {
    const rows = buildScheduleRows(monitor(), tpl, null, NOW);

    expect(rows.every((r) => r.entryState === null)).toBe(true);
  });
});

describe('buildScheduleRows smart scheduling (feature 189)', () => {
  const tpl = template([
    entry('s1', 'Alpha', { entryId: 'e1' }),
    entry('s2', 'Beta', { entryId: 'e2' })
  ]);

  it('adds the postponed text for an item with a requested time', () => {
    const m = monitor({
      upcoming: [item({ sequenceId: 's1', scheduleKind: 'SelfReschedule', expectedAt: at(10, 10), requestedAt: at(10, 0), order: 0 })],
      entryStates: { e1: 'scheduled' }
    });
    const rows = buildScheduleRows(m, tpl, null, NOW);
    expect(rows.find((r) => r.name === 'Alpha')?.timingText).toBe('Scheduled · 10:10 (in 10 min) · postponed from 10:00');
  });

  it('keeps the text of today for an item with no requested time', () => {
    const m = monitor({
      upcoming: [item({ sequenceId: 's1', scheduleKind: 'SelfReschedule', expectedAt: at(10, 10), order: 0 })],
      entryStates: { e1: 'scheduled' }
    });
    const rows = buildScheduleRows(m, tpl, null, NOW);
    expect(rows.find((r) => r.name === 'Alpha')?.timingText).toBe('Scheduled · 10:10 (in 10 min)');
  });

  it('ignores a requested time that is not valid', () => {
    const m = monitor({
      upcoming: [item({ sequenceId: 's1', scheduleKind: 'SelfReschedule', expectedAt: at(10, 10), requestedAt: 'bad', order: 0 })]
    });
    const rows = buildScheduleRows(m, tpl, null, NOW);
    expect(rows.find((r) => r.name === 'Alpha')?.timingText).toBe('10:10 (in 10 min)');
  });
});