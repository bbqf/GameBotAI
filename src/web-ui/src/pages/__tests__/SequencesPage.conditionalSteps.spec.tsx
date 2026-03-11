import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { SequencesPage } from '../SequencesPage';
import { createSequence, listSequences } from '../../services/sequences';
import { listCommands } from '../../services/commands';

jest.mock('../../services/sequences');
jest.mock('../../services/commands');

const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;
const createSequenceMock = createSequence as jest.MockedFunction<typeof createSequence>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;

describe('SequencesPage mixed conditional authoring', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listSequencesMock.mockResolvedValue([] as any);
    listCommandsMock.mockResolvedValue([
      { id: 'cmd-a', name: 'Command A' },
      { id: 'cmd-b', name: 'Command B' },
      { id: 'cmd-final', name: 'Command Final' }
    ] as any);
    createSequenceMock.mockResolvedValue({ id: 'seq-us2', name: 'US2 Flow', steps: [] } as any);
  });

  it('submits a mixed per-step condition payload', async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'US2 Flow' } });

    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-a' } });
    fireEvent.click(screen.getByText('Add to steps'));
    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-b' } });
    fireEvent.click(screen.getByText('Add to steps'));
    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-final' } });
    fireEvent.click(screen.getByText('Add to steps'));

    const conditionTypeFields = screen.getAllByLabelText('Condition Type');
    fireEvent.change(conditionTypeFields[1], { target: { value: 'imageVisible' } });
    fireEvent.change(screen.getByLabelText('Image Id'), { target: { value: 'image-a' } });
    fireEvent.change(screen.getByLabelText('Min Similarity'), { target: { value: '0.82' } });

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(createSequenceMock).toHaveBeenCalledWith(expect.objectContaining({
        name: 'US2 Flow',
        version: 1,
        steps: expect.arrayContaining([
          expect.objectContaining({
            stepId: 'step-1',
            action: expect.objectContaining({
              type: 'command',
              parameters: expect.objectContaining({ commandId: 'cmd-a' })
            }),
            condition: null
          }),
          expect.objectContaining({
            stepId: 'step-2',
            action: expect.objectContaining({
              type: 'command',
              parameters: expect.objectContaining({ commandId: 'cmd-b' })
            }),
            condition: expect.objectContaining({
              type: 'imageVisible',
              imageId: 'image-a',
              minSimilarity: 0.82
            })
          }),
          expect.objectContaining({
            stepId: 'step-3',
            action: expect.objectContaining({
              type: 'command',
              parameters: expect.objectContaining({ commandId: 'cmd-final' })
            })
          })
        ])
      }));
    });
  });
});
