import { ApiError, deleteJson, getJson, patchJson, postJson, putJson } from '../lib/api';
import type { BranchLink, FlowStep, SequenceFlow, SequenceFlowUpsertRequest, SequenceSaveConflict } from '../types/sequenceFlow';

export type SequenceDto = {
  id: string;
  name: string;
  version?: number;
  entryStepId?: string;
  steps: string[] | FlowStep[];
  links?: BranchLink[];
};

export type SequenceCreate = {
  name: string;
  steps?: string[] | FlowStep[];
  version?: number;
  entryStepId?: string;
  links?: BranchLink[];
};

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

export const toSequenceFlowRequest = (dto: SequenceDto): SequenceFlowUpsertRequest | null => {
  if (!dto.entryStepId || !Array.isArray(dto.links) || !isFlowStepArray(dto.steps)) {
    return null;
  }

  return {
    name: dto.name,
    version: dto.version ?? 1,
    entryStepId: dto.entryStepId,
    steps: dto.steps,
    links: dto.links
  };
};

export const getSequenceFlow = async (sequenceId: string): Promise<SequenceFlow> => {
  const dto = await getJson<SequenceDto>(`${base}/${sequenceId}`);
  const flow = toSequenceFlowRequest(dto);
  if (!flow) {
    throw new ApiError(400, 'Sequence does not have conditional flow data.');
  }

  return {
    sequenceId: dto.id,
    ...flow
  };
};

export const executeSequence = (sequenceId: string) => postJson<unknown>(`${base}/${sequenceId}/execute`, {});

export const isSequenceConflictError = (error: unknown): error is SequenceConflictError => {
  return error instanceof ApiError
         && error.status === 409
         && typeof error.payload?.sequenceId === 'string'
         && typeof error.payload?.currentVersion === 'number';
};

const isFlowStepArray = (steps: SequenceDto['steps']): steps is FlowStep[] => {
  return Array.isArray(steps) && steps.every((step) => typeof step === 'object' && step !== null && 'stepId' in step);
};
