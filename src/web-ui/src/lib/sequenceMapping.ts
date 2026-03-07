import type { SequenceDto } from '../services/sequences';
import type {
  BranchLink,
  ConditionExpression,
  FlowStep,
  SequenceFlowUpsertRequest,
  SequenceLinearStep
} from '../types/sequenceFlow';
import type { ConditionDraft } from './sequenceFlowGraph';
import { createDefaultConditionExpression } from './sequenceFlowGraph';

export const isFlowStepArray = (steps: SequenceDto['steps']): steps is FlowStep[] => {
  return Array.isArray(steps) && steps.every((step) => typeof step === 'object' && step !== null && 'stepId' in step);
};

export const toCommandStepIds = (steps: SequenceDto['steps']): string[] => {
  if (steps.length === 0) {
    return [];
  }

  const first = steps[0];
  if (typeof first === 'string') {
    return steps as string[];
  }

  if (isLinearStepArray(steps)) {
    return steps
      .map((step) => step.action?.parameters?.commandId)
      .filter((value): value is string => typeof value === 'string' && value.length > 0);
  }

  return (steps as Array<{ stepType?: string; payloadRef?: string | null }>)
    .filter((step) => step.stepType === 'command' && !!step.payloadRef)
    .map((step) => step.payloadRef as string);
};

export const isLinearStepArray = (steps: SequenceDto['steps']): steps is SequenceLinearStep[] => {
  return Array.isArray(steps)
    && steps.every((step) => typeof step === 'object'
      && step !== null
      && 'stepId' in step
      && 'action' in step);
};

export const toLinearSteps = (steps: SequenceDto['steps']): SequenceLinearStep[] => {
  return isLinearStepArray(steps) ? steps : [];
};

export const toConditionDrafts = (steps: FlowStep[], links: BranchLink[], fallbackSourceStepId: string): ConditionDraft[] => {
  return steps
    .filter((step) => step.stepType === 'condition')
    .map((step) => {
      const nextLink = links.find((link) => link.targetStepId === step.stepId && link.branchType === 'next');
      const trueLink = links.find((link) => link.sourceStepId === step.stepId && link.branchType === 'true');
      const falseLink = links.find((link) => link.sourceStepId === step.stepId && link.branchType === 'false');

      return {
        stepId: step.stepId,
        sourceStepId: nextLink?.sourceStepId ?? fallbackSourceStepId,
        trueTargetId: trueLink?.targetStepId ?? '',
        falseTargetId: falseLink?.targetStepId ?? '',
        expression: step.condition ?? (createDefaultConditionExpression() as ConditionExpression)
      };
    });
};

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
