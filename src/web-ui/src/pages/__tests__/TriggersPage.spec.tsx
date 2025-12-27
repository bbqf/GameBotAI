import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { TriggersPage } from '../TriggersPage';
import { listTriggers, createTrigger, getTrigger, updateTrigger } from '../../services/triggers';
import { listActions } from '../../services/actions';
import { listCommands } from '../../services/commands';
import { listSequences } from '../../services/sequences';

jest.mock('../../services/triggers');
jest.mock('../../services/actions');
jest.mock('../../services/commands');
jest.mock('../../services/sequences');

const listTriggersMock = listTriggers as jest.MockedFunction<typeof listTriggers>;
const createTriggerMock = createTrigger as jest.MockedFunction<typeof createTrigger>;
const getTriggerMock = getTrigger as jest.MockedFunction<typeof getTrigger>;
const updateTriggerMock = updateTrigger as jest.MockedFunction<typeof updateTrigger>;
const listActionsMock = listActions as jest.MockedFunction<typeof listActions>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;
const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;

describe('TriggersPage', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listTriggersMock.mockResolvedValue([] as any);
    listActionsMock.mockResolvedValue([
      { id: 'a1', name: 'Action One' },
      { id: 'a2', name: 'Action Two' },
    ] as any);
    listCommandsMock.mockResolvedValue([
      { id: 'c1', name: 'Command One' },
      { id: 'c2', name: 'Command Two' },
    ] as any);
    listSequencesMock.mockResolvedValue([{ id: 's1', name: 'Sequence One' }] as any);
  });

  it('creates a trigger with ordered actions and commands', async () => {
    render(<TriggersPage />);

    await waitFor(() => expect(listTriggersMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Trigger'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Trigger A' } });
    fireEvent.change(screen.getByLabelText('Criteria (JSON)'), { target: { value: '{}' } });

    fireEvent.change(screen.getByLabelText('Add action'), { target: { value: 'a1' } });
    fireEvent.click(screen.getByText('Add to actions'));

    fireEvent.change(screen.getByLabelText('Add action'), { target: { value: 'a2' } });
    fireEvent.click(screen.getByText('Add to actions'));

    const actionsSection = screen.getByText('Actions').closest('section')!;
    const actionMoveUp = within(actionsSection).getAllByLabelText('Move up');
    fireEvent.click(actionMoveUp[1]);

    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'c1' } });
    fireEvent.click(screen.getByText('Add to commands'));

    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'c2' } });
    fireEvent.click(screen.getByText('Add to commands'));

    const commandsSection = screen.getByText('Commands').closest('section')!;
    const commandMoveDown = within(commandsSection).getAllByLabelText('Move down');
    fireEvent.click(commandMoveDown[0]);

    const sequenceSelect = screen.getByLabelText('Sequence', { selector: 'select' });
    fireEvent.change(sequenceSelect, { target: { value: 's1' } });

    createTriggerMock.mockResolvedValue({} as any);
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createTriggerMock).toHaveBeenCalledWith({
      name: 'Trigger A',
      criteria: {},
      actions: ['a2', 'a1'],
      commands: ['c2', 'c1'],
      sequence: 's1',
    }));
  });

  it('loads and updates an existing trigger', async () => {
    listTriggersMock.mockResolvedValue([{ id: 't1', name: 'Trigger 1', actions: ['a1'], commands: ['c1'] }] as any);
    getTriggerMock.mockResolvedValue({ id: 't1', name: 'Trigger 1', criteria: { type: 'simple' }, actions: ['a1'], commands: ['c1'] } as any);
    updateTriggerMock.mockResolvedValue({} as any);

    render(<TriggersPage />);

    await screen.findByText('Trigger 1');
    fireEvent.click(screen.getByText('Trigger 1'));

    await screen.findByText('Edit Trigger');
    fireEvent.change(screen.getByLabelText('Criteria (JSON)'), { target: { value: '{"type":"updated"}' } });

    fireEvent.change(screen.getByLabelText('Add action'), { target: { value: 'a2' } });
    fireEvent.click(screen.getByText('Add to actions'));

    const actionsSection = screen.getByText('Actions').closest('section')!;
    const actionMoveDown = within(actionsSection).getAllByLabelText('Move down');
    fireEvent.click(actionMoveDown[0]);

    fireEvent.click(screen.getAllByText('Save')[0]);

    await waitFor(() => expect(updateTriggerMock).toHaveBeenCalledWith('t1', {
      name: 'Trigger 1',
      criteria: { type: 'updated' },
      actions: ['a2', 'a1'],
      commands: ['c1'],
      sequence: undefined,
    }));
  });
});
