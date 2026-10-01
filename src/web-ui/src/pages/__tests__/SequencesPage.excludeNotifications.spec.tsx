import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { SequencesPage } from '../SequencesPage';
import { getSequence, listSequences, updateSequence } from '../../services/sequences';
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
const updateSequenceMock = updateSequence as jest.MockedFunction<typeof updateSequence>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;

const HELP = /still sends failure, cancelled and recovered messages/i;

describe('SequencesPage exclude from success notifications', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listCommandsMock.mockResolvedValue([{ id: 'cmd-1', name: 'Command One' }] as any);
    listSequencesMock.mockResolvedValue([
      { id: 'seq-helper', name: 'Helper Sequence', steps: [], excludeFromSuccessNotifications: true },
      { id: 'seq-plain', name: 'Plain Sequence', steps: [] }
    ] as any);
    updateSequenceMock.mockResolvedValue({ id: 'seq-helper', name: 'Helper Sequence', steps: [] } as any);
    getSequenceMock.mockResolvedValue({
      id: 'seq-helper',
      name: 'Helper Sequence',
      version: 2,
      excludeFromSuccessNotifications: true,
      steps: [
        { stepId: 'step-1', label: 'Command One', action: { type: 'command', parameters: { commandId: 'cmd-1' } }, condition: null }
      ]
    } as any);
  });

  it('shows the badge only for flagged sequences', async () => {
    render(<SequencesPage />);

    await screen.findByText('Helper Sequence');

    const badges = screen.getAllByText('No success notifications');
    expect(badges).toHaveLength(1);
    expect(badges[0]).toHaveAttribute('title', expect.stringMatching(HELP));
  });

  it('shows the saved state in the edit form and sends the flag on save', async () => {
    render(<SequencesPage />);

    await screen.findByText('Helper Sequence');
    fireEvent.click(screen.getByRole('button', { name: 'Helper Sequence' }));

    await screen.findByText('Edit Sequence');
    const checkbox = screen.getByLabelText('Exclude from success notifications');
    expect(checkbox).toBeChecked();
    expect(screen.getByText(HELP)).toBeInTheDocument();

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(updateSequenceMock).toHaveBeenCalledWith('seq-helper', expect.objectContaining({
        version: 2,
        excludeFromSuccessNotifications: true
      }));
    });
  });

  it('sends false after the option is turned off', async () => {
    render(<SequencesPage />);

    await screen.findByText('Helper Sequence');
    fireEvent.click(screen.getByRole('button', { name: 'Helper Sequence' }));

    await screen.findByText('Edit Sequence');
    fireEvent.click(screen.getByLabelText('Exclude from success notifications'));
    expect(screen.getByLabelText('Exclude from success notifications')).not.toBeChecked();
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(updateSequenceMock).toHaveBeenCalledWith('seq-helper', expect.objectContaining({
        excludeFromSuccessNotifications: false
      }));
    });
  });
});
