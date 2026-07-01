import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { SequencesPage } from '../SequencesPage';
import { createSequence, getSequence, listSequences } from '../../services/sequences';
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
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;

describe('SequencesPage self-reschedule action (feature 065)', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listSequencesMock.mockResolvedValue([] as any);
    listCommandsMock.mockResolvedValue([{ id: 'cmd-home', name: 'Home' }] as any);
    createSequenceMock.mockResolvedValue({ id: 'seq-reschedule', name: 'Reschedule', steps: [] } as any);
  });

  it('T024: authors a reschedule-self action inside an IF branch and serializes it', async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Reschedule' } });

    // A prior command step to gate against.
    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'cmd-home' } });
    fireEvent.click(screen.getByText('Add to steps'));

    // The self-reschedule action step.
    fireEvent.click(screen.getByText('Add reschedule step'));

    // Default option is Once Per Run.
    expect(screen.getByLabelText('Reschedule option')).toHaveValue('OncePerRun');

    // Place it under an IF: gate on the prior step's outcome.
    const conditionTypes = screen.getAllByLabelText('Condition Type');
    fireEvent.change(conditionTypes[1], { target: { value: 'commandOutcome' } });
    fireEvent.change(screen.getByLabelText('Step Ref'), { target: { value: 'step-1' } });

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(createSequenceMock).toHaveBeenCalledWith(expect.objectContaining({
        steps: expect.arrayContaining([
          expect.objectContaining({
            stepId: 'step-2',
            primitiveAction: expect.objectContaining({
              type: 'reschedule-self',
              payload: expect.objectContaining({ option: 'OncePerRun' })
            }),
            condition: expect.objectContaining({ type: 'commandOutcome', stepRef: 'step-1' })
          })
        ])
      }));
    });
  });

  it('T035: option dropdown offers all four options and shows Timer inputs', async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Reschedule timer' } });
    fireEvent.click(screen.getByText('Add reschedule step'));

    const optionSelect = screen.getByLabelText('Reschedule option');
    const optionLabels = Array.from(optionSelect.querySelectorAll('option')).map((o) => o.textContent);
    expect(optionLabels).toEqual(['At Queue Start', 'Once Per Run', 'Timer', 'After Every Step']);

    // Timer mode is hidden until Timer is selected.
    expect(screen.queryByLabelText('Timer mode')).not.toBeInTheDocument();

    fireEvent.change(optionSelect, { target: { value: 'Timer' } });
    expect(screen.getByLabelText('Timer mode')).toBeInTheDocument();
    // Default mode is relative offset.
    expect(screen.getByLabelText('Relative offset (HH:mm:ss)')).toBeInTheDocument();

    // Switching to time-of-day reveals the time input (mirrors the queue-template editor).
    fireEvent.change(screen.getByLabelText('Timer mode'), { target: { value: 'timeOfDay' } });
    expect(screen.getByLabelText('Time of day')).toBeInTheDocument();
  });

  it('edit mode also exposes "Add reschedule step" and round-trips an existing reschedule step', async () => {
    listSequencesMock.mockResolvedValue([{ id: 'seq-1', name: 'Existing', steps: [] }] as any);
    getSequenceMock.mockResolvedValue({
      id: 'seq-1',
      name: 'Existing',
      version: 2,
      steps: [
        { stepId: 'step-1', label: 'Home', primitiveAction: { type: 'command', schemaVersion: 'v1', payload: { commandId: 'cmd-home' } }, condition: null },
        {
          stepId: 'step-2',
          stepType: 'Action',
          primitiveAction: { type: 'reschedule-self', schemaVersion: '1', payload: { option: 'Timer', timerRelativeOffset: '00:15:00' } },
          condition: { type: 'commandOutcome', stepRef: 'step-1', expectedState: 'success' }
        }
      ]
    } as any);

    render(<SequencesPage />);
    await screen.findByText('Existing');
    fireEvent.click(screen.getByText('Existing'));

    // Regression: the edit form must offer the reschedule control too (not only the create form).
    expect(await screen.findByText('Add reschedule step')).toBeInTheDocument();

    // The existing reschedule step loaded its option and timer field back into the editor.
    expect(screen.getByLabelText('Reschedule option')).toHaveValue('Timer');
    expect(screen.getByLabelText('Relative offset (HH:mm:ss)')).toHaveValue('00:15:00');
  });

  it('T035: Timer relative offset serializes the single timer field', async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Reschedule timer' } });
    fireEvent.click(screen.getByText('Add reschedule step'));
    fireEvent.change(screen.getByLabelText('Reschedule option'), { target: { value: 'Timer' } });
    fireEvent.change(screen.getByLabelText('Relative offset (HH:mm:ss)'), { target: { value: '00:05:00' } });

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => {
      expect(createSequenceMock).toHaveBeenCalledWith(expect.objectContaining({
        steps: expect.arrayContaining([
          expect.objectContaining({
            primitiveAction: expect.objectContaining({
              type: 'reschedule-self',
              payload: expect.objectContaining({ option: 'Timer', timerRelativeOffset: '00:05:00' })
            })
          })
        ])
      }));
    });
  });
});
