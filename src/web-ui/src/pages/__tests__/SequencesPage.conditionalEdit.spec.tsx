import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { SequencesPage } from '../SequencesPage';
import { getSequence, listSequences, updateSequence } from '../../services/sequences';
import { listCommands } from '../../services/commands';

jest.mock('../../services/sequences');
jest.mock('../../services/commands');

const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;
const getSequenceMock = getSequence as jest.MockedFunction<typeof getSequence>;
const updateSequenceMock = updateSequence as jest.MockedFunction<typeof updateSequence>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;

describe('SequencesPage conditional edit mode', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listCommandsMock.mockResolvedValue([
      { id: 'cmd-1', name: 'Command One' },
      { id: 'cmd-2', name: 'Command Two' }
    ] as any);
    listSequencesMock.mockResolvedValue([{ id: 'seq-1', name: 'Conditional Sequence', steps: [] }] as any);
    updateSequenceMock.mockResolvedValue({ id: 'seq-1', name: 'Conditional Sequence', steps: [] } as any);
    getSequenceMock.mockResolvedValue({
      id: 'seq-1',
      name: 'Conditional Sequence',
      version: 3,
      entryStepId: 'cmd-1',
      steps: [
        { stepId: 'cmd-1', label: 'Command One', stepType: 'command', payloadRef: 'cmd-1' },
        {
          stepId: 'cond-1',
          label: 'Condition 1',
          stepType: 'condition',
          condition: {
            nodeType: 'operand',
            operand: {
              operandType: 'image-detection',
              targetRef: 'image-a',
              expectedState: 'present',
              threshold: 0.85
            }
          }
        },
        { stepId: 'cmd-2', label: 'Command Two', stepType: 'command', payloadRef: 'cmd-2' }
      ],
      links: [
        { linkId: 'n1', sourceStepId: 'cmd-1', targetStepId: 'cond-1', branchType: 'next' },
        { linkId: 't1', sourceStepId: 'cond-1', targetStepId: 'cmd-2', branchType: 'true' },
        { linkId: 'f1', sourceStepId: 'cond-1', targetStepId: 'cmd-1', branchType: 'false' }
      ]
    } as any);
  });

  it('loads existing conditional flow into edit mode and saves edited values', async () => {
    render(<SequencesPage />);

    await screen.findByText('Conditional Sequence');
    fireEvent.click(screen.getByText('Conditional Sequence'));

    await screen.findByText('Edit Sequence');
    expect(screen.getByLabelText('Enable conditional flow')).toBeChecked();
    expect(screen.getByLabelText('Condition Step Id')).toHaveValue('cond-1');
    expect(screen.getByLabelText('Operand Type')).toHaveValue('image-detection');
    expect(screen.getByLabelText('Threshold')).toHaveValue(0.85);

    fireEvent.change(screen.getByLabelText('Threshold'), { target: { value: '0.93' } });
    fireEvent.change(screen.getByLabelText('True Target'), { target: { value: 'cmd-1' } });
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(updateSequenceMock).toHaveBeenCalledWith('seq-1', expect.objectContaining({
        version: 3,
        entryStepId: 'cmd-1'
      }));
    });
  });
});
