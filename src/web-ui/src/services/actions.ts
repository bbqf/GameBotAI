import { deleteJson, getJson, postJson, putJson } from '../lib/api';

export type ActionDto = {
  id: string;
  name: string;
  description?: string;
};

export type ActionCreate = {
  name: string;
  description?: string;
};

export type ActionUpdate = ActionCreate;

const base = '/api/actions';

export const listActions = () => getJson<ActionDto[]>(base);
export const getAction = (id: string) => getJson<ActionDto>(`${base}/${id}`);
export const createAction = (input: ActionCreate) => postJson<ActionDto>(base, input);
export const updateAction = (id: string, input: ActionUpdate) => putJson<ActionDto>(`${base}/${id}`, input);
export const deleteAction = (id: string) => deleteJson<void>(`${base}/${id}`);
