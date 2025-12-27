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
  const query = params?.type ? `?type=${encodeURIComponent(params.type)}` : '';
  return getJson<ActionDto[]>(`${actionsBase}${query}`);
};

export const getAction = async (id: string): Promise<ActionDto> => getJson<ActionDto>(`${actionsBase}/${encodeURIComponent(id)}`);

export const createAction = async (payload: ActionCreate): Promise<ActionDto> => postJson<ActionDto>(actionsBase, payload);

export const updateAction = async (id: string, payload: ActionUpdate): Promise<ActionDto> =>
  putJson<ActionDto>(`${actionsBase}/${encodeURIComponent(id)}`, payload);

export const duplicateAction = async (id: string): Promise<ActionDto> =>
  postJson<ActionDto>(`${actionsBase}/${encodeURIComponent(id)}/duplicate`, {});

export const validateAction = async (payload: ActionCreate | ActionUpdate): Promise<ValidationResult> =>
  postJson<ValidationResult>(`${actionsBase}/validate`, payload);
