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

/**
 * A cut-down "PNS Pit Ensure Mining": a top-level tap, a tap inside an if-branch, and a
 * self-reschedule inside an if-branch whose payload carries a nested OCR region. Every one of these
 * shapes used to be rewritten into a command step on save, because the editor understood only
 * command / WaitForImage / reschedule-self steps at the top level and only command steps in bodies.
 */
const pitLikeSequence = () => ({
  id: 'seq-pit',
  name: 'PNS Pit Ensure Mining',
  version: 7,
  parameters: [
    { name: 'sectionRowY', type: 'number', default: '569', required: false, description: 'Y of the Enter Field row.' },
  ],
  steps: [
    {
      stepId: 'open-rail',
      label: 'Open the side rail',
      stepType: 'Action',
      primitiveAction: { type: 'tap', schemaVersion: 'v1', payload: { x: 37, y: 358 } },
    },
    {
      stepId: 'enter7',
      label: 'Enter field row 7',
      stepType: 'If',
      if: { condition: { type: 'imageVisible', imageId: 'rare-earth-title', minSimilarity: 0.8 } },
      body: [
        {
          stepId: 'enter7-tap',
          label: 'Tap Enter Field row7',
          stepType: 'Action',
          primitiveAction: { type: 'tap', schemaVersion: 'v1', payload: { x: 448, y: 569 } },
        },
      ],
    },
    {
      stepId: 'success',
      label: 'Reschedule on success',
      stepType: 'If',
      if: { condition: { type: 'imageVisible', imageId: 'gather-ok', minSimilarity: null } },
      body: [
        {
          stepId: 'resched-success',
          label: 'Reschedule from the OCR countdown',
          stepType: 'Action',
          primitiveAction: {
            type: 'reschedule-self',
            schemaVersion: '1',
            payload: {
              option: 'Timer',
              ocrOffset: { region: { x: 2, y: 228, width: 78, height: 18 }, fallback: '02:05:00' },
            },
          },
        },
      ],
    },
  ],
});

const openEditor = async () => {
  render(<SequencesPage />);
  await screen.findByText('PNS Pit Ensure Mining');
  fireEvent.click(screen.getByText('PNS Pit Ensure Mining'));
  await screen.findByText('Edit Sequence');
};

const savedSteps = () => (updateSequenceMock.mock.calls[0][1] as { steps: any[] }).steps;

describe('SequencesPage inline (non-command) action steps', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listCommandsMock.mockResolvedValue([{ id: 'cmd-back', name: 'Back' }] as any);
    listSequencesMock.mockResolvedValue([{ id: 'seq-pit', name: 'PNS Pit Ensure Mining', steps: [] }] as any);
    getSequenceMock.mockResolvedValue(pitLikeSequence() as any);
    updateSequenceMock.mockResolvedValue({ id: 'seq-pit', name: 'PNS Pit Ensure Mining', steps: [] } as any);
  });

  it('renders an inline action from its own payload rather than as a command step', async () => {
    await openEditor();

    // Top-level tap: both coordinates are editable fields, not a command dropdown.
    expect(screen.getAllByLabelText('x')[0]).toHaveValue('37');
    expect(screen.getAllByLabelText('y')[0]).toHaveValue('358');
    expect(screen.getAllByTestId('primitive-action-type').map((n) => n.textContent))
      .toEqual(expect.arrayContaining(['tap', 'reschedule-self']));
  });

  it('round-trips every step untouched when nothing is edited', async () => {
    await openEditor();

    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'PNS Pit Ensure Mining' } });
    fireEvent.click(screen.getByText('Save'));
    await waitFor(() => expect(updateSequenceMock).toHaveBeenCalled());

    const steps = savedSteps();
    expect(steps[0]).toMatchObject({
      stepId: 'open-rail',
      label: 'Open the side rail',
      primitiveAction: { type: 'tap', payload: { x: 37, y: 358 } },
    });

    // The if-branch tap survives with numeric coordinates, not as a command id of 'enter7-tap'.
    expect(steps[1].body[0]).toMatchObject({
      stepId: 'enter7-tap',
      label: 'Tap Enter Field row7',
      primitiveAction: { type: 'tap', payload: { x: 448, y: 569 } },
    });
    expect(steps[1].body[0].primitiveAction.payload).not.toHaveProperty('commandId');

    // The nested OCR region is carried through byte-for-byte; the editor never reshapes it.
    expect(steps[2].body[0].primitiveAction).toMatchObject({
      type: 'reschedule-self',
      payload: {
        option: 'Timer',
        ocrOffset: { region: { x: 2, y: 228, width: 78, height: 18 }, fallback: '02:05:00' },
      },
    });
  });

  it('replaces a hard-coded coordinate with a parameter reference', async () => {
    await openEditor();

    // The two taps both expose a 'y'; the second is the one inside the if-branch.
    const yFields = screen.getAllByLabelText('y');
    fireEvent.change(yFields[1], { target: { value: '{{sectionRowY}}' } });
    fireEvent.click(screen.getByText('Save'));
    await waitFor(() => expect(updateSequenceMock).toHaveBeenCalled());

    const tap = savedSteps()[1].body[0].primitiveAction;
    // A reference goes out as a string in the numeric slot — the runner parses these defensively.
    expect(tap.payload.y).toBe('{{sectionRowY}}');
    expect(tap.payload.x).toBe(448);
  });

  it('offers the sequence parameters and queue built-ins in an inline action field', async () => {
    await openEditor();

    fireEvent.click(screen.getAllByLabelText('Insert parameter into y')[1]);
    expect(screen.getByRole('option', { name: /sectionRowY/ })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /queue\.emulatorSerial/ })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('option', { name: /sectionRowY/ }));
    expect(screen.getAllByLabelText('y')[1]).toHaveValue('{{sectionRowY}}');
  });

  it('preserves step labels on steps the operator never opened', async () => {
    await openEditor();

    fireEvent.click(screen.getByText('Save'));
    await waitFor(() => expect(updateSequenceMock).toHaveBeenCalled());

    expect(savedSteps().map((s: any) => s.label))
      .toEqual(['Open the side rail', 'Enter field row 7', 'Reschedule on success']);
  });
});
