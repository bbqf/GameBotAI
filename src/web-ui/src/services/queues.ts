import { deleteJson, getJson, postJson, putJson } from '../lib/api';

export type QueueStatus = 'Stopped' | 'Running';

/** How much a queue tells the operator (feature 120). */
export type NotificationLevel = 'none' | 'failure' | 'successAndFailure';

export type QueueDto = {
  id: string;
  name: string;
  emulatorSerial: string;
  cycleExecution: boolean;
  /** When true, the game is backed out during idle gaps over the threshold (feature 073). */
  pauseWhenIdle: boolean;
  /** Idle-detection threshold in seconds (default 30). */
  idleThresholdSeconds: number;
  /** Optional LDPlayer instance name cold-started before session creation (feature 074); null when unset. */
  emulatorInstanceName?: string | null;
  /** Optional LDPlayer instance index for the pre-session cold-start (feature 074); null when unset. */
  emulatorInstanceIndex?: number | null;
  /** When true, the queue is started again after a service restart if it was running (feature 098). */
  resumeOnServiceStart?: boolean;
  /** Notification level of the queue (feature 120). The API sends 'none' when the queue has no level. */
  notificationLevel?: NotificationLevel;
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

/** State of the operator pause (feature 171). 'pausing' means a sequence still runs. */
export type OperatorPauseState = 'none' | 'pausing' | 'paused';

/** The part of the health block of a running queue that the pause controls read. */
export type QueueHealthDto = {
  paused: boolean;
  pausedAt?: string | null;
  pauseKind?: 'idle' | 'failurePolicy' | 'operator' | null;
  pauseReason?: string | null;
  operatorPause?: OperatorPauseState;
};

export type QueueDetailDto = QueueDto & {
  entries: QueueEntryDto[];
  linkedTemplateName: string | null;
  /** Live health of the run; null or absent when the queue is not running. */
  health?: QueueHealthDto | null;
  // linkedGameId and linkedGameName are inherited from QueueDto
};

export type QueueCreate = {
  name: string;
  emulatorSerial: string;
  cycleExecution: boolean;
  pauseWhenIdle?: boolean;
  idleThresholdSeconds?: number;
  emulatorInstanceName?: string | null;
  emulatorInstanceIndex?: number | null;
  resumeOnServiceStart?: boolean;
};

export type QueueUpdate = {
  name: string;
  cycleExecution: boolean;
  pauseWhenIdle?: boolean;
  idleThresholdSeconds?: number;
  emulatorInstanceName?: string | null;
  emulatorInstanceIndex?: number | null;
  resumeOnServiceStart?: boolean;
};

const base = '/api/queues';

export const listQueues = () => getJson<QueueDto[]>(base);
export const getQueue = (id: string) => getJson<QueueDetailDto>(`${base}/${id}`);
/**
 * Reads the health block of one queue (feature 171): the live state of its run, with the operator pause. Null when
 * the queue does not run. It reads the same endpoint as <see cref="getQueue"/>.
 */
export const getQueueHealth = async (id: string): Promise<QueueHealthDto | null> =>
  (await getJson<QueueDetailDto>(`${base}/${id}`)).health ?? null;
export const createQueue = (input: QueueCreate) => postJson<QueueDto>(base, input);
export const updateQueue = (id: string, input: QueueUpdate) => putJson<QueueDto>(`${base}/${id}`, input);
export const deleteQueue = (id: string) => deleteJson<void>(`${base}/${id}`);
/** Sets the notification level of one queue (feature 120). The level applies to the next finished sequence. */
export const setQueueNotificationLevel = (id: string, level: NotificationLevel) =>
  putJson<QueueDto>(`${base}/${id}/notification-level`, { level });

export type QueueDuplicate = {
  name: string;
  emulatorSerial: string;
  emulatorInstanceName?: string | null;
  emulatorInstanceIndex?: number | null;
};

/**
 * Creates a 1:1 copy of the queue under a new name (feature 083). Name must differ from the
 * source's. The emulator fields are the one exception to "1:1": the caller resubmits them
 * (typically pre-filled from the source) and may point the duplicate at a different
 * emulator/instance — but unlike the name, they are allowed to come back unchanged.
 */
export const duplicateQueue = (id: string, input: QueueDuplicate) =>
  postJson<QueueDto>(`${base}/${id}/duplicate`, input);

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

/** Result of asking a running queue to pause between sequences (feature 171). */
export type QueuePauseResult = {
  id: string;
  operatorPause: OperatorPauseState;
  requestedAt?: string | null;
};

/** Result of a resume call. `kind` says which pause the call ended; absent when nothing changed. */
export type QueueResumeResult = {
  id: string;
  status: QueueStatus;
  resumed: boolean;
  kind?: 'operator' | 'failurePolicy';
};

// ── Apply template changes to a running queue (feature 171) ────────────────────────────────────

/** The type of one difference between a running queue and its template. */
export type TemplateDifferenceType = 'added' | 'removed' | 'enabled' | 'disabled' | 'parameters' | 'schedule' | 'order';

/** One difference between the entries of a running queue and the entries of its template. */
export type TemplateDifferenceDto = {
  /** `<type>:<entryId>`. Send it back in `selectedKeys` to apply the difference. */
  key: string;
  type: TemplateDifferenceType;
  entryId: string;
  sequenceName: string;
  /** One sentence that says what the apply does. */
  summary: string;
  /** False for a schedule change: a schedule changes only on a direct instruction. */
  selectedByDefault: boolean;
  /** True when the difference starts or moves the schedule of the entry. */
  affectsSchedule: boolean;
};

export type QueueTemplateDiffDto = {
  queueId: string;
  templateId: string;
  /** True when the template or the run has entries with no entry ID. The list is empty then. */
  needsUpgrade: boolean;
  /** Send it back with the apply call. A change of the template changes it. */
  baseToken: string;
  differences: TemplateDifferenceDto[];
};

export type ApplyTemplateResult = {
  applied: string[];
  planVersion: number;
};

/** Reads the differences between a running queue and its linked template. It changes nothing. */
export const getTemplateDiff = (id: string) => getJson<QueueTemplateDiffDto>(`${base}/${id}/template-diff`);

/**
 * Applies the selected differences to a paused queue. All of them go in, or none. The call is refused with 409 when
 * the queue is not paused (`not_paused`), when the template changed after the read (`template_changed`), when the
 * template needs an upgrade (`needs_upgrade`), or when another apply runs (`apply_in_progress`).
 */
export const applyTemplate = (id: string, baseToken: string, selectedKeys: string[]) =>
  postJson<ApplyTemplateResult>(`${base}/${id}/apply-template`, { baseToken, selectedKeys });

/** The error code of a refused call (for example `not_paused`), or null. */
export const apiErrorCode = (err: unknown): string | null => {
  const code = (err as { payload?: { error?: { code?: unknown } } } | null)?.payload?.error?.code;
  return typeof code === 'string' ? code : null;
};

/** The hint of a refused call (what to do next), or null. */
export const apiErrorHint = (err: unknown): string | null => {
  const hint = (err as { payload?: { error?: { hint?: unknown } } } | null)?.payload?.error?.hint;
  return typeof hint === 'string' && hint.length > 0 ? hint : null;
};

/**
 * Asks a running queue to pause between sequences (feature 171). The running sequence finishes first. A repeat
 * call is safe.
 */
export const pauseQueue = (id: string) => postJson<QueuePauseResult>(`${base}/${id}/pause`, {});

/** Ends the operator pause, or cancels its request while the state is 'pausing'. The run goes on. */
export const resumeQueue = (id: string) => postJson<QueueResumeResult>(`${base}/${id}/resume`, {});

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
  | 'BeforeEachRun'
  | 'TimerTimeOfDay'
  | 'TimerRelative'
  | 'LiveSchedule'
  | 'SelfReschedule'
  | 'IdlePause';

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
  /** Emulator this run exclusively holds (feature 079); null when the queue is not running. */
  deviceSerial?: string | null;
  current: QueueMonitorItemDto | null;
  upcoming: QueueMonitorItemDto[];
  nothingScheduled: boolean;
  lastOutcome: RunOutcomeDto | null;
};

/** Fetch the live monitor snapshot for a queue. Safe to poll; no side effects. */
export const getQueueMonitor = (id: string) => getJson<QueueMonitorDto>(`${base}/${id}/monitor`);
