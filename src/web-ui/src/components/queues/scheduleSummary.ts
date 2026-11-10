/** The fields of an entry that decide its schedule text. A template entry and an editor card both fit. */
export type ScheduleSummaryInput = {
  scheduleType?: string | null;
  timerTimeOfDay?: string | null;
  timerRelativeOffset?: string | null;
  /** Set by the editor. When it is set, it decides which Timer field the text reads. */
  timerMode?: 'timeOfDay' | 'relative' | null;
};

const NOT_SCHEDULED = 'not scheduled';

const timerText = (entry: ScheduleSummaryInput): string => {
  const time = entry.timerTimeOfDay ? `Daily at ${entry.timerTimeOfDay.slice(0, 5)}` : null;
  const offset = entry.timerRelativeOffset ? `Every ${entry.timerRelativeOffset}` : null;
  if (entry.timerMode === 'relative') return offset ?? NOT_SCHEDULED;
  if (entry.timerMode === 'timeOfDay') return time ?? NOT_SCHEDULED;
  // A template entry has no mode: the time comes first, then the offset.
  return time ?? offset ?? NOT_SCHEDULED;
};

/** One short text for the schedule of an entry. The card summary and the phone rows both use it (FR-004). */
export const scheduleSummary = (entry: ScheduleSummaryInput): string => {
  switch (entry.scheduleType) {
    case 'Timer': return timerText(entry);
    case 'OncePerRun': return 'Once each run';
    case 'EveryStep': return 'Every step';
    case 'AtQueueStart': return 'At queue start';
    case 'BeforeEachRun': return 'Before each run';
    default: return NOT_SCHEDULED;
  }
};
