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
