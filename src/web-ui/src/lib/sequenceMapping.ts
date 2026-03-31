import type { SequenceDto } from '../services/sequences';
import type {
  FlowStep,
  SequenceLinearStep
} from '../types/sequenceFlow';

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
      && ('action' in step || 'stepType' in step));
};

export const toLinearSteps = (steps: SequenceDto['steps']): SequenceLinearStep[] => {
  return isLinearStepArray(steps) ? steps : [];
};
