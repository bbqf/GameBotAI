import { deleteJson, getJson, postJson, patchJson } from '../lib/api';

export type CommandDto = {
  id: string;
  name: string;
  triggerId?: string;
  steps?: CommandStepDto[];
  detection?: DetectionTargetDto;
};

export type CommandCreate = {
  name: string;
  triggerId?: string;
  steps?: CommandStepDto[];
  detection?: DetectionTargetDto;
};

export type CommandUpdate = CommandCreate;

export type CommandStepDto = {
  type: 'Command' | 'PrimitiveTap' | 'WaitForImage' | 'EnsureGameRunning' | 'KeyInput' | 'Swipe';
  targetId?: string;
  order: number;
  primitiveTap?: PrimitiveTapConfigDto;
  waitForImage?: WaitForImageConfigDto;
  keyInput?: KeyInputConfigDto;
  swipe?: SwipeConfigDto;
};

export type KeyInputConfigDto = {
  key: string;
};

export type SwipeConfigDto = {
  startX: number;
  startY: number;
  endX: number;
  endY: number;
  durationMs?: number;
};

export type PrimitiveTapConfigDto = {
  detectionTarget: DetectionTargetDto;
};

export type WaitForImageConfigDto = {
  detectionTarget?: DetectionTargetDto;
  timeoutMs?: number;
};

export type ResolvedPointDto = {
  x: number;
  y: number;
};

export type StepOutcomeDto = {
  stepOrder: number;
  status: string;
  stepType?: string;
  reason?: string;
  resolvedPoint?: ResolvedPointDto;
  detectionConfidence?: number;
  timeoutMs?: number;
  effectiveTimeoutMs?: number;
  referenceImageId?: string;
  imageLoadStatus?: string;
};

export type CommandExecuteResponse = {
  accepted: number;
  triggerStatus?: string;
  message?: string;
  stepOutcomes?: StepOutcomeDto[];
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

export const executeStep = (step: CommandStepDto, sessionId?: string) =>
  postJson<CommandExecuteResponse>('/api/steps/execute', { step, sessionId });
