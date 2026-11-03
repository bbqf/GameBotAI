import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { SequencesPage } from '../SequencesPage';
import { listSequences, createSequence, getSequence, updateSequence } from '../../services/sequences';
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
const getSequenceMock = getSequence as jest.MockedFunction<typeof getSequence>;
const updateSequenceMock = updateSequence as jest.MockedFunction<typeof updateSequence>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;

describe('SequencesPage WaitForImage until option (feature 175)', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listSequencesMock.mockResolvedValue([] as any);
    listCommandsMock.mockResolvedValue([{ id: 'c1', name: 'Command One' }] as any);
  });

  const addWaitStep = async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());
    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Wait Seq' } });
    fireEvent.click(screen.getByText('Add wait step'));
    fireEvent.change(screen.getByLabelText('Wait image ID'), { target: { value: 'toast' } });
    fireEvent.change(screen.getByLabelText('Wait timeout (ms)'), { target: { value: '6000' } });
  };

  it('shows the Wait until select, and shows Absent captures only for absent', async () => {
    await addWaitStep();

    const select = screen.getByLabelText('Wait until') as HTMLSelectElement;
    expect(select.value).toBe('present');
    expect(screen.queryByLabelText('Absent captures')).toBeNull();

    fireEvent.change(select, { target: { value: 'absent' } });
    expect(screen.getByLabelText('Absent captures')).toBeInTheDocument();

    fireEvent.change(select, { target: { value: 'present' } });
    expect(screen.queryByLabelText('Absent captures')).toBeNull();
  });

  it('leaves both fields out of the save body in present mode', async () => {
    await addWaitStep();
    createSequenceMock.mockResolvedValue({} as any);
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createSequenceMock).toHaveBeenCalled());
    const payload = (createSequenceMock.mock.calls[0][0] as any).steps[0].primitiveAction.payload;
    expect(payload).not.toHaveProperty('until');
    expect(payload).not.toHaveProperty('absentCaptures');
  });

  it('saves until absent and absentCaptures', async () => {
    await addWaitStep();
    fireEvent.change(screen.getByLabelText('Wait until'), { target: { value: 'absent' } });
    fireEvent.change(screen.getByLabelText('Absent captures'), { target: { value: '3' } });
    createSequenceMock.mockResolvedValue({} as any);
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createSequenceMock).toHaveBeenCalled());
    const payload = (createSequenceMock.mock.calls[0][0] as any).steps[0].primitiveAction.payload;
    expect(payload).toMatchObject({ timeoutMs: 6000, until: 'absent', absentCaptures: 3 });
  });

  it('omits absentCaptures when the field is empty', async () => {
    await addWaitStep();
    fireEvent.change(screen.getByLabelText('Wait until'), { target: { value: 'absent' } });
    createSequenceMock.mockResolvedValue({} as any);
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createSequenceMock).toHaveBeenCalled());
    const payload = (createSequenceMock.mock.calls[0][0] as any).steps[0].primitiveAction.payload;
    expect(payload.until).toBe('absent');
    expect(payload).not.toHaveProperty('absentCaptures');
  });

  it('does not save an absentCaptures value below 1', async () => {
    await addWaitStep();
    fireEvent.change(screen.getByLabelText('Wait until'), { target: { value: 'absent' } });
    fireEvent.change(screen.getByLabelText('Absent captures'), { target: { value: '0' } });
    fireEvent.click(screen.getByText('Save'));

    // The save is blocked by the page check, so no request goes out.
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(createSequenceMock).not.toHaveBeenCalled();
  });

  it('loads and saves a step with until absent', async () => {
    listSequencesMock.mockResolvedValue([{ id: 'seq-absent', name: 'Absent Sequence', steps: [] }] as any);
    getSequenceMock.mockResolvedValue({
      id: 'seq-absent',
      name: 'Absent Sequence',
      version: 2,
      steps: [
        {
          stepId: 'step-1',
          primitiveAction: {
            type: 'WaitForImage',
            schemaVersion: 'v1',
            payload: {
              detectionTarget: { referenceImageId: 'toast', confidence: 0.8 },
              timeoutMs: 6000,
              until: 'absent',
              absentCaptures: 3
            }
          },
          condition: null
        }
      ]
    } as any);
    updateSequenceMock.mockResolvedValue({} as any);

    render(<SequencesPage />);
    await screen.findByText('Absent Sequence');
    fireEvent.click(screen.getByText('Absent Sequence'));
    await screen.findByText('Edit Sequence');

    expect(screen.getByText('Wait until image is gone: toast')).toBeInTheDocument();
    expect((screen.getByLabelText('Wait until') as HTMLSelectElement).value).toBe('absent');
    expect((screen.getByLabelText('Absent captures') as HTMLInputElement).value).toBe('3');

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateSequenceMock).toHaveBeenCalled());
    const payload = (updateSequenceMock.mock.calls[0][1] as any).steps[0].primitiveAction.payload;
    expect(payload).toMatchObject({ timeoutMs: 6000, until: 'absent', absentCaptures: 3 });
  });
});
