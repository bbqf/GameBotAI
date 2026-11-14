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
const updateSequenceMock = updateSequence as jest.MockedFunction<typeof updateSequence>;
const createSequenceMock = createSequence as jest.MockedFunction<typeof createSequence>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;

const LABEL = 'Ignore smart scheduling';
const HELP = /The bookings of this sequence always keep the requested time/i;

describe('SequencesPage ignore smart scheduling (feature 189)', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listCommandsMock.mockResolvedValue([{ id: 'cmd-1', name: 'Command One' }] as any);
    listSequencesMock.mockResolvedValue([
      { id: 'seq-critical', name: 'Critical Sequence', steps: [], ignoreSmartScheduling: true },
      { id: 'seq-plain', name: 'Plain Sequence', steps: [] }
    ] as any);
    updateSequenceMock.mockResolvedValue({ id: 'seq-critical', name: 'Critical Sequence', steps: [] } as any);
    createSequenceMock.mockResolvedValue({ id: 'seq-new', name: 'New Sequence', steps: [] } as any);
    getSequenceMock.mockResolvedValue({
      id: 'seq-critical',
      name: 'Critical Sequence',
      version: 2,
      ignoreSmartScheduling: true,
      steps: [
        { stepId: 'step-1', label: 'Command One', action: { type: 'command', parameters: { commandId: 'cmd-1' } }, condition: null }
      ]
    } as any);
  });

  it('shows the mark only for a sequence with the setting', async () => {
    render(<SequencesPage />);

    await screen.findByText('Critical Sequence');

    const marks = screen.getAllByText('Ignores smart scheduling');
    expect(marks).toHaveLength(1);
    expect(marks[0]).toHaveAttribute('title', expect.stringMatching(HELP));
  });

  it('has the checkbox off by default in the create form', async () => {
    render(<SequencesPage />);

    await screen.findByText('Critical Sequence');
    fireEvent.click(screen.getByRole('button', { name: /Create Sequence/i }));

    expect(await screen.findByLabelText(LABEL)).not.toBeChecked();
    expect(screen.getByText(HELP)).toBeInTheDocument();
  });

  it('loads the stored value in the edit form and sends it on save', async () => {
    render(<SequencesPage />);

    await screen.findByText('Critical Sequence');
    fireEvent.click(screen.getByRole('button', { name: 'Critical Sequence' }));

    await screen.findByText('Edit Sequence');
    expect(screen.getByLabelText(LABEL)).toBeChecked();

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(updateSequenceMock).toHaveBeenCalledWith('seq-critical', expect.objectContaining({ ignoreSmartScheduling: true }));
    });
  });

  it('sends false after the setting is turned off', async () => {
    render(<SequencesPage />);

    await screen.findByText('Critical Sequence');
    fireEvent.click(screen.getByRole('button', { name: 'Critical Sequence' }));

    await screen.findByText('Edit Sequence');
    fireEvent.click(screen.getByLabelText(LABEL));
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(updateSequenceMock).toHaveBeenCalledWith('seq-critical', expect.objectContaining({ ignoreSmartScheduling: false }));
    });
  });
});
