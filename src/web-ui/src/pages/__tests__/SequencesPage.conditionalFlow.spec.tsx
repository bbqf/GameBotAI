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

describe('SequencesPage commandOutcome authoring', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listSequencesMock.mockResolvedValue([] as any);
    listCommandsMock.mockResolvedValue([
      { id: 'cmd-1', name: 'Command One' },
      { id: 'cmd-2', name: 'Command Two' }
    ] as any);
    createSequenceMock.mockResolvedValue({ id: 'seq-1', name: 'Flow Sequence', steps: [] } as any);
  });

  it('shows validation when commandOutcome is missing stepRef', async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Flow Sequence' } });

    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-1' } });
    fireEvent.click(screen.getByText('Add to steps'));
    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-2' } });
    fireEvent.click(screen.getByText('Add to steps'));

    const conditionTypeFields = screen.getAllByLabelText('Condition Type');
    fireEvent.change(conditionTypeFields[1], { target: { value: 'commandOutcome' } });

    fireEvent.click(screen.getByText('Save'));

    expect(await screen.findByText(/requires stepRef/i)).toBeInTheDocument();
    expect(createSequenceMock).not.toHaveBeenCalled();
  });

  it('submits commandOutcome payload when required fields are complete', async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Flow Sequence' } });

    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-1' } });
    fireEvent.click(screen.getByText('Add to steps'));
    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-2' } });
    fireEvent.click(screen.getByText('Add to steps'));

    const conditionTypeFields = screen.getAllByLabelText('Condition Type');
    fireEvent.change(conditionTypeFields[1], { target: { value: 'commandOutcome' } });
    fireEvent.change(screen.getByLabelText('Step Ref'), { target: { value: 'step-1' } });
    fireEvent.change(screen.getByLabelText('Expected State'), { target: { value: 'success' } });

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(createSequenceMock).toHaveBeenCalledWith(expect.objectContaining({
        name: 'Flow Sequence',
        version: 1,
        steps: expect.arrayContaining([
          expect.objectContaining({
            stepId: 'step-2',
            condition: expect.objectContaining({
              type: 'commandOutcome',
              stepRef: 'step-1',
              expectedState: 'success'
            })
          })
        ])
      }));
    });
  });
});
