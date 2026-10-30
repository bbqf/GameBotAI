import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { SequencesPage } from '../SequencesPage';
import { createSequence, getSequence, listSequences, updateSequence } from '../../services/sequences';
import { listCommands } from '../../services/commands';

jest.mock('../../services/sequences');
jest.mock('../../services/commands');

jest.mock('../../components/images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ id, label, value, onChange, disabled }: {
    id?: string; label?: string; value: string; onChange: (v: string) => void; disabled?: boolean;
  }) => (
    <>
      {label && <label htmlFor={id}>{label}</label>}
      <input id={id} value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)} />
    </>
  ),
}));

const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;
const getSequenceMock = getSequence as jest.MockedFunction<typeof getSequence>;
const createSequenceMock = createSequence as jest.MockedFunction<typeof createSequence>;
const updateSequenceMock = updateSequence as jest.MockedFunction<typeof updateSequence>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;

const LABEL = 'When the time limit is reached';

describe('SequencesPage onTimeLimit', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listCommandsMock.mockResolvedValue([{ id: 'cmd-1', name: 'Command One' }] as any);
    listSequencesMock.mockResolvedValue([{ id: 'seq-end', name: 'Planned Stop', steps: [], onTimeLimit: 'end' }] as any);
    createSequenceMock.mockResolvedValue({} as any);
    updateSequenceMock.mockResolvedValue({ id: 'seq-end', name: 'Planned Stop', steps: [] } as any);
    getSequenceMock.mockResolvedValue({
      id: 'seq-end',
      name: 'Planned Stop',
      version: 3,
      onTimeLimit: 'end',
      steps: [
        { stepId: 'step-1', label: 'Command One', action: { type: 'command', parameters: { commandId: 'cmd-1' } }, condition: null }
      ]
    } as any);
  });

  const openCreateWithOneStep = async (name: string) => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());
    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: name } });
    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-1' } });
    fireEvent.click(screen.getByText('Add to steps'));
  };

  it('shows the select in the create form with fail as the default', async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());
    fireEvent.click(screen.getByText('Create Sequence'));

    const select = screen.getByLabelText(LABEL) as HTMLSelectElement;
    expect(select.value).toBe('fail');
    expect(screen.getByRole('option', { name: 'Fail the run' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'End the run as success' })).toBeInTheDocument();
  });

  it('does not send onTimeLimit on create when the value is fail', async () => {
    await openCreateWithOneStep('Seq Fail');

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createSequenceMock).toHaveBeenCalled());
    expect(createSequenceMock.mock.calls[0][0]).not.toHaveProperty('onTimeLimit');
  });

  it('sends onTimeLimit end on create when it is selected', async () => {
    await openCreateWithOneStep('Seq End');

    fireEvent.change(screen.getByLabelText(LABEL), { target: { value: 'end' } });
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createSequenceMock).toHaveBeenCalledWith(expect.objectContaining({ onTimeLimit: 'end' })));
  });

  it('loads the saved value in the edit form and always sends the value on save', async () => {
    render(<SequencesPage />);
    await screen.findByText('Planned Stop');
    fireEvent.click(screen.getByRole('button', { name: 'Planned Stop' }));

    await screen.findByText('Edit Sequence');
    expect((screen.getByLabelText(LABEL) as HTMLSelectElement).value).toBe('end');

    fireEvent.click(screen.getByText('Save'));
    await waitFor(() => expect(updateSequenceMock).toHaveBeenCalledWith('seq-end', expect.objectContaining({ onTimeLimit: 'end' })));
  });

  it('sends fail on update after the author switches back', async () => {
    render(<SequencesPage />);
    await screen.findByText('Planned Stop');
    fireEvent.click(screen.getByRole('button', { name: 'Planned Stop' }));

    await screen.findByText('Edit Sequence');
    fireEvent.change(screen.getByLabelText(LABEL), { target: { value: 'fail' } });
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateSequenceMock).toHaveBeenCalledWith('seq-end', expect.objectContaining({ onTimeLimit: 'fail' })));
  });
});
