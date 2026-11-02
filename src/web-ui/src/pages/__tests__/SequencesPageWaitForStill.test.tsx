import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { SequencesPage } from '../SequencesPage';
import { createSequence, listSequences } from '../../services/sequences';
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
const createSequenceMock = createSequence as jest.MockedFunction<typeof createSequence>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;

describe('SequencesPage WaitForStill step (feature 174)', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listSequencesMock.mockResolvedValue([] as any);
    listCommandsMock.mockResolvedValue([{ id: 'cmd-home', name: 'Home' }] as any);
    createSequenceMock.mockResolvedValue({ id: 'seq-still', name: 'Still', steps: [] } as any);
  });

  it('adds a WaitForStill step with the default values', async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Still' } });
    fireEvent.click(screen.getByText('Add wait for still step'));

    expect(screen.getByTestId('wait-for-still-fields')).toBeInTheDocument();
    expect(screen.getByLabelText('stableMs')).toHaveValue('300');
    expect(screen.getByLabelText('maxDiff')).toHaveValue('0.01');
    expect(screen.getByLabelText('timeoutMs')).toHaveValue('5000');

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(createSequenceMock).toHaveBeenCalledWith(expect.objectContaining({
        steps: [
          expect.objectContaining({
            primitiveAction: expect.objectContaining({
              type: 'WaitForStill',
              payload: { stableMs: 300, maxDiff: 0.01, timeoutMs: 5000 },
            }),
          }),
        ],
      }));
    });
  });
});
