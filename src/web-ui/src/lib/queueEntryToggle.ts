import type { QueueTemplateDetail, SaveQueueTemplate, TemplateEntrySaveDto } from '../services/queueTemplates';

const toSaveEntry = (entry: QueueTemplateDetail['entries'][number], enabled: boolean): TemplateEntrySaveDto => {
  const scheduleType = entry.scheduleType ?? 'OncePerRun';
  const isRelative = scheduleType === 'Timer' && !!entry.timerRelativeOffset;
  const bound = (entry.parameterValues ?? []).filter((v) => v.value !== null && v.value !== undefined);
  return {
    ...(entry.entryId ? { entryId: entry.entryId } : {}),
    sequenceId: entry.sequenceId,
    scheduleType,
    ...(scheduleType === 'Timer' && !isRelative && entry.timerTimeOfDay ? { timerTimeOfDay: entry.timerTimeOfDay } : {}),
    ...(isRelative ? { timerRelativeOffset: entry.timerRelativeOffset as string } : {}),
    enabled,
    ...(bound.length > 0 ? { parameterValues: bound } : {})
  };
};

/**
 * Builds the save request that flips `enabled` of one template entry (research R-05).
 * Every entry is sent in the same order. Only one `enabled` value changes.
 */
export const toggleTemplateEntry = (template: QueueTemplateDetail, index: number): SaveQueueTemplate => ({
  name: template.name,
  overwrite: true,
  entries: template.entries.map((entry, i) =>
    toSaveEntry(entry, i === index ? !(entry.enabled ?? true) : (entry.enabled ?? true)))
});
