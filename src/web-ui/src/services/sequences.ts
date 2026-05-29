import { ApiError, deleteJson, getJson, patchJson, postJson, putJson } from '../lib/api';
import type { DetectionTargetDto } from './commands';
import type {
  BranchLink,
  FlowStep,
  InterStepDelayRangeMs,
  SequenceFlowUpsertRequest,
  SequenceLinearStep,
  SequenceLinearUpsertRequest,
  SequenceSaveConflict
} from '../types/sequenceFlow';

export type WaitForImageSequencePayload = {
  timeoutMs?: number;
  detectionTarget?: DetectionTargetDto;
};

export type WaitForImagePrimitiveActionDto = {
  type: 'WaitForImage';
  schemaVersion?: string;
  payload: WaitForImageSequencePayload;
};

export type SequenceDto = {
  id: string;
  name: string;
  version?: number;
  entryStepId?: string;
  steps: string[] | FlowStep[] | SequenceLinearStep[];
  links?: BranchLink[];
  interStepDelayRangeMs?: InterStepDelayRangeMs | null;
};

export type SequenceCreate = {
  name: string;
  steps?: string[] | FlowStep[] | SequenceLinearStep[];
  version?: number;
  entryStepId?: string;
  links?: BranchLink[];
  interStepDelayRangeMs?: InterStepDelayRangeMs | null;
};

export type SequenceLinearCreate = SequenceLinearUpsertRequest;

export type SequenceUpdate = SequenceCreate;

export type SequenceConflictError = ApiError & {
  status: 409;
  payload: SequenceSaveConflict;
};

export type SequenceExecutionStepDto = {
  commandId: string;
  status: string;
  actionOutcome?: string;
  message?: string;
};

export type SequenceExecuteResponse = {
  sequenceId: string;
  status: string;
  steps: SequenceExecutionStepDto[];
};

const base = '/api/sequences';

export const listSequences = () => getJson<SequenceDto[]>(base);
export const getSequence = (id: string) => getJson<SequenceDto>(`${base}/${id}`);
export const createSequence = (input: SequenceCreate) => postJson<SequenceDto>(base, input);
export const updateSequence = (id: string, input: SequenceUpdate) => putJson<SequenceDto>(`${base}/${id}`, input);
export const patchSequence = (id: string, input: SequenceUpdate) => patchJson<SequenceDto>(`${base}/${id}`, input);
export const deleteSequence = (id: string) => deleteJson<void>(`${base}/${id}`);

export const validateSequenceFlow = (sequenceId: string, input: SequenceFlowUpsertRequest) =>
  postJson<{ valid: boolean; errors: string[] }>(`${base}/${sequenceId}/validate`, input);

export const executeSequence = (sequenceId: string, sessionId?: string) =>
  postJson<SequenceExecuteResponse>(`${base}/${sequenceId}/execute`, sessionId ? { sessionId } : {});

export const isSequenceConflictError = (error: unknown): error is SequenceConflictError => {
  return error instanceof ApiError
         && error.status === 409
         && typeof error.payload?.sequenceId === 'string'
         && typeof error.payload?.currentVersion === 'number';
};
