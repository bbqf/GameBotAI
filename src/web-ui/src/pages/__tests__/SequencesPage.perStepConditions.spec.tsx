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

describe('SequencesPage per-step conditions', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listSequencesMock.mockResolvedValue([] as any);
    listCommandsMock.mockResolvedValue([
      { id: 'cmd-home', name: 'Home' },
      { id: 'cmd-back', name: 'Back' }
    ] as any);
    createSequenceMock.mockResolvedValue({ id: 'seq-per-step', name: 'Per Step', steps: [] } as any);
  });

  it('submits linear per-step payload with optional condition mapping', async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Per Step' } });

    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-home' } });
    fireEvent.click(screen.getByText('Add to steps'));
    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-back' } });
    fireEvent.click(screen.getByText('Add to steps'));

    expect(screen.queryByLabelText('Enable per-step conditions')).not.toBeInTheDocument();
    expect(screen.queryByText('Per-Step Conditions')).not.toBeInTheDocument();

    const conditionTypeFields = screen.getAllByLabelText('Condition Type');
    fireEvent.change(conditionTypeFields[1], { target: { value: 'commandOutcome' } });
    fireEvent.change(screen.getByLabelText('Step Ref'), { target: { value: 'step-1' } });
    fireEvent.change(screen.getByLabelText('Expected State'), { target: { value: 'skipped' } });

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(createSequenceMock).toHaveBeenCalledWith(expect.objectContaining({
        name: 'Per Step',
        version: 1,
        steps: expect.arrayContaining([
          expect.objectContaining({
            stepId: 'step-1',
            action: expect.objectContaining({
              type: 'command',
              parameters: expect.objectContaining({ commandId: 'cmd-home' })
            })
          }),
          expect.objectContaining({
            stepId: 'step-2',
            condition: expect.objectContaining({
              type: 'commandOutcome',
              stepRef: 'step-1',
              expectedState: 'skipped'
            })
          })
        ])
      }));
    });
  });
});
