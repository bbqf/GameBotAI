import { deleteJson, getJson, postJson, putJson } from '../lib/api';

export type CommandDto = {
  id: string;
  name: string;
  parameters?: Record<string, unknown>;
  actions?: string[];
};

export type CommandCreate = {
  name: string;
  parameters?: Record<string, unknown>;
  actions?: string[];
};

export type CommandUpdate = CommandCreate;

const base = '/api/commands';

export const listCommands = () => getJson<CommandDto[]>(base);
export const getCommand = (id: string) => getJson<CommandDto>(`${base}/${id}`);
export const createCommand = (input: CommandCreate) => postJson<CommandDto>(base, input);
export const updateCommand = (id: string, input: CommandUpdate) => putJson<CommandDto>(`${base}/${id}`, input);
export const deleteCommand = (id: string) => deleteJson<void>(`${base}/${id}`);
