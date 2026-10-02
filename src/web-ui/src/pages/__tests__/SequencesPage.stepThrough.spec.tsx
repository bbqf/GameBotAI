import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { SequencesPage } from '../SequencesPage';
import { getSequence, listSequences, updateSequence } from '../../services/sequences';
import { listCommands } from '../../services/commands';

jest.mock('../../services/sequences');
jest.mock('../../services/commands');

// The panel has its own tests. This page test only checks when the page offers it.
jest.mock('../../components/stepthrough/StepThroughPanel', () => ({
  StepThroughPanel: ({ sequenceId, sequenceName, onClose }: { sequenceId: string; sequenceName: string; onClose: () => void }) => (
    <div data-testid="step-through-panel">
      panel for {sequenceId} / {sequenceName}
      <button type="button" onClick={onClose}>Close panel</button>
    </div>
  )
}));

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

const SAVE_FIRST = /Save the sequence first/;
const UNSAVED = /Save your changes first/;

describe('SequencesPage step-through entry', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listCommandsMock.mockResolvedValue([{ id: 'cmd-1', name: 'Command One' }] as any);
    listSequencesMock.mockResolvedValue([{ id: 'seq-1', name: 'Daily', steps: [] }] as any);
    updateSequenceMock.mockResolvedValue({ id: 'seq-1', name: 'Daily', steps: [] } as any);
    getSequenceMock.mockResolvedValue({
      id: 'seq-1',
      name: 'Daily',
      version: 3,
      steps: [{ stepId: 'step-1', label: 'Command One', action: { type: 'command', parameters: { commandId: 'cmd-1' } }, condition: null }]
    } as any);
  });

  it('disables the entry with a reason for a new sequence that is not saved', async () => {
    render(<SequencesPage />);
    await screen.findByText('Daily');

    fireEvent.click(screen.getByRole('button', { name: 'Create Sequence' }));

    const button = await screen.findByTestId('step-through-button');
    expect(button).toBeDisabled();
    expect(screen.getByText(SAVE_FIRST)).toBeInTheDocument();
    expect(button).toHaveAttribute('aria-describedby', 'step-through-reason');
  });

  it('enables the entry for a saved sequence with no edits', async () => {
    render(<SequencesPage />);
    await screen.findByText('Daily');

    fireEvent.click(screen.getByRole('button', { name: 'Daily' }));
    await screen.findByText('Edit Sequence');

    await waitFor(() => expect(screen.getByTestId('step-through-button')).toBeEnabled());
    expect(screen.queryByText(SAVE_FIRST)).toBeNull();
    expect(screen.queryByText(UNSAVED)).toBeNull();
  });

  it('disables the entry again with a reason when the author edits the form', async () => {
    render(<SequencesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByRole('button', { name: 'Daily' }));
    await screen.findByText('Edit Sequence');
    await waitFor(() => expect(screen.getByTestId('step-through-button')).toBeEnabled());

    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Daily edited' } });

    expect(screen.getByTestId('step-through-button')).toBeDisabled();
    expect(screen.getByText(UNSAVED)).toBeInTheDocument();
  });

  it('opens the panel for the saved sequence and closes it', async () => {
    render(<SequencesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByRole('button', { name: 'Daily' }));
    await screen.findByText('Edit Sequence');
    await waitFor(() => expect(screen.getByTestId('step-through-button')).toBeEnabled());

    fireEvent.click(screen.getByTestId('step-through-button'));

    expect(await screen.findByTestId('step-through-panel')).toHaveTextContent('panel for seq-1 / Daily');
    expect(screen.getByTestId('step-through-button')).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Close panel' }));
    expect(screen.queryByTestId('step-through-panel')).toBeNull();
    expect(screen.getByTestId('step-through-button')).toBeEnabled();
  });

  it('closes the panel when the author goes back to the list', async () => {
    render(<SequencesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByRole('button', { name: 'Daily' }));
    await screen.findByText('Edit Sequence');
    await waitFor(() => expect(screen.getByTestId('step-through-button')).toBeEnabled());
    fireEvent.click(screen.getByTestId('step-through-button'));
    await screen.findByTestId('step-through-panel');

    fireEvent.click(screen.getByRole('button', { name: '← Back to list' }));

    expect(screen.queryByTestId('step-through-panel')).toBeNull();
  });
});
