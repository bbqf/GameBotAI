import type { BranchLink, ConditionExpression, FlowStep, SequenceFlowUpsertRequest } from '../types/sequenceFlow';

export type ConditionDraft = {
  stepId: string;
  sourceStepId: string;
  trueTargetId: string;
  falseTargetId: string;
  expression: ConditionExpression;
};

export const createDefaultConditionExpression = (): ConditionExpression => ({
  nodeType: 'operand',
  operand: {
    operandType: 'command-outcome',
    targetRef: '',
    expectedState: 'success'
  }
});

export const buildSequenceFlow = (
  name: string,
  version: number,
  commandIds: string[],
  entryStepId: string,
  conditions: ConditionDraft[]
): SequenceFlowUpsertRequest => {
  const commandSteps: FlowStep[] = commandIds.map((commandId) => ({
    stepId: commandId,
    label: commandId,
    stepType: 'command',
    payloadRef: commandId
  }));

  const conditionSteps: FlowStep[] = conditions.map((condition) => ({
    stepId: condition.stepId,
    label: condition.stepId,
    stepType: 'condition',
    condition: condition.expression
  }));

  const links: BranchLink[] = [];
  if (conditions.length === 0) {
    for (let index = 0; index < commandIds.length - 1; index += 1) {
      links.push({
        linkId: `next-${index + 1}`,
        sourceStepId: commandIds[index],
        targetStepId: commandIds[index + 1],
        branchType: 'next'
      });
    }
  }

  for (const condition of conditions) {
    links.push({
      linkId: `cond-next-${condition.stepId}`,
      sourceStepId: condition.sourceStepId,
      targetStepId: condition.stepId,
      branchType: 'next'
    });
    links.push({
      linkId: `cond-true-${condition.stepId}`,
      sourceStepId: condition.stepId,
      targetStepId: condition.trueTargetId,
      branchType: 'true'
    });
    links.push({
      linkId: `cond-false-${condition.stepId}`,
      sourceStepId: condition.stepId,
      targetStepId: condition.falseTargetId,
      branchType: 'false'
    });
  }

  return {
    name,
    version,
    entryStepId,
    steps: [...commandSteps, ...conditionSteps],
    links
  };
};
