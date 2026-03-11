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
      steps: [
        {
          stepId: 'step-1',
          label: 'Command One',
          action: {
            type: 'command',
            parameters: { commandId: 'cmd-1' }
          },
          condition: null
        },
        {
          stepId: 'step-2',
          label: 'Command Two',
          action: {
            type: 'command',
            parameters: { commandId: 'cmd-2' }
          },
          condition: {
            type: 'imageVisible',
            imageId: 'image-a',
            minSimilarity: 0.85
          }
        }
      ]
    } as any);
  });

  it('loads existing conditional flow into edit mode and saves edited values', async () => {
    render(<SequencesPage />);

    await screen.findByText('Conditional Sequence');
    fireEvent.click(screen.getByText('Conditional Sequence'));

    await screen.findByText('Edit Sequence');
    const conditionTypeFields = screen.getAllByLabelText('Condition Type');
    expect(conditionTypeFields[1]).toHaveValue('imageVisible');
    expect(screen.getByLabelText('Image Id')).toHaveValue('image-a');
    expect(screen.getByLabelText('Min Similarity')).toHaveValue('0.85');

    fireEvent.change(screen.getByLabelText('Min Similarity'), { target: { value: '0.93' } });
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(updateSequenceMock).toHaveBeenCalledWith('seq-1', expect.objectContaining({
        version: 3,
        steps: expect.arrayContaining([
          expect.objectContaining({
            stepId: 'step-2',
            condition: expect.objectContaining({
              type: 'imageVisible',
              imageId: 'image-a',
              minSimilarity: 0.93
            })
          })
        ])
      }));
    });
  });
});
