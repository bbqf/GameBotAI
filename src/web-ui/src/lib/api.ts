import { getBaseUrl } from './config';
import { token$ } from './token';

export type ApiValidationError = {
  field?: string;
  message: string;
};

export class ApiError extends Error {
  status: number;
  errors?: ApiValidationError[];
  references?: Record<string, Array<{ id: string; name: string }>>;
  payload?: any;
  constructor(status: number, message: string, errors?: ApiValidationError[], payload?: any, references?: Record<string, Array<{ id: string; name: string }>>) {
    super(message);
    this.status = status;
    this.errors = errors;
    this.payload = payload;
    this.references = references;
  }
}

export const buildApiUrl = (path: string) => {
  const base = getBaseUrl().trim();
  if (!base) return path; // same-origin
  return base.endsWith('/') ? base.slice(0, -1) + path : base + path;
};

export const buildAuthHeaders = (includeJsonContentType = true) => {
  const headers: Record<string, string> = includeJsonContentType ? { 'Content-Type': 'application/json' } : {};
  const t = token$.get();
  if (t && t.length > 0) headers['Authorization'] = `Bearer ${t}`;
  return headers;
};

const buildUrl = buildApiUrl;
const buildHeaders = () => buildAuthHeaders(true);

export const apiGet = (path: string) => {
  return fetch(buildUrl(path), {
    method: 'GET',
    headers: buildHeaders()
  });
};

export const apiPost = (path: string, body: unknown) => {
  return fetch(buildUrl(path), {
    method: 'POST',
    headers: buildHeaders(),
    body: JSON.stringify(body)
  });
};

export const apiPatch = (path: string, body: unknown) => {
  return fetch(buildUrl(path), {
    method: 'PATCH',
    headers: buildHeaders(),
    body: JSON.stringify(body)
  });
};

export const apiDelete = (path: string) => {
  return fetch(buildUrl(path), {
    method: 'DELETE',
    headers: buildHeaders()
  });
};

// JSON helpers with error mapping
const parseJsonSafe = async <T>(res: Response): Promise<T | undefined> => {
  try {
    return (await res.json()) as T;
  } catch {
    return undefined;
  }
};

const request = async <T>(method: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE', path: string, body?: unknown): Promise<T> => {
  const res = await fetch(buildUrl(path), {
    method,
    headers: buildHeaders(),
    body: body === undefined ? undefined : JSON.stringify(body)
  });

  if (res.ok) {
    // 204 no content
    if (res.status === 204) return undefined as unknown as T;
    const data = await parseJsonSafe<T>(res);
    return (data as T) ?? (undefined as unknown as T);
  }

  const data = await parseJsonSafe<any>(res);
  if (res.status === 400) {
    const errors: ApiValidationError[] | undefined = Array.isArray(data?.errors)
      ? data.errors.map((e: any) => ({ field: e.field, message: e.message ?? String(e) }))
      : undefined;
    throw new ApiError(400, 'Validation error', errors, data);
  }
  const message = (data?.error && (data.error.message ?? data.error.code)) || data?.message || `HTTP ${res.status}`;
  const references = data?.references as Record<string, Array<{ id: string; name: string }>> | undefined;
  throw new ApiError(res.status, message, undefined, data, references);
};

export const getJson = async <T>(path: string) => request<T>('GET', path);
export const postJson = async <T>(path: string, body: unknown) => request<T>('POST', path, body);
export const patchJson = async <T>(path: string, body: unknown) => request<T>('PATCH', path, body);
export const deleteJson = async <T>(path: string) => request<T>('DELETE', path);
export const putJson = async <T>(path: string, body: unknown) => request<T>('PUT', path, body);
