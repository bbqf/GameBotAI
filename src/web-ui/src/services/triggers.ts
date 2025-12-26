import { deleteJson, getJson, postJson, putJson } from '../lib/api';

export type TriggerDto = {
  id: string;
  name: string;
  criteria?: Record<string, unknown>;
  actions?: string[];
  commands?: string[];
  sequence?: string;
};

export type TriggerCreate = {
  name: string;
  criteria?: Record<string, unknown>;
  actions?: string[];
  commands?: string[];
  sequence?: string;
};

export type TriggerUpdate = TriggerCreate;

const base = '/api/triggers';

export const listTriggers = () => getJson<TriggerDto[]>(base);
export const getTrigger = (id: string) => getJson<TriggerDto>(`${base}/${id}`);
export const createTrigger = (input: TriggerCreate) => postJson<TriggerDto>(base, input);
export const updateTrigger = (id: string, input: TriggerUpdate) => putJson<TriggerDto>(`${base}/${id}`, input);
export const deleteTrigger = (id: string) => deleteJson<void>(`${base}/${id}`);
