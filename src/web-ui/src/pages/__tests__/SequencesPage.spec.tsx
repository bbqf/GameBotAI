import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { SequencesPage } from '../SequencesPage';
import { listSequences, createSequence, getSequence, updateSequence } from '../../services/sequences';
import { listCommands } from '../../services/commands';

jest.mock('../../services/sequences');
jest.mock('../../services/commands');

const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;
const createSequenceMock = createSequence as jest.MockedFunction<typeof createSequence>;
const getSequenceMock = getSequence as jest.MockedFunction<typeof getSequence>;
const updateSequenceMock = updateSequence as jest.MockedFunction<typeof updateSequence>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;

describe('SequencesPage', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listSequencesMock.mockResolvedValue([] as any);
    listCommandsMock.mockResolvedValue([
      { id: 'c1', name: 'Command One' },
      { id: 'c2', name: 'Command Two' }
    ] as any);
  });

  it('creates a sequence with ordered steps', async () => {
    render(<SequencesPage />);

    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Seq A' } });

    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'c1' } });
    fireEvent.click(screen.getByText('Add to steps'));

    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'c2' } });
    fireEvent.click(screen.getByText('Add to steps'));

    const moveUpButtons = screen.getAllByLabelText('Move up');
    fireEvent.click(moveUpButtons[1]);

    createSequenceMock.mockResolvedValue({} as any);
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createSequenceMock).toHaveBeenCalledWith({ name: 'Seq A', steps: ['c2', 'c1'] }));
  });

  it('loads and updates an existing sequence', async () => {
    listSequencesMock.mockResolvedValue([{ id: 's1', name: 'Sequence 1', steps: ['c1', 'c2'] }] as any);
    getSequenceMock.mockResolvedValue({ id: 's1', name: 'Sequence 1', steps: ['c1', 'c2'] } as any);
    updateSequenceMock.mockResolvedValue({} as any);

    render(<SequencesPage />);

    await screen.findByText('Sequence 1');
    fireEvent.click(screen.getByText('Sequence 1'));

    await screen.findByText('Edit Sequence');

    const stepsSection = screen.getByRole('heading', { name: 'Steps', level: 3 }).closest('section')!;
    const deleteButtons = within(stepsSection).getAllByText('Delete');
    fireEvent.click(deleteButtons[0]);

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateSequenceMock).toHaveBeenCalledWith('s1', { name: 'Sequence 1', steps: ['c2'] }));
    await waitFor(() => expect(screen.queryByText('Edit Sequence')).not.toBeInTheDocument());
  });
});
