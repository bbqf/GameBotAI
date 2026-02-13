import { ApiError, ApiValidationError, buildApiUrl, buildAuthHeaders } from '../lib/api';

export type SessionCreateRequest = {
  gameId: string;
  adbSerial: string;
};

export type SessionCreateResponse = {
  id: string;
  sessionId?: string;
  status?: string;
  gameId?: string;
};

export type RunningSessionDto = {
  sessionId: string;
  gameId: string;
  emulatorId: string;
  startedAtUtc: string;
  lastHeartbeatUtc: string;
  status: 'Running' | 'Stopping' | 'running' | 'stopping';
};

export type StartSessionRequest = {
  gameId: string;
  emulatorId: string;
  options?: Record<string, unknown>;
};

export type StartSessionResponse = {
  sessionId: string;
  runningSessions: RunningSessionDto[];
};

export type StopSessionResponse = {
  stopped: boolean;
};

export type RunningSessionsResponse = {
  sessions: RunningSessionDto[];
};

const parseJsonSafe = async <T>(res: Response): Promise<T | undefined> => {
  try {
    return (await res.json()) as T;
  } catch {
    return undefined;
  }
};

export const createSession = async (payload: SessionCreateRequest, timeoutMs = 30000): Promise<SessionCreateResponse> => {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), Math.max(0, timeoutMs));
  try {
    const res = await fetch(buildApiUrl('/api/sessions'), {
      method: 'POST',
      headers: buildAuthHeaders(true),
      body: JSON.stringify(payload),
      signal: controller.signal
    });

    if (res.ok) {
      const data = await parseJsonSafe<SessionCreateResponse>(res);
      return data as SessionCreateResponse;
    }

    const data = await parseJsonSafe<any>(res);
    if (controller.signal.aborted || res.status === 504) {
      throw new ApiError(504, 'Session creation timed out', undefined, data);
    }
    if (res.status === 400) {
      const errors: ApiValidationError[] | undefined = Array.isArray(data?.errors)
        ? data.errors.map((e: any) => ({ field: e.field, message: e.message ?? String(e) }))
        : undefined;
      throw new ApiError(400, 'Validation error', errors, data);
    }
    const message = (data?.error && (data.error.message ?? data.error.code)) || data?.message || `HTTP ${res.status}`;
    throw new ApiError(res.status, message, undefined, data);
  } catch (err: any) {
    if (err?.name === 'AbortError') {
      throw new ApiError(504, 'Session creation timed out');
    }
    if (err instanceof ApiError) throw err;
    throw new ApiError(500, err?.message ?? 'Failed to create session');
  } finally {
    clearTimeout(timer);
  }
};

export const getRunningSessions = async (timeoutMs = 10000): Promise<RunningSessionDto[]> => {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), Math.max(0, timeoutMs));
  try {
    const res = await fetch(buildApiUrl('/api/sessions/running'), {
      method: 'GET',
      headers: buildAuthHeaders(true),
      signal: controller.signal
    });

    const data = await parseJsonSafe<RunningSessionsResponse>(res);
    if (res.ok && data?.sessions) return data.sessions;

    const message = (data as any)?.message || (data as any)?.error?.message || `HTTP ${res.status}`;
    throw new ApiError(res.status, message, undefined, data);
  } catch (err: any) {
    if (err?.name === 'AbortError') throw new ApiError(504, 'Running sessions request timed out');
    if (err instanceof ApiError) throw err;
    throw new ApiError(500, err?.message ?? 'Failed to load running sessions');
  } finally {
    clearTimeout(timer);
  }
};

export const startSession = async (payload: StartSessionRequest, timeoutMs = 30000): Promise<StartSessionResponse> => {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), Math.max(0, timeoutMs));
  try {
    const res = await fetch(buildApiUrl('/api/sessions/start'), {
      method: 'POST',
      headers: buildAuthHeaders(true),
      body: JSON.stringify(payload),
      signal: controller.signal
    });

    const data = await parseJsonSafe<StartSessionResponse>(res);
    if (res.ok && data) return data;

    if (controller.signal.aborted || res.status === 504) throw new ApiError(504, 'Session creation timed out', undefined, data);
    if (res.status === 400) throw new ApiError(400, 'Validation error', undefined, data);
    if (res.status === 404) throw new ApiError(404, (data as any)?.error?.message || 'Session could not start', undefined, data);
    if (res.status === 429) throw new ApiError(429, 'Session capacity exceeded', undefined, data);

    const message = (data as any)?.error?.message || (data as any)?.message || `HTTP ${res.status}`;
    throw new ApiError(res.status, message, undefined, data);
  } catch (err: any) {
    if (err?.name === 'AbortError') throw new ApiError(504, 'Session creation timed out');
    if (err instanceof ApiError) throw err;
    throw new ApiError(500, err?.message ?? 'Failed to start session');
  } finally {
    clearTimeout(timer);
  }
};

export const stopSession = async (sessionId: string, timeoutMs = 10000): Promise<boolean> => {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), Math.max(0, timeoutMs));
  try {
    const res = await fetch(buildApiUrl('/api/sessions/stop'), {
      method: 'POST',
      headers: buildAuthHeaders(true),
      body: JSON.stringify({ sessionId }),
      signal: controller.signal
    });

    if (res.ok) return true;
    const data = await parseJsonSafe<StopSessionResponse>(res);
    if (res.status === 404) throw new ApiError(404, 'Session not found', undefined, data);
    if (controller.signal.aborted || res.status === 504) throw new ApiError(504, 'Stop request timed out', undefined, data);
    const message = (data as any)?.error?.message || `HTTP ${res.status}`;
    throw new ApiError(res.status, message, undefined, data);
  } catch (err: any) {
    if (err?.name === 'AbortError') throw new ApiError(504, 'Stop request timed out');
    if (err instanceof ApiError) throw err;
    throw new ApiError(500, err?.message ?? 'Failed to stop session');
  } finally {
    clearTimeout(timer);
  }
};
