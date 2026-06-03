import { deleteJson, getJson, postJson, putJson } from '../lib/api';

export type QueueStatus = 'Stopped' | 'Running';

export type QueueDto = {
  id: string;
  name: string;
  emulatorSerial: string;
  cycleExecution: boolean;
  status: QueueStatus;
  entryCount: number;
  linkedTemplateId: string | null;
  linkedGameId: string | null;
  linkedGameName: string | null;
};

export type QueueEntryDto = {
  entryId: string;
  sequenceId: string;
  sequenceName: string | null;
  stale: boolean;
};

export type QueueDetailDto = QueueDto & {
  entries: QueueEntryDto[];
  linkedTemplateName: string | null;
  // linkedGameId and linkedGameName are inherited from QueueDto
};

export type QueueCreate = {
  name: string;
  emulatorSerial: string;
  cycleExecution: boolean;
};

export type QueueUpdate = {
  name: string;
  cycleExecution: boolean;
};

const base = '/api/queues';

export const listQueues = () => getJson<QueueDto[]>(base);
export const getQueue = (id: string) => getJson<QueueDetailDto>(`${base}/${id}`);
export const createQueue = (input: QueueCreate) => postJson<QueueDto>(base, input);
export const updateQueue = (id: string, input: QueueUpdate) => putJson<QueueDto>(`${base}/${id}`, input);
export const deleteQueue = (id: string) => deleteJson<void>(`${base}/${id}`);

export const addQueueEntry = (id: string, sequenceId: string) =>
  postJson<QueueEntryDto>(`${base}/${id}/entries`, { sequenceId });
export const removeQueueEntry = (id: string, entryId: string) =>
  deleteJson<void>(`${base}/${id}/entries/${entryId}`);
export const replaceQueueEntries = (id: string, sequenceIds: string[]) =>
  putJson<QueueDetailDto>(`${base}/${id}/entries`, { sequenceIds });

export const setQueueTemplateLink = (id: string, templateId: string | null) =>
  putJson<QueueDetailDto>(`${base}/${id}/template`, { templateId });

export const setQueueGameLink = (id: string, gameId: string | null) =>
  putJson<QueueDetailDto>(`${base}/${id}/game`, { gameId });

export const startQueue = (id: string) => postJson<QueueDto>(`${base}/${id}/start`, {});
export const stopQueue = (id: string) => postJson<QueueDto>(`${base}/${id}/stop`, {});
