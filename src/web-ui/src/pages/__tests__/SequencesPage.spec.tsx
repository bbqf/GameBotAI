import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { SequencesPage } from '../SequencesPage';
import { listSequences, createSequence, getSequence, updateSequence } from '../../services/sequences';
import { listCommands } from '../../services/commands';

jest.mock('../../services/sequences');
jest.mock('../../services/commands');

jest.mock('../../components/images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ id, label, value, onChange, disabled, 'data-testid': testId }: {
    id?: string; label?: string; value: string; onChange: (v: string) => void; disabled?: boolean;
    'data-testid'?: string;
  }) => (
    <>
      {label && <label htmlFor={id}>{label}</label>}
      <input id={id} data-testid={testId} value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)} />
    </>
  ),
}));

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

  it('shows empty-state guidance when no sequences exist', async () => {
    render(<SequencesPage />);

    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());
    await screen.findByText('No sequences found.');
    expect(screen.getByText(/No sequences yet\. Create your first sequence/i)).toBeInTheDocument();
  });

  it('creates a sequence with multiple steps', async () => {
    render(<SequencesPage />);

    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Seq A' } });

    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'c1' } });
    fireEvent.click(screen.getByText('Add to steps'));

    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'c2' } });
    fireEvent.click(screen.getByText('Add to steps'));

    createSequenceMock.mockResolvedValue({} as any);
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createSequenceMock).toHaveBeenCalledWith({
      name: 'Seq A',
      version: 1,
      interStepDelayRangeMs: null,
      steps: [
        {
          stepId: 'step-1',
          stepType: 'Action',
          primitiveAction: { type: 'command', schemaVersion: 'v1', payload: { commandId: 'c1' } },
          condition: null
        },
        {
          stepId: 'step-2',
          stepType: 'Action',
          primitiveAction: { type: 'command', schemaVersion: 'v1', payload: { commandId: 'c2' } },
          condition: null
        }
      ]
    }));
  });

  it('loads and updates an existing sequence', async () => {
    listSequencesMock.mockResolvedValue([{ id: 's1', name: 'Sequence 1', steps: ['c1', 'c2'] }] as any);
    getSequenceMock.mockResolvedValue({
      id: 's1',
      name: 'Sequence 1',
      version: 1,
      steps: [
        {
          stepId: 'step-1',
          action: { type: 'command', parameters: { commandId: 'c1' } },
          condition: null
        },
        {
          stepId: 'step-2',
          action: { type: 'command', parameters: { commandId: 'c2' } },
          condition: null
        }
      ]
    } as any);
    updateSequenceMock.mockResolvedValue({} as any);

    render(<SequencesPage />);

    await screen.findByText('Sequence 1');
    fireEvent.click(screen.getByText('Sequence 1'));

    await screen.findByText('Edit Sequence');

    const stepsSection = screen.getByRole('heading', { name: 'Steps', level: 3 }).closest('section')!;
    const deleteButtons = within(stepsSection).getAllByText('Delete');
    fireEvent.click(deleteButtons[0]);

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateSequenceMock).toHaveBeenCalledWith('s1', {
      name: 'Sequence 1',
      version: 1,
      interStepDelayRangeMs: null,
      steps: [
        {
          stepId: 'step-2',
          stepType: 'Action',
          primitiveAction: { type: 'command', schemaVersion: 'v1', payload: { commandId: 'c2' } },
          condition: null
        }
      ]
    }));
    await waitFor(() => expect(screen.queryByText('Edit Sequence')).not.toBeInTheDocument());
  });

  it('creates a sequence with a wait-for-image step', async () => {
    render(<SequencesPage />);

    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Wait Seq' } });
    // Wait steps are now added first, then configured inline on the step itself.
    fireEvent.click(screen.getByText('Add wait step'));
    fireEvent.change(screen.getByLabelText('Wait image ID'), { target: { value: 'mail_icon' } });
    fireEvent.change(screen.getByLabelText('Wait confidence (0-1)'), { target: { value: '0.88' } });
    fireEvent.change(screen.getByLabelText('Wait timeout (ms)'), { target: { value: '2400' } });

    createSequenceMock.mockResolvedValue({} as any);
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createSequenceMock).toHaveBeenCalledWith({
      name: 'Wait Seq',
      version: 1,
      interStepDelayRangeMs: null,
      steps: [
        {
          stepId: 'step-1',
          stepType: 'Action',
          primitiveAction: {
            type: 'WaitForImage',
            schemaVersion: 'v1',
            payload: {
              detectionTarget: {
                referenceImageId: 'mail_icon',
                confidence: 0.88,
              },
              timeoutMs: 2400,
            }
          },
          condition: null
        }
      ]
    }));
  });

  it('loads and saves an existing wait-for-image step', async () => {
    listSequencesMock.mockResolvedValue([{ id: 'seq-wait', name: 'Wait Sequence', steps: [] }] as any);
    getSequenceMock.mockResolvedValue({
      id: 'seq-wait',
      name: 'Wait Sequence',
      version: 4,
      steps: [
        {
          stepId: 'step-1',
          primitiveAction: {
            type: 'WaitForImage',
            schemaVersion: 'v1',
            payload: {
              detectionTarget: { referenceImageId: 'mail_icon', confidence: 0.91 },
              timeoutMs: 1800
            }
          },
          condition: null
        }
      ]
    } as any);
    updateSequenceMock.mockResolvedValue({} as any);

    render(<SequencesPage />);

    await screen.findByText('Wait Sequence');
    fireEvent.click(screen.getByText('Wait Sequence'));

    await screen.findByText('Edit Sequence');
    expect(screen.getByText('Wait for image: mail_icon')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateSequenceMock).toHaveBeenCalledWith('seq-wait', {
      name: 'Wait Sequence',
      version: 4,
      interStepDelayRangeMs: null,
      steps: [
        {
          stepId: 'step-1',
          stepType: 'Action',
          primitiveAction: {
            type: 'WaitForImage',
            schemaVersion: 'v1',
            payload: {
              detectionTarget: {
                referenceImageId: 'mail_icon',
                confidence: 0.91,
              },
              timeoutMs: 1800,
            }
          },
          condition: null
        }
      ]
    }));
  });

  it('preserves loop body command selections when reloading saved primitive actions', async () => {
    listSequencesMock.mockResolvedValue([{ id: 'seq-loop', name: 'Loop Sequence', steps: [] }] as any);
    getSequenceMock.mockResolvedValue({
      id: 'seq-loop',
      name: 'Loop Sequence',
      version: 3,
      steps: [
        {
          stepId: 'loop-1',
          stepType: 'Loop',
          loop: {
            loopType: 'count',
            count: 2,
            maxIterations: 2
          },
          body: [
            {
              stepId: 'body-step-1',
              stepType: 'Action',
              primitiveAction: {
                type: 'command',
                schemaVersion: 'v1',
                payload: {
                  commandId: 'c2'
                }
              },
              condition: null
            }
          ]
        }
      ]
    } as any);
    updateSequenceMock.mockResolvedValue({} as any);

    render(<SequencesPage />);

    await screen.findByText('Loop Sequence');
    fireEvent.click(screen.getByText('Loop Sequence'));

    await screen.findByText('Edit Sequence');

    const bodyCommandSelect = screen.getByTestId('loop-body-command-select') as HTMLSelectElement;
    expect(bodyCommandSelect.value).toBe('c2');

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateSequenceMock).toHaveBeenCalledWith('seq-loop', {
      name: 'Loop Sequence',
      version: 3,
      interStepDelayRangeMs: null,
      steps: [
        {
          stepId: 'loop-1',
          stepType: 'Loop',
          loop: {
            loopType: 'count',
            count: 2,
            maxIterations: 2
          },
          body: [
            {
              stepId: 'body-step-1',
              stepType: 'Action',
              primitiveAction: {
                type: 'command',
                schemaVersion: 'v1',
                payload: {
                  commandId: 'c2'
                }
              },
              condition: null
            }
          ]
        }
      ]
    }));
  });

  it('labels the add-step group "Loops and Conditions" and lists If after the loop buttons', async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));

    expect(screen.getByText('Loops and Conditions')).toBeInTheDocument();
    const buttonGroup = screen.getByTestId('add-loop-buttons');
    const labels = within(buttonGroup).getAllByRole('button').map((b) => b.textContent);
    expect(labels).toEqual(['Count', 'While', 'Repeat‑Until', 'If']);

    fireEvent.click(within(buttonGroup).getByText('If'));
    expect(screen.getByTestId('if-block')).toBeInTheDocument();
    expect(screen.getByTestId('if-then-empty')).toBeInTheDocument();
  });

  it('creates a sequence with an if step carrying then and else branches', async () => {
    render(<SequencesPage />);
    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'If Seq' } });

    fireEvent.click(within(screen.getByTestId('add-loop-buttons')).getByText('If'));

    // Configure the condition image.
    fireEvent.change(screen.getByTestId('if-condition-imageId'), { target: { value: 'img-9' } });

    // Then branch: one command step.
    fireEvent.click(screen.getByTestId('if-then-add-step'));
    fireEvent.change(screen.getByTestId('if-then-command-select'), { target: { value: 'c1' } });

    // Else branch: reveal and add one command step.
    fireEvent.click(screen.getByTestId('if-add-else'));
    fireEvent.click(screen.getByTestId('if-else-add-step'));
    fireEvent.change(screen.getByTestId('if-else-command-select'), { target: { value: 'c2' } });

    createSequenceMock.mockResolvedValue({} as any);
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createSequenceMock).toHaveBeenCalledWith({
      name: 'If Seq',
      version: 1,
      interStepDelayRangeMs: null,
      steps: [
        {
          stepId: 'step-1',
          stepType: 'If',
          if: { condition: { type: 'imageVisible', imageId: 'img-9', minSimilarity: null } },
          body: [
            {
              stepId: 'then-step-1',
              stepType: 'Action',
              primitiveAction: { type: 'command', schemaVersion: 'v1', payload: { commandId: 'c1' } },
              condition: null
            }
          ],
          elseBody: [
            {
              stepId: 'else-step-1',
              stepType: 'Action',
              primitiveAction: { type: 'command', schemaVersion: 'v1', payload: { commandId: 'c2' } },
              condition: null
            }
          ]
        }
      ]
    }));
  });

  it('round-trips an if step nested inside a loop body', async () => {
    listSequencesMock.mockResolvedValue([{ id: 'seq-if', name: 'If In Loop', steps: [] }] as any);
    getSequenceMock.mockResolvedValue({
      id: 'seq-if',
      name: 'If In Loop',
      version: 2,
      steps: [
        {
          stepId: 'loop-1',
          stepType: 'Loop',
          loop: { loopType: 'count', count: 2, maxIterations: 2 },
          body: [
            {
              stepId: 'if-1',
              stepType: 'If',
              if: { condition: { type: 'imageVisible', imageId: 'img-p', minSimilarity: null, negate: false } },
              body: [
                {
                  stepId: 'then-1',
                  stepType: 'Action',
                  primitiveAction: { type: 'command', schemaVersion: 'v1', payload: { commandId: 'c2' } },
                  condition: null
                }
              ],
              elseBody: null
            }
          ]
        }
      ]
    } as any);
    updateSequenceMock.mockResolvedValue({} as any);

    render(<SequencesPage />);

    await screen.findByText('If In Loop');
    fireEvent.click(screen.getByText('If In Loop'));

    await screen.findByText('Edit Sequence');

    // The nested if renders inside the loop block with its then step.
    expect(screen.getByTestId('if-block')).toBeInTheDocument();
    const thenSelect = screen.getByTestId('if-then-command-select') as HTMLSelectElement;
    expect(thenSelect.value).toBe('c2');
    // Nested inside a loop, branches may add break steps.
    expect(screen.getByTestId('if-then-add-break')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateSequenceMock).toHaveBeenCalledWith('seq-if', {
      name: 'If In Loop',
      version: 2,
      interStepDelayRangeMs: null,
      steps: [
        {
          stepId: 'loop-1',
          stepType: 'Loop',
          loop: { loopType: 'count', count: 2, maxIterations: 2 },
          body: [
            {
              stepId: 'if-1',
              stepType: 'If',
              if: { condition: { type: 'imageVisible', imageId: 'img-p', minSimilarity: null, negate: false } },
              body: [
                {
                  stepId: 'then-1',
                  stepType: 'Action',
                  primitiveAction: { type: 'command', schemaVersion: 'v1', payload: { commandId: 'c2' } },
                  condition: null
                }
              ],
              elseBody: null
            }
          ]
        }
      ]
    }));
  });
});
