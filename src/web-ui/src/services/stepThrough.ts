import { ApiError, apiDelete, buildApiUrl, buildAuthHeaders, getJson, postJson, putJson } from '../lib/api';

// Types of the step-through API (feature 127, contracts/step-through-api.md).

export type StepThroughRunState = 'idle' | 'running' | 'complete';

export type StepThroughNodeDto = {
  path: string;
  depth: number;
  stepId?: string | null;
  type: string;
  label: string;
  container: boolean;
  selectable: boolean;
  branch?: string | null;
};

export type StepThroughHistoryEntryDto = {
  seq: number;
  path: string;
  stepId?: string | null;
  kind: 'step' | 'enter' | 'exit';
  iteration?: number | null;
  status: string;
  outcome?: string | null;
  message?: string | null;
  effects: string[];
  notes: string[];
  startedAt: string;
  durationMs: number;
  executionLogId?: string | null;
};

export type StepThroughParameterDto = {
  name: string;
  value?: string | null;
  isSet: boolean;
};

export type StepThroughQueueDto = {
  queueId: string;
  queueName: string;
  running: boolean;
  firingActive: boolean;
  pausedByStepThrough: boolean;
  alreadyPaused: boolean;
};

export type StepThroughStateDto = {
  id: string;
  sequenceId: string;
  sequenceName: string;
  gameSessionId: string;
  state: StepThroughRunState;
  cursor?: string | null;
  nodes: StepThroughNodeDto[];
  running?: { path: string; startedAt: string } | null;
  history: StepThroughHistoryEntryDto[];
  parameters: StepThroughParameterDto[];
  outcomes: Record<string, string>;
  queue?: StepThroughQueueDto | null;
  leaseExpiresAt: string;
};

export type StartStepThroughRequest = {
  sequenceId: string;
  gameSessionId: string;
  startPath?: string;
  parameterValues?: Record<string, string>;
};

export type StepThroughValuesRequest = {
  parameterValues?: Record<string, string>;
  /** An empty value makes the outcome "not set". */
  outcomes?: Record<string, string>;
};

/** An error of the step-through API with the code of the contract. */
export class StepThroughError extends Error {
  status: number;
  code: string;
  details?: Record<string, unknown>;

  constructor(status: number, code: string, message: string, details?: Record<string, unknown>) {
    super(message);
    this.status = status;
    this.code = code;
    this.details = details;
  }
}

const base = '/api/step-through';

/** Maps an `ApiError` to a `StepThroughError`. The body of the API has `{ error: { code, message, details } }`. */
const toStepThroughError = (error: unknown): never => {
  if (error instanceof StepThroughError) throw error;
  if (error instanceof ApiError) {
    const body = (error.payload as { error?: { code?: string; message?: string; details?: Record<string, unknown> } } | undefined)?.error;
    throw new StepThroughError(error.status, body?.code ?? 'error', body?.message ?? error.message, body?.details);
  }
  throw new StepThroughError(500, 'error', (error as Error)?.message ?? 'The request failed.');
};

const call = async <T>(action: () => Promise<T>): Promise<T> => {
  try {
    return await action();
  } catch (error) {
    return toStepThroughError(error);
  }
};

const url = (id: string, suffix = '') => `${base}/${encodeURIComponent(id)}${suffix}`;

export const startStepThrough = (request: StartStepThroughRequest) =>
  call(() => postJson<StepThroughStateDto>(base, request));

export const getStepThrough = (id: string, afterSeq?: number) =>
  call(() => getJson<StepThroughStateDto>(`${url(id)}${afterSeq !== undefined ? `?afterSeq=${afterSeq}` : ''}`));

export const runNextStep = (id: string) => call(() => postJson<StepThroughStateDto>(url(id, '/run-next'), {}));

export const selectStep = (id: string, path: string) =>
  call(() => postJson<StepThroughStateDto>(url(id, '/select'), { path }));

export const cancelStep = (id: string) => call(() => postJson<StepThroughStateDto>(url(id, '/cancel'), {}));

export const restartStepThrough = (id: string) => call(() => postJson<StepThroughStateDto>(url(id, '/restart'), {}));

export const setStepThroughValues = (id: string, values: StepThroughValuesRequest) =>
  call(() => putJson<StepThroughStateDto>(url(id, '/values'), values));

export const pauseStepThroughQueue = (id: string) =>
  call(() => postJson<StepThroughStateDto>(url(id, '/pause-queue'), {}));

export const endStepThrough = async (id: string): Promise<void> => {
  const response = await apiDelete(url(id));
  // 204 ends it. 404 means it ended before (the lease expired). Both are fine.
  if (!response.ok && response.status !== 404) {
    throw new StepThroughError(response.status, 'error', `The step-through could not end (HTTP ${response.status}).`);
  }
};

/**
 * Ends the step-through when the page closes. A `keepalive` request survives the unload of the page.
 * `navigator.sendBeacon` cannot be used: it sends only POST and it cannot send the token header.
 * If the request does not arrive, the lease of 90 seconds ends the step-through.
 */
export const endStepThroughOnClose = (id: string): void => {
  try {
    void fetch(buildApiUrl(url(id)), { method: 'DELETE', headers: buildAuthHeaders(false), keepalive: true }).catch(() => undefined);
  } catch {
    // The lease ends the step-through.
  }
};
