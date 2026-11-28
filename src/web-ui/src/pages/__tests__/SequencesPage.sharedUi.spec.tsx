import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { SequencesPage } from '../SequencesPage';
import { ApiError } from '../../lib/api';
import { createSequence, deleteSequence, getSequence, listSequences, updateSequence } from '../../services/sequences';
import { listCommands } from '../../services/commands';

// Keep the real conflict check. Replace only the calls to the server.
jest.mock('../../services/sequences', () => ({
  ...jest.requireActual('../../services/sequences'),
  listSequences: jest.fn(),
  createSequence: jest.fn(),
  getSequence: jest.fn(),
  updateSequence: jest.fn(),
  deleteSequence: jest.fn(),
}));
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
const createSequenceMock = createSequence as jest.MockedFunction<typeof createSequence>;
const getSequenceMock = getSequence as jest.MockedFunction<typeof getSequence>;
const updateSequenceMock = updateSequence as jest.MockedFunction<typeof updateSequence>;
const deleteSequenceMock = deleteSequence as jest.MockedFunction<typeof deleteSequence>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;

const savedSequence = {
  id: 'seq-a',
  name: 'Alpha',
  version: 3,
  steps: [{ stepId: 'step-1', label: 'Command One', action: { type: 'command', parameters: { commandId: 'c1' } }, condition: null }],
};

const openAlpha = async () => {
  render(<SequencesPage />);
  await screen.findByText('Alpha');
  fireEvent.click(screen.getByRole('button', { name: 'Edit Alpha' }));
  await screen.findByRole('button', { name: 'Back' });
};

describe('SequencesPage on the shared UI components (feature 207)', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listCommandsMock.mockResolvedValue([{ id: 'c1', name: 'Command One' }] as any);
    listSequencesMock.mockResolvedValue([
      { id: 'seq-a', name: 'Alpha', steps: [{}, {}, {}], excludeFromSuccessNotifications: true, ignoreSmartScheduling: true },
      { id: 'seq-b', name: 'Beta', steps: [] },
    ] as any);
    getSequenceMock.mockResolvedValue(savedSequence as any);
    updateSequenceMock.mockResolvedValue(savedSequence as any);
    createSequenceMock.mockResolvedValue({ id: 'seq-new', name: 'New', steps: [] } as any);
    deleteSequenceMock.mockResolvedValue(undefined as any);
  });

  describe('list', () => {
    it('shows one title, the create button, and the filter', async () => {
      render(<SequencesPage />);
      await screen.findByText('Alpha');
      expect(screen.getByRole('heading', { name: 'Sequences' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Create sequence' })).toBeInTheDocument();
      expect(screen.getByLabelText('Filter by name')).toBeInTheDocument();
    });

    it('shows both options in one text, and a dash for none', async () => {
      render(<SequencesPage />);
      await screen.findByText('Alpha');
      expect(screen.getByText('No success notifications, Ignores smart scheduling')).toBeInTheDocument();
      const betaRow = screen.getByTestId('sequence-row-seq-b');
      expect(within(betaRow).getByText('—')).toBeInTheDocument();
    });

    it('shows the number of steps', async () => {
      render(<SequencesPage />);
      await screen.findByText('Alpha');
      const alphaRow = screen.getByTestId('sequence-row-seq-a');
      expect(within(alphaRow).getByText('3')).toBeInTheDocument();
    });

    it('shows "No sequences." for an empty list', async () => {
      listSequencesMock.mockResolvedValue([] as any);
      render(<SequencesPage />);
      expect(await screen.findByText('No sequences.')).toBeInTheDocument();
    });

    it('shows the error with a retry button when the list does not load', async () => {
      listSequencesMock.mockRejectedValueOnce(new Error('Server is down'));
      render(<SequencesPage />);
      expect(await screen.findByText('Server is down')).toBeInTheDocument();
      fireEvent.click(screen.getByRole('button', { name: /try again/i }));
      await waitFor(() => expect(listSequencesMock).toHaveBeenCalledTimes(2));
    });

    it('filters by name', async () => {
      render(<SequencesPage />);
      await screen.findByText('Alpha');
      fireEvent.change(screen.getByLabelText('Filter by name'), { target: { value: 'bet' } });
      expect(screen.queryByText('Alpha')).not.toBeInTheDocument();
      expect(screen.getByText('Beta')).toBeInTheDocument();
    });
  });

  describe('form page', () => {
    it('shows the title "New sequence" for a new sequence, with no Delete button', async () => {
      render(<SequencesPage />);
      await screen.findByText('Alpha');
      fireEvent.click(screen.getByRole('button', { name: 'Create sequence' }));
      expect(await screen.findByRole('heading', { name: 'New sequence' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument();
    });

    it('shows the loaded name as the title, and the title does not change with the name field', async () => {
      await openAlpha();
      expect(screen.getByRole('heading', { name: 'Alpha' })).toBeInTheDocument();
      fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Changed' } });
      expect(screen.getByRole('heading', { name: 'Alpha' })).toBeInTheDocument();
    });

    it('shows Delete first and Save last in the footer in edit mode', async () => {
      await openAlpha();
      const footer = document.querySelector('.ui-footer-bar') as HTMLElement;
      const labels = within(footer).getAllByRole('button').map((b) => b.textContent);
      expect(labels).toEqual(['Delete', 'Cancel', 'Save']);
    });

    it('puts Step through in the page header, with the reason in the message area', async () => {
      render(<SequencesPage />);
      await screen.findByText('Alpha');
      fireEvent.click(screen.getByRole('button', { name: 'Create sequence' }));
      const header = (await screen.findByRole('heading', { name: 'New sequence' })).closest('header') as HTMLElement;
      const button = within(header).getByRole('button', { name: 'Step through' });
      expect(button).toBeDisabled();
      expect(button).toHaveAttribute('aria-describedby', 'step-through-reason');
      expect(document.getElementById('step-through-reason')).toHaveTextContent('Save the sequence first');
    });

    it('creates with no optional members when parameters and options are off', async () => {
      render(<SequencesPage />);
      await screen.findByText('Alpha');
      fireEvent.click(screen.getByRole('button', { name: 'Create sequence' }));
      fireEvent.change(await screen.findByLabelText('Name *'), { target: { value: 'Plain' } });
      fireEvent.click(screen.getByRole('button', { name: 'Save' }));
      await waitFor(() => expect(createSequenceMock).toHaveBeenCalledTimes(1));
      const payload = createSequenceMock.mock.calls[0][0] as Record<string, unknown>;
      expect(payload).not.toHaveProperty('parameters');
      expect(payload).not.toHaveProperty('excludeFromSuccessNotifications');
      expect(payload).not.toHaveProperty('ignoreSmartScheduling');
      expect(payload).not.toHaveProperty('onTimeLimit');
      expect(await screen.findByText('Sequence created.')).toBeInTheDocument();
      expect(screen.getByRole('heading', { name: 'Sequences' })).toBeInTheDocument();
    });

    it('creates with the options on and the time limit "end"', async () => {
      render(<SequencesPage />);
      await screen.findByText('Alpha');
      fireEvent.click(screen.getByRole('button', { name: 'Create sequence' }));
      fireEvent.change(await screen.findByLabelText('Name *'), { target: { value: 'Full' } });
      fireEvent.click(screen.getByLabelText('Exclude from success notifications'));
      fireEvent.click(screen.getByLabelText('Ignore smart scheduling'));
      fireEvent.change(document.getElementById('sequence-on-time-limit') as HTMLElement, { target: { value: 'end' } });
      fireEvent.click(screen.getByRole('button', { name: 'Save' }));
      await waitFor(() => expect(createSequenceMock).toHaveBeenCalledTimes(1));
      expect(createSequenceMock.mock.calls[0][0]).toEqual(expect.objectContaining({
        excludeFromSuccessNotifications: true,
        ignoreSmartScheduling: true,
        onTimeLimit: 'end',
      }));
    });

    it('always sends the four members on update, also when they are empty and off', async () => {
      await openAlpha();
      fireEvent.click(screen.getByRole('button', { name: 'Save' }));
      await waitFor(() => expect(updateSequenceMock).toHaveBeenCalledTimes(1));
      const [id, payload] = updateSequenceMock.mock.calls[0] as [string, Record<string, unknown>];
      expect(id).toBe('seq-a');
      expect(payload).toEqual(expect.objectContaining({
        version: 3,
        parameters: [],
        excludeFromSuccessNotifications: false,
        ignoreSmartScheduling: false,
        onTimeLimit: 'fail',
      }));
      expect(await screen.findByText('Sequence updated.')).toBeInTheDocument();
    });

    it('shows the conflict text and loads the sequence again', async () => {
      updateSequenceMock.mockRejectedValueOnce(new ApiError(409, 'Conflict', undefined, { sequenceId: 'seq-a', currentVersion: 4 }));
      await openAlpha();
      expect(getSequenceMock).toHaveBeenCalledTimes(1);
      fireEvent.click(screen.getByRole('button', { name: 'Save' }));
      expect(await screen.findByText(/Sequence has changed on the server \(version 4\)\. Reloaded latest version\./)).toBeInTheDocument();
      await waitFor(() => expect(getSequenceMock).toHaveBeenCalledTimes(2));
    });

    it('goes back to the list with the Back button', async () => {
      await openAlpha();
      fireEvent.click(screen.getByRole('button', { name: 'Back' }));
      expect(await screen.findByRole('heading', { name: 'Sequences' })).toBeInTheDocument();
    });
  });

  describe('delete', () => {
    const openRowMenu = async (name: string) => {
      render(<SequencesPage />);
      await screen.findByText(name);
      fireEvent.click(screen.getByRole('button', { name: `More actions for ${name}` }));
      fireEvent.click(await screen.findByRole('menuitem', { name: /Delete/ }));
    };

    it('opens the shared dialog with the focus on Cancel, and deletes after Delete', async () => {
      await openRowMenu('Alpha');
      const dialog = await screen.findByRole('dialog');
      expect(within(dialog).getByText('Delete sequence Alpha?')).toBeInTheDocument();
      expect(within(dialog).getByText('You cannot undo this.')).toBeInTheDocument();
      expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus();
      fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
      await waitFor(() => expect(deleteSequenceMock).toHaveBeenCalledTimes(1));
      expect(deleteSequenceMock).toHaveBeenCalledWith('seq-a');
      expect(await screen.findByText('Sequence deleted.')).toBeInTheDocument();
      await waitFor(() => expect(listSequencesMock).toHaveBeenCalledTimes(2));
    });

    it('shows the references for a 409, turns Delete off, and Cancel closes the dialog', async () => {
      deleteSequenceMock.mockRejectedValueOnce(new ApiError(409, 'In use', undefined, undefined, { queues: [{ id: 'q1', name: 'Queue One' }] }));
      await openRowMenu('Alpha');
      const dialog = await screen.findByRole('dialog');
      fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
      expect(await within(dialog).findByText('Referenced by:')).toBeInTheDocument();
      expect(within(dialog).getByText(/Queue One/)).toBeInTheDocument();
      expect(within(dialog).getByRole('button', { name: 'Delete' })).toBeDisabled();
      fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    });

    it('shows another error in the message area', async () => {
      deleteSequenceMock.mockRejectedValueOnce(new Error('Server error'));
      await openRowMenu('Alpha');
      const dialog = await screen.findByRole('dialog');
      fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
      expect(await screen.findByText('Server error')).toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('deletes from the footer of the edit page and shows the list', async () => {
      await openAlpha();
      const footer = document.querySelector('.ui-footer-bar') as HTMLElement;
      fireEvent.click(within(footer).getByRole('button', { name: 'Delete' }));
      const dialog = await screen.findByRole('dialog');
      expect(within(dialog).getByText('Delete sequence Alpha?')).toBeInTheDocument();
      fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
      await waitFor(() => expect(deleteSequenceMock).toHaveBeenCalledWith('seq-a'));
      expect(await screen.findByText('Sequence deleted.')).toBeInTheDocument();
      expect(screen.getByRole('heading', { name: 'Sequences' })).toBeInTheDocument();
    });
  });
});
