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

describe('SequencesPage unresolved commands', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listSequencesMock.mockResolvedValue([{ id: 'seq-missing', name: 'Missing Command Sequence', steps: [] }] as any);
    listCommandsMock.mockResolvedValue([
      { id: 'c1', name: 'Command One' }
    ] as any);
  });

  it('shows unresolved saved command names instead of an empty selection and preserves them on save', async () => {
    getSequenceMock.mockResolvedValue({
      id: 'seq-missing',
      name: 'Missing Command Sequence',
      version: 7,
      steps: [
        {
          stepId: 'step-1',
          label: 'Deleted command step',
          stepType: 'Action',
          primitiveAction: {
            type: 'command',
            schemaVersion: 'v1',
            payload: {
              commandId: 'missing-cmd'
            }
          },
          commandReference: {
            commandId: 'missing-cmd',
            commandName: 'Deleted Command',
            isResolved: false
          },
          condition: null
        }
      ]
    } as any);
    updateSequenceMock.mockResolvedValue({} as any);

    render(<SequencesPage />);

    await screen.findByText('Missing Command Sequence');
    fireEvent.click(screen.getByText('Missing Command Sequence'));

    await screen.findByText('Edit Sequence');

    expect(screen.getByText('Deleted Command (unresolved)')).toBeInTheDocument();
    expect(screen.queryByText('missing-cmd')).not.toBeInTheDocument();

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateSequenceMock).toHaveBeenCalledWith('seq-missing', {
      name: 'Missing Command Sequence',
      version: 7,
      interStepDelayRangeMs: null,
      steps: [
        {
          stepId: 'step-1',
          stepType: 'Action',
          primitiveAction: {
            type: 'command',
            schemaVersion: 'v1',
            payload: {
              commandId: 'missing-cmd'
            }
          },
          commandReference: {
            commandId: 'missing-cmd',
            commandName: 'Deleted Command',
            isResolved: false
          },
          condition: null
        }
      ]
    }));
  });
});