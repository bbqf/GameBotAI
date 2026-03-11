import { ApiError, deleteJson, getJson, patchJson, postJson, putJson } from '../lib/api';
import type {
  BranchLink,
  FlowStep,
  SequenceFlowUpsertRequest,
  SequenceLinearStep,
  SequenceLinearUpsertRequest,
  SequenceSaveConflict
} from '../types/sequenceFlow';

export type SequenceDto = {
  id: string;
  name: string;
  version?: number;
  entryStepId?: string;
  steps: string[] | FlowStep[] | SequenceLinearStep[];
  links?: BranchLink[];
};

export type SequenceCreate = {
  name: string;
  steps?: string[] | FlowStep[] | SequenceLinearStep[];
  version?: number;
  entryStepId?: string;
  links?: BranchLink[];
};

export type SequenceLinearCreate = SequenceLinearUpsertRequest;

export type SequenceUpdate = SequenceCreate;

export type SequenceConflictError = ApiError & {
  status: 409;
  payload: SequenceSaveConflict;
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

export const executeSequence = (sequenceId: string) => postJson<unknown>(`${base}/${sequenceId}/execute`, {});

export const isSequenceConflictError = (error: unknown): error is SequenceConflictError => {
  return error instanceof ApiError
         && error.status === 409
         && typeof error.payload?.sequenceId === 'string'
         && typeof error.payload?.currentVersion === 'number';
};
