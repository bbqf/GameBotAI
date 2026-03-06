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

  it('submits a mixed command/condition flow payload', async () => {
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

    fireEvent.click(screen.getByLabelText('Enable conditional flow'));
    fireEvent.change(screen.getByLabelText('Entry Step'), { target: { value: 'cmd-a' } });

    fireEvent.click(screen.getByText('Add Condition Step'));
    fireEvent.change(screen.getByLabelText('Condition Step Id'), { target: { value: 'cond-a' } });
    fireEvent.change(screen.getByLabelText('True Target'), { target: { value: 'cmd-b' } });
    fireEvent.change(screen.getByLabelText('False Target'), { target: { value: 'cmd-final' } });
    fireEvent.change(screen.getByLabelText('Operand Type'), { target: { value: 'image-detection' } });
    fireEvent.change(screen.getByLabelText('Operand Target (0)'), { target: { value: 'image-a' } });
    fireEvent.change(screen.getByLabelText('Expected State'), { target: { value: 'present' } });

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(createSequenceMock).toHaveBeenCalledWith(expect.objectContaining({
        name: 'US2 Flow',
        entryStepId: 'cmd-a',
        steps: expect.arrayContaining([
          expect.objectContaining({ stepId: 'cmd-a', stepType: 'command', payloadRef: 'cmd-a' }),
          expect.objectContaining({ stepId: 'cond-a', stepType: 'condition' }),
          expect.objectContaining({ stepId: 'cmd-final', stepType: 'command', payloadRef: 'cmd-final' })
        ]),
        links: expect.arrayContaining([
          expect.objectContaining({ sourceStepId: 'cmd-a', targetStepId: 'cond-a', branchType: 'next' }),
          expect.objectContaining({ sourceStepId: 'cond-a', targetStepId: 'cmd-b', branchType: 'true' }),
          expect.objectContaining({ sourceStepId: 'cond-a', targetStepId: 'cmd-final', branchType: 'false' })
        ])
      }));
    });
  });
});
