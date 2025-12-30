import { deleteJson, getJson, postJson, patchJson } from '../lib/api';

export type CommandDto = {
  id: string;
  name: string;
  triggerId?: string;
  steps?: CommandStepDto[];
  actions?: string[]; // legacy authoring shape
  detectionTarget?: DetectionTargetDto;
};

export type CommandCreate = {
  name: string;
  triggerId?: string;
  steps?: CommandStepDto[];
  actions?: string[]; // legacy authoring shape
  detectionTarget?: DetectionTargetDto;
};

export type CommandUpdate = CommandCreate;

export type CommandStepDto = {
  type: 'Action' | 'Command';
  targetId: string;
  order: number;
};

export type CommandExecuteResponse = {
  accepted: number;
  triggerStatus?: string;
  message?: string;
};

export type DetectionTargetDto = {
  referenceImageId: string;
  confidence?: number;
  offsetX?: number;
  offsetY?: number;
  selectionStrategy?: string;
};

const base = '/api/commands';

export const listCommands = () => getJson<CommandDto[]>(base);
export const getCommand = (id: string) => getJson<CommandDto>(`${base}/${id}`);
export const createCommand = (input: CommandCreate) => postJson<CommandDto>(base, input);
export const updateCommand = (id: string, input: CommandUpdate) => patchJson<CommandDto>(`${base}/${id}`, input);
export const deleteCommand = (id: string) => deleteJson<void>(`${base}/${id}`);
export const forceExecuteCommand = (id: string, sessionId?: string) => {
  const query = sessionId ? `?sessionId=${encodeURIComponent(sessionId)}` : '';
  return postJson<CommandExecuteResponse>(`${base}/${id}/force-execute${query}`, {});
};
