import { deleteJson, getJson, postJson } from '../lib/api';

export type QueueTemplateSummary = {
  id: string;
  name: string;
  entryCount: number;
  createdAt: string | null;
  updatedAt: string | null;
};

export type QueueTemplateEntryDto = {
  sequenceId: string;
  sequenceName: string | null;
  stale: boolean;
};

export type QueueTemplateDetail = QueueTemplateSummary & { entries: QueueTemplateEntryDto[] };

export type SaveQueueTemplate = {
  name: string;
  sequenceIds: string[];
  overwrite: boolean;
};

const base = '/api/queue-templates';

export const listQueueTemplates = () => getJson<QueueTemplateSummary[]>(base);
export const getQueueTemplate = (id: string) => getJson<QueueTemplateDetail>(`${base}/${id}`);
export const saveQueueTemplate = (input: SaveQueueTemplate) => postJson<QueueTemplateDetail>(base, input);
export const deleteQueueTemplate = (id: string) => deleteJson<void>(`${base}/${id}`);
