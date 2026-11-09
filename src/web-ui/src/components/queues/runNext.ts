import type { EntryState } from '../../services/queues';

/**
 * What a row of the template editor needs for Run next (feature 184). The editor of a running queue passes it. The
 * editor of a stopped queue passes `queueRunning: false`, so each row shows Run next off with the reason.
 */
export type RunNextSupport = {
  /** True when the queue runs. A stopped queue shows Run next off. */
  queueRunning: boolean;
  /** The state of each enabled entry that has an ID, from the monitor. */
  states?: Record<string, EntryState>;
  /** True while a call runs, so the operator cannot press a second time. */
  busy?: boolean;
  /** True when the connection is lost. No call is sent. */
  lost?: boolean;
  /** Sends the call for the entry. */
  onRunNext?: (entryId: string) => void;
};

/** How one row shows Run next. */
export type RunNextView = {
  /** True when the button is on. */
  enabled: boolean;
  /** The tooltip of the button. It always gives the reason when the button is off. */
  tooltip: string;
  /** Short text on the row for an off button, or null. A touch screen has no tooltip, so the reason is also text. */
  note: string | null;
};

export const RUN_NEXT_STOPPED_TEXT = 'Start the queue to use Run next';

/** Gives the view of Run next for one row. `entryId` is the row ID of the editor (`new-<n>` and `row-<n>` are not saved entries). */
export const describeRunNext = (support: RunNextSupport, entryId: string, entryEnabled: boolean): RunNextView => {
  if (!support.queueRunning) return { enabled: false, tooltip: RUN_NEXT_STOPPED_TEXT, note: null };
  if (entryId.startsWith('new-')) return { enabled: false, tooltip: 'Save the template first', note: 'Save the template first' };
  if (entryId.startsWith('row-')) {
    return { enabled: false, tooltip: 'Upgrade the template to use Run next', note: 'Upgrade the template to use Run next' };
  }
  if (!entryEnabled) return { enabled: false, tooltip: 'The entry is disabled', note: null };

  const state = support.states?.[entryId];
  switch (state) {
    case 'running':
      return { enabled: false, tooltip: 'Running', note: 'Running' };
    case 'queued':
      return { enabled: false, tooltip: 'Queued to run next', note: 'Queued to run next' };
    case 'scheduled':
      return { enabled: false, tooltip: 'Scheduled. Use the timer to change it.', note: 'Scheduled' };
    case 'idle':
      if (support.lost) return { enabled: false, tooltip: 'Connection lost', note: null };
      if (support.busy || !support.onRunNext) return { enabled: false, tooltip: 'Run next', note: null };
      return { enabled: true, tooltip: 'Run next', note: null };
    default:
      return { enabled: false, tooltip: 'Apply the template changes first', note: 'Apply the template changes first' };
  }
};
