import { validateConditionalFlow } from '../validation';
import type { SequenceFlowUpsertRequest } from '../../types/sequenceFlow';

const buildFlow = (): SequenceFlowUpsertRequest => ({
  name: 'Mixed Sequence',
  version: 1,
  entryStepId: 'cmd-a',
  steps: [
    { stepId: 'cmd-a', label: 'A', stepType: 'command', payloadRef: 'cmd-a' },
    {
      stepId: 'cond-1',
      label: 'Check A',
      stepType: 'condition',
      condition: {
        nodeType: 'operand',
        operand: { operandType: 'image-detection', targetRef: 'image-a', expectedState: 'present', threshold: 0.85 }
      }
    },
    { stepId: 'cmd-b', label: 'B', stepType: 'command', payloadRef: 'cmd-b' },
    { stepId: 'cmd-c', label: 'C', stepType: 'command', payloadRef: 'cmd-c' }
  ],
  links: [
    { linkId: 'n1', sourceStepId: 'cmd-a', targetStepId: 'cond-1', branchType: 'next' },
    { linkId: 't1', sourceStepId: 'cond-1', targetStepId: 'cmd-b', branchType: 'true' },
    { linkId: 'f1', sourceStepId: 'cond-1', targetStepId: 'cmd-c', branchType: 'false' }
  ]
});

describe('sequence conditional validation', () => {
  it('rejects image-detection condition without imageId/targetRef', () => {
    const flow = buildFlow();
    const conditional = flow.steps.find((step) => step.stepType === 'condition');
    if (!conditional?.condition?.operand) {
      throw new Error('missing condition operand in test fixture');
    }
    conditional.condition.operand.targetRef = '';

    const errors = validateConditionalFlow(flow);

    expect(errors.some((error) => /imageId\/targetRef/i.test(error))).toBe(true);
  });

  it('rejects invalid image-detection expectedState values', () => {
    const flow = buildFlow();
    const conditional = flow.steps.find((step) => step.stepType === 'condition');
    if (!conditional?.condition?.operand) {
      throw new Error('missing condition operand in test fixture');
    }
    conditional.condition.operand.expectedState = 'success';

    const errors = validateConditionalFlow(flow);

    expect(errors.some((error) => /expectedState must be present or absent/i.test(error))).toBe(true);
  });

  it('rejects command/action steps without payloadRef', () => {
    const flow = buildFlow();
    const commandStep = flow.steps.find((step) => step.stepId === 'cmd-c');
    if (!commandStep) {
      throw new Error('missing command step in test fixture');
    }
    commandStep.payloadRef = '';

    const errors = validateConditionalFlow(flow);

    expect(errors.some((error) => /requires an action payload reference/i.test(error))).toBe(true);
  });
});
