import { validateConditionalFlow } from '../validation';
import type { SequenceFlowUpsertRequest } from '../../types/sequenceFlow';

const baseFlow = (): SequenceFlowUpsertRequest => ({
  name: 'Test Flow',
  version: 1,
  entryStepId: 'start',
  steps: [
    { stepId: 'start', label: 'Start', stepType: 'command', payloadRef: 'cmd-1' },
    {
      stepId: 'decision',
      label: 'Decision',
      stepType: 'condition',
      condition: {
        nodeType: 'operand',
        operand: { operandType: 'command-outcome', targetRef: 'cmd-1', expectedState: 'success' }
      }
    },
    { stepId: 'then', label: 'Then', stepType: 'action', payloadRef: 'action-1' },
    { stepId: 'else', label: 'Else', stepType: 'action', payloadRef: 'action-2' }
  ],
  links: [
    { linkId: 'l1', sourceStepId: 'start', targetStepId: 'decision', branchType: 'next' },
    { linkId: 'l2', sourceStepId: 'decision', targetStepId: 'then', branchType: 'true' },
    { linkId: 'l3', sourceStepId: 'decision', targetStepId: 'else', branchType: 'false' }
  ]
});

describe('validateConditionalFlow', () => {
  it('returns error when condition branch targets are missing', () => {
    const flow = baseFlow();
    flow.links = flow.links.filter((link) => link.branchType !== 'false');

    const errors = validateConditionalFlow(flow);

    expect(errors).toContain('Condition step "decision" must define one true and one false branch.');
  });

  it('returns errors for invalid step references in links and entry step', () => {
    const flow = baseFlow();
    flow.entryStepId = 'missing-entry';
    flow.links = [{ linkId: 'bad-link', sourceStepId: 'missing-source', targetStepId: 'missing-target', branchType: 'next' }];

    const errors = validateConditionalFlow(flow);

    expect(errors).toContain('Entry step "missing-entry" does not exist in the flow graph.');
    expect(errors).toContain('Link "bad-link" has unresolved source step "missing-source".');
    expect(errors).toContain('Link "bad-link" has unresolved target step "missing-target".');
  });
});
