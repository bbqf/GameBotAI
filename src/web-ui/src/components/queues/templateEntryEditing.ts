import type { QueueTemplateEntryDto, ScheduleType, TemplateEntrySaveDto } from '../../services/queueTemplates';
import type { EntrySchedule } from './QueueEntryList';

/** The schedule of a new row: once each run, no timer. */
export const defaultEntrySchedule = (): EntrySchedule => ({ scheduleType: 'OncePerRun' as ScheduleType, timerTimeOfDay: '' });

/**
 * The editable schedule of one template entry. The queue page and the editor of a running queue both use it, so a
 * reopened queue shows every saved value and a save does not erase one.
 */
export const scheduleFromTemplateEntry = (entry: QueueTemplateEntryDto): EntrySchedule => ({
  ...(entry.entryId ? { templateEntryId: entry.entryId } : {}),
  scheduleType: entry.scheduleType ?? 'OncePerRun',
  timerTimeOfDay: entry.timerTimeOfDay ?? '',
  timerMode: entry.timerRelativeOffset ? 'relative' : 'timeOfDay',
  timerRelativeOffset: entry.timerRelativeOffset ?? '',
  enabled: entry.enabled ?? true,
  // Feature 078: restore the supplied values and their effective-value preview, or the next save would erase them.
  parameterValues: entry.parameterValues ?? undefined,
  effectiveParameters: entry.effectiveParameters ?? undefined,
});

/** One row of the editor: the sequence and its working schedule, in the order of the list. */
export type TemplateEditRow = { sequenceId: string; schedule?: EntrySchedule };

/** The entries of a template save, in the order of the rows. */
export const buildTemplateSaveEntries = (rows: TemplateEditRow[]): TemplateEntrySaveDto[] =>
  rows.map(({ sequenceId, schedule }) => {
    const sched = schedule ?? defaultEntrySchedule();
    const timerMode = sched.timerMode ?? (sched.timerRelativeOffset ? 'relative' : 'timeOfDay');
    const isRelative = sched.scheduleType === 'Timer' && timerMode === 'relative';
    const bound = (sched.parameterValues ?? []).filter((v) => v.value !== null && v.value !== undefined);
    return {
      // Feature 171: send back the ID of the template entry, so that the save keeps its identity.
      ...(sched.templateEntryId ? { entryId: sched.templateEntryId } : {}),
      sequenceId,
      scheduleType: sched.scheduleType,
      ...(sched.scheduleType === 'Timer' && !isRelative && sched.timerTimeOfDay ? { timerTimeOfDay: sched.timerTimeOfDay } : {}),
      ...(isRelative && sched.timerRelativeOffset ? { timerRelativeOffset: sched.timerRelativeOffset } : {}),
      // Only emit when disabled. An absent flag means enabled.
      ...(sched.enabled === false ? { enabled: false } : {}),
      // Feature 078: only rows that bind something. A row left on "inherit" carries no value.
      ...(bound.length > 0 ? { parameterValues: bound } : {}),
    };
  });
