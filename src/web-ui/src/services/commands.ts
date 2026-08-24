import { deleteJson, getJson, postJson, patchJson } from '../lib/api';
import type {
  ParameterBinding,
  ParameterDeclaration,
  ParameterWarning,
} from '../components/parameters/types';

export type CommandDto = {
  id: string;
  name: string;
  triggerId?: string;
  steps?: CommandStepDto[];
  detection?: DetectionTargetDto;
  /** Parameters this command declares (feature 078); absent when unparametrized. */
  parameters?: ParameterDeclaration[];
  /** Non-blocking parameter advisories returned by the last save. */
  warnings?: ParameterWarning[];
};

export type CommandCreate = {
  name: string;
  triggerId?: string;
  steps?: CommandStepDto[];
  detection?: DetectionTargetDto;
  parameters?: ParameterDeclaration[];
};

export type CommandUpdate = CommandCreate;

export type CommandStepDto = {
  type: 'Command' | 'PrimitiveTap' | 'WaitForImage' | 'EnsureGameRunning' | 'GoToHomeScreen' | 'KeyInput' | 'Swipe' | 'EnsureEmulatorRunning';
  targetId?: string;
  order: number;
  primitiveTap?: PrimitiveTapConfigDto;
  waitForImage?: WaitForImageConfigDto;
  keyInput?: KeyInputConfigDto;
  swipe?: SwipeConfigDto;
  ensureEmulatorRunning?: EnsureEmulatorRunningConfigDto;
  /**
   * Placeholders for this step's numeric fields, keyed by dotted path (feature 078), e.g.
   * `{ 'swipe.startX': '{{originX}}' }`. String fields carry their placeholder inline instead.
   */
  fieldTemplates?: Record<string, string>;
  /** Values bound for the invoked command's parameters; only meaningful on a Command step. */
  parameterBindings?: ParameterBinding[];
};

export type EnsureEmulatorRunningConfigDto = {
  instanceName?: string;
  instanceIndex?: number;
  adbSerial: string;
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
