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

describe('SequencesPage conditional flow authoring', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listSequencesMock.mockResolvedValue([] as any);
    listCommandsMock.mockResolvedValue([
      { id: 'cmd-1', name: 'Command One' },
      { id: 'cmd-2', name: 'Command Two' }
    ] as any);
    createSequenceMock.mockResolvedValue({ id: 'seq-1', name: 'Flow Sequence', steps: [] } as any);
  });

  it('shows validation when a condition is missing false branch target', async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Flow Sequence' } });

    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-1' } });
    fireEvent.click(screen.getByText('Add to steps'));
    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-2' } });
    fireEvent.click(screen.getByText('Add to steps'));

    fireEvent.click(screen.getByLabelText('Enable conditional flow'));
    fireEvent.change(screen.getByLabelText('Entry Step'), { target: { value: 'cmd-1' } });

    fireEvent.click(screen.getByText('Add Condition Step'));
    fireEvent.change(screen.getByLabelText('Condition Step Id'), { target: { value: 'cond-1' } });
    fireEvent.change(screen.getByLabelText('Operand Target (0)'), { target: { value: 'cmd-1' } });
    fireEvent.change(screen.getByLabelText('True Target'), { target: { value: 'cmd-2' } });

    fireEvent.click(screen.getByText('Save'));

    expect(await screen.findByText(/unresolved target step/i)).toBeInTheDocument();
    expect(createSequenceMock).not.toHaveBeenCalled();
  });

  it('submits conditional flow payload when branch targets are complete', async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Flow Sequence' } });

    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-1' } });
    fireEvent.click(screen.getByText('Add to steps'));
    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-2' } });
    fireEvent.click(screen.getByText('Add to steps'));

    fireEvent.click(screen.getByLabelText('Enable conditional flow'));
    fireEvent.change(screen.getByLabelText('Entry Step'), { target: { value: 'cmd-1' } });

    fireEvent.click(screen.getByText('Add Condition Step'));
    fireEvent.change(screen.getByLabelText('Condition Step Id'), { target: { value: 'cond-1' } });
    fireEvent.change(screen.getByLabelText('Operand Target (0)'), { target: { value: 'cmd-1' } });
    fireEvent.change(screen.getByLabelText('True Target'), { target: { value: 'cmd-2' } });
    fireEvent.change(screen.getByLabelText('False Target'), { target: { value: 'cmd-1' } });

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(createSequenceMock).toHaveBeenCalledWith(expect.objectContaining({
        name: 'Flow Sequence',
        entryStepId: 'cmd-1'
      }));
    });
  });
});
