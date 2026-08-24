import { ApiError, deleteJson, getJson, patchJson, postJson, putJson } from '../lib/api';
import type { DetectionTargetDto } from './commands';
import type { ParameterBinding, ParameterDeclaration } from '../components/parameters/types';
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
  /** Parameters this sequence declares (feature 078); absent when unparametrized. */
  parameters?: ParameterDeclaration[] | null;
};

export type SequenceCreate = {
  name: string;
  steps?: string[] | FlowStep[] | SequenceLinearStep[];
  version?: number;
  entryStepId?: string;
  links?: BranchLink[];
  interStepDelayRangeMs?: InterStepDelayRangeMs | null;
  parameters?: ParameterDeclaration[];
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

/**
 * Runs a sequence ad hoc.
 *
 * @param sequenceId Sequence to run.
 * @param sessionId Optional session override.
 * @param parameters Values for an ad-hoc run (feature 078, FR-031). A run outside any queue has no
 *   built-ins to inherit, so a required parameter with no default must be supplied here or the
 *   backend refuses the run with 409 `missing_required_parameters`.
 */
export const executeSequence = (
  sequenceId: string,
  sessionId?: string,
  parameters?: ParameterBinding[],
) =>
  postJson<SequenceExecuteResponse>(`${base}/${sequenceId}/execute`, {
    ...(sessionId ? { sessionId } : {}),
    ...(parameters && parameters.length > 0 ? { parameters } : {}),
  });

export const isSequenceConflictError = (error: unknown): error is SequenceConflictError => {
  return error instanceof ApiError
         && error.status === 409
         && typeof error.payload?.sequenceId === 'string'
         && typeof error.payload?.currentVersion === 'number';
};
