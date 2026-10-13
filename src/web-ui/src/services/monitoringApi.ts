import { ApiError, apiGet, getJson, postJson } from '../lib/api';

/** The state of a session in the monitoring list (feature 141). */
export type MonitoredSessionState = 'live' | 'no_answer' | 'restarting' | 'stopping';

export type MonitoredSessionDto = {
  id: string;
  name: string;
  gameId: string;
  deviceSerial: string | null;
  state: MonitoredSessionState;
  reason: string | null;
  ownerQueueId: string | null;
  ownerQueueName: string | null;
  restartAvailable: boolean;
  restartUnavailableReason: string | null;
  startedAtUtc: string;
};

export type MonitoredSessionsResponse = {
  sessions: MonitoredSessionDto[];
};

export type RestartState = 'restarting' | 'success' | 'failure';

export type RestartStep =
  | 'starting'
  | 'rebooting'
  | 'waiting_for_device'
  | 'reconnecting'
  | 'checking_capture'
  | 'done';

export type SessionRestartRecordDto = {
  sessionId: string;
  state: RestartState;
  step: RestartStep;
  startedAtUtc: string;
  endedAtUtc: string | null;
  reason: string | null;
};

/** Lists all sessions that the service holds, with a state. */
export const listMonitoredSessions = async (): Promise<MonitoredSessionDto[]> => {
  const data = await getJson<MonitoredSessionsResponse>('/api/sessions/monitoring');
  return Array.isArray(data?.sessions) ? data.sessions : [];
};

/**
 * Gets one screenshot of a session as a PNG blob. The time stamp in the address prevents a cached picture.
 * The signal aborts the request when the page selects another session or closes.
 */
export const fetchSnapshot = async (sessionId: string, signal?: AbortSignal): Promise<Blob> => {
  const res = await apiGet(`/api/sessions/${encodeURIComponent(sessionId)}/snapshot?t=${Date.now()}`, signal);
  if (!res.ok) {
    throw new ApiError(res.status, `The screenshot request failed (HTTP ${res.status}).`);
  }
  return res.blob();
};

/** Starts a restart of the whole emulator of the session. Sends one request. */
export const startRestart = (sessionId: string): Promise<SessionRestartRecordDto> =>
  postJson<SessionRestartRecordDto>(`/api/sessions/${encodeURIComponent(sessionId)}/restart`, {});

/** Reads the last restart record of the session. A session with no restart gives an ApiError with status 404. */
export const getRestart = (sessionId: string): Promise<SessionRestartRecordDto> =>
  getJson<SessionRestartRecordDto>(`/api/sessions/${encodeURIComponent(sessionId)}/restart`);
