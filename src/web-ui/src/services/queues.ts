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

/** Result of scheduling a sequence to fire after a relative offset against a running queue. */
export type LiveScheduleResult = {
  sequenceId: string;
  offset: string;
  expectedFireAt: string;
};

/**
 * Schedule a sequence to fire once after a relative offset ("HH:mm:ss") from now against the
 * queue's active run. Ephemeral; not persisted to the template.
 */
export const liveScheduleSequence = (queueId: string, sequenceId: string, offset: string) =>
  postJson<LiveScheduleResult>(`${base}/${queueId}/live-schedule`, { sequenceId, offset });

// ── Live monitor (feature 072) ────────────────────────────────────────────────────────────────

/** Why a monitor item is scheduled, mirroring the run loop's schedule semantics. */
export type ScheduleKind =
  | 'AtQueueStart'
  | 'OncePerRun'
  | 'EveryStep'
  | 'TimerTimeOfDay'
  | 'TimerRelative'
  | 'LiveSchedule'
  | 'SelfReschedule';

/** One now/up-next item in the live monitor plan. */
export type QueueMonitorItemDto = {
  sequenceId: string;
  sequenceName: string | null;
  stale: boolean;
  scheduleKind: ScheduleKind;
  reason: string;
  /** Absolute expected time (ISO-8601 with offset) when known; null for spine steps. */
  expectedAt: string | null;
  /** Hint when there is no absolute time: now / next / up next / waiting / due. */
  relativeLabel: string | null;
  repeats: boolean;
  order: number;
};

/** Best-effort last finalized run outcome (shown when the queue is not running). */
export type RunOutcomeDto = {
  status: string;
  summary: string;
};

/** Read-only snapshot of a queue's live run plan. */
export type QueueMonitorDto = {
  queueId: string;
  name: string;
  running: boolean;
  cycleExecution: boolean;
  runStartedAt: string | null;
  current: QueueMonitorItemDto | null;
  upcoming: QueueMonitorItemDto[];
  nothingScheduled: boolean;
  lastOutcome: RunOutcomeDto | null;
};

/** Fetch the live monitor snapshot for a queue. Safe to poll; no side effects. */
export const getQueueMonitor = (id: string) => getJson<QueueMonitorDto>(`${base}/${id}/monitor`);
