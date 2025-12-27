import { ApiError, buildApiUrl, buildAuthHeaders, deleteJson, getJson, postJson, putJson } from '../lib/api';
import {
  ActionCreate,
  ActionDto,
  ActionListParams,
  ActionType,
  ActionTypeCatalog,
  ActionUpdate,
  ValidationResult
} from '../types/actions';

const actionsBase = '/api/actions';
const actionTypesBase = '/api/action-types';

type DomainAction = {
  id: string;
  name: string;
  gameId?: string;
  steps?: Array<{ type: string; args?: Record<string, unknown>; delayMs?: number; durationMs?: number }>;
  checkpoints?: string[];
};

const toDto = (domain: DomainAction): ActionDto => {
  const firstStep = domain.steps?.[0];
  const attrs: Record<string, unknown> = { ...(firstStep?.args ?? {}) };
  if (firstStep?.durationMs !== undefined && firstStep.durationMs !== null) {
    attrs.durationMs = firstStep.durationMs;
  }
  return {
    id: domain.id,
    name: domain.name,
    gameId: domain.gameId ?? '',
    type: firstStep?.type ?? '',
    attributes: attrs,
    validationStatus: undefined,
    validationMessages: undefined,
    createdBy: undefined,
    updatedBy: undefined,
    updatedAt: undefined
  };
};

const normalizeDuration = (attributes: Record<string, unknown>): { durationMs?: number; rest: Record<string, unknown> } => {
  const copy = { ...attributes };
  if (!Object.prototype.hasOwnProperty.call(copy, 'durationMs')) return { rest: copy };
  const raw = (copy as any).durationMs;
  delete (copy as any).durationMs;
  if (raw === undefined || raw === null) return { rest: copy };
  if (typeof raw === 'number' && Number.isFinite(raw)) return { durationMs: raw, rest: copy };
  if (typeof raw === 'string' && raw.trim().length > 0 && !Number.isNaN(Number(raw))) return { durationMs: Number(raw), rest: copy };
  return { rest: copy };
};

const fromCreate = (payload: ActionCreate): DomainAction => {
  const { durationMs, rest } = normalizeDuration(payload.attributes ?? {});
  return {
  id: '',
  name: payload.name,
  gameId: payload.gameId,
  steps: [
    {
      type: payload.type,
        args: rest,
        delayMs: 0,
        durationMs
    }
  ],
  checkpoints: []
  };
};

const fromUpdate = (payload: ActionUpdate, id: string): DomainAction => {
  const { durationMs, rest } = normalizeDuration(payload.attributes ?? {});
  return {
    id,
    name: payload.name,
    gameId: payload.gameId,
    steps: [
      {
        type: payload.type,
        args: rest,
        delayMs: 0,
        durationMs
      }
    ],
    checkpoints: []
  };
};

export type ActionTypeFetchResult = {
  catalog?: ActionTypeCatalog;
  etag?: string;
  notModified: boolean;
};

export const getActionTypes = async (etag?: string): Promise<ActionTypeFetchResult> => {
  const headers = buildAuthHeaders(false);
  headers['Accept'] = 'application/json';
  if (etag) headers['If-None-Match'] = etag;

  const res = await fetch(buildApiUrl(actionTypesBase), { method: 'GET', headers });
  if (res.status === 304) {
    return { catalog: undefined, etag, notModified: true };
  }
  if (!res.ok) {
    const message = (await res.text()) || `HTTP ${res.status}`;
    throw new ApiError(res.status, message);
  }
  const data = (await res.json()) as ActionTypeCatalog;
  const nextEtag = res.headers.get('ETag') ?? undefined;
  return { catalog: data, etag: nextEtag, notModified: false };
};

export const getActionType = async (key: string): Promise<ActionType> => {
  return getJson<ActionType>(`${actionTypesBase}/${encodeURIComponent(key)}`);
};

export const listActions = async (params?: ActionListParams): Promise<ActionDto[]> => {
  const queryParams = new URLSearchParams();
  if (params?.gameId) queryParams.append('gameId', params.gameId);
  if (params?.type) queryParams.append('type', params.type);
  const query = queryParams.toString();
  const raw = await getJson<DomainAction[]>(`${actionsBase}${query ? `?${query}` : ''}`);
  const mapped = raw.map(toDto);
  let filtered = mapped;
  if (params?.gameId) filtered = filtered.filter((a) => a.gameId === params.gameId);
  if (params?.type) filtered = filtered.filter((a) => a.type === params.type);
  return filtered;
};

export const getAction = async (id: string): Promise<ActionDto> => {
  const raw = await getJson<DomainAction>(`${actionsBase}/${encodeURIComponent(id)}`);
  return toDto(raw);
};

export const createAction = async (payload: ActionCreate): Promise<ActionDto> => {
  const raw = await postJson<DomainAction>(actionsBase, fromCreate(payload));
  return toDto(raw);
};

export const updateAction = async (id: string, payload: ActionUpdate): Promise<ActionDto> => {
  const raw = await putJson<DomainAction>(`${actionsBase}/${encodeURIComponent(id)}`, fromUpdate(payload, id));
  return toDto(raw);
};

export const duplicateAction = async (id: string): Promise<ActionDto> => {
  const raw = await postJson<DomainAction>(`${actionsBase}/${encodeURIComponent(id)}/duplicate`, {});
  return toDto(raw);
};

export const deleteAction = async (id: string): Promise<void> =>
  deleteJson<void>(`${actionsBase}/${encodeURIComponent(id)}`);

export const validateAction = async (payload: ActionCreate | ActionUpdate): Promise<ValidationResult> =>
  postJson<ValidationResult>(`${actionsBase}/validate`, payload);
