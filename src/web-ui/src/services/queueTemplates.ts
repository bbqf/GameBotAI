import { deleteJson, getJson, postJson } from '../lib/api';

export type QueueTemplateSummary = {
  id: string;
  name: string;
  entryCount: number;
  createdAt: string | null;
  updatedAt: string | null;
};

export type ScheduleType = 'OncePerRun' | 'EveryStep' | 'Timer';

export type QueueTemplateEntryDto = {
  sequenceId: string;
  sequenceName: string | null;
  stale: boolean;
  scheduleType: ScheduleType;
  timerTimeOfDay: string | null;
  /** Relative-mode offset ("HH:mm:ss") for a Timer entry; null in time-of-day mode. */
  timerRelativeOffset: string | null;
};

export type QueueTemplateDetail = QueueTemplateSummary & { entries: QueueTemplateEntryDto[] };

/** Per-entry payload for saving a template. */
export type TemplateEntrySaveDto = {
  sequenceId: string;
  scheduleType?: ScheduleType;
  timerTimeOfDay?: string;
  /** Relative-mode offset ("HH:mm:ss"); mutually exclusive with timerTimeOfDay. */
  timerRelativeOffset?: string;
};

export type SaveQueueTemplate = {
  name: string;
  entries: TemplateEntrySaveDto[];
  overwrite: boolean;
};

const base = '/api/queue-templates';

export const listQueueTemplates = () => getJson<QueueTemplateSummary[]>(base);
export const getQueueTemplate = (id: string) => getJson<QueueTemplateDetail>(`${base}/${id}`);
export const saveQueueTemplate = (input: SaveQueueTemplate) => postJson<QueueTemplateDetail>(base, input);
export const deleteQueueTemplate = (id: string) => deleteJson<void>(`${base}/${id}`);
