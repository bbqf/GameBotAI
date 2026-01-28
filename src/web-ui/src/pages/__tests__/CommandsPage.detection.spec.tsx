import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CommandsPage } from '../CommandsPage';
import { listCommands, createCommand, getCommand, updateCommand } from '../../services/commands';
import { listActions } from '../../services/actions';
import { listActions as listDomainActions } from '../../services/actionsApi';
import { listGames } from '../../services/games';

jest.mock('../../services/commands');
jest.mock('../../services/actions');
jest.mock('../../services/actionsApi', () => ({
  listActions: jest.fn()
}));
jest.mock('../../services/games', () => ({
  listGames: jest.fn()
}));

const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;
const createCommandMock = createCommand as jest.MockedFunction<typeof createCommand>;
const getCommandMock = getCommand as jest.MockedFunction<typeof getCommand>;
const updateCommandMock = updateCommand as jest.MockedFunction<typeof updateCommand>;
const listActionsMock = listActions as jest.MockedFunction<typeof listActions>;
const listDomainActionsMock = listDomainActions as jest.MockedFunction<typeof listDomainActions>;
const listGamesMock = listGames as jest.MockedFunction<typeof listGames>;

describe('CommandsPage detection persistence', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listCommandsMock.mockResolvedValue([] as any);
    listActionsMock.mockResolvedValue([{ id: 'a1', name: 'Action One', description: 'desc' }] as any);
    listDomainActionsMock.mockResolvedValue([] as any);
    listGamesMock.mockResolvedValue([] as any);
  });

  it('creates a command with detection payload', async () => {
    render(<CommandsPage />);
    await waitFor(() => expect(listCommandsMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Command'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Detect Command' } });
    fireEvent.change(screen.getByLabelText('Reference image ID'), { target: { value: 'template_a' } });
    fireEvent.change(screen.getByLabelText('Confidence (0-1)'), { target: { value: '0.77' } });
    fireEvent.change(screen.getByLabelText('Offset X'), { target: { value: '5' } });
    fireEvent.change(screen.getByLabelText('Offset Y'), { target: { value: '-3' } });

    fireEvent.change(screen.getByLabelText('Add action'), { target: { value: 'a1' } });
    fireEvent.click(screen.getByText('Add action step'));

    createCommandMock.mockResolvedValue({} as any);

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createCommandMock).toHaveBeenCalledWith({
      name: 'Detect Command',
      steps: [{ type: 'Action', targetId: 'a1', order: 0 }],
      detection: { referenceImageId: 'template_a', confidence: 0.77, offsetX: 5, offsetY: -3 },
    }));
  });

  it('loads and updates detection fields without loss', async () => {
    listCommandsMock.mockResolvedValue([{ id: 'c1', name: 'DetectCmd', detection: { referenceImageId: 'tpl1', confidence: 0.5, offsetX: 1, offsetY: 2 }, steps: [{ type: 'Action', targetId: 'a1', order: 0 }] } as any]);
    getCommandMock.mockResolvedValue({ id: 'c1', name: 'DetectCmd', detection: { referenceImageId: 'tpl1', confidence: 0.5, offsetX: 1, offsetY: 2 }, steps: [{ type: 'Action', targetId: 'a1', order: 0 }] } as any);
    updateCommandMock.mockResolvedValue({} as any);

    render(<CommandsPage />);
    await screen.findByText('DetectCmd');
    fireEvent.click(screen.getByText('DetectCmd'));

    await screen.findByText('Edit Command');

    expect((screen.getByLabelText('Reference image ID') as HTMLInputElement).value).toBe('tpl1');
    expect((screen.getByLabelText('Confidence (0-1)') as HTMLInputElement).value).toBe('0.5');
    expect((screen.getByLabelText('Offset X') as HTMLInputElement).value).toBe('1');
    expect((screen.getByLabelText('Offset Y') as HTMLInputElement).value).toBe('2');

    fireEvent.change(screen.getByLabelText('Reference image ID'), { target: { value: 'tpl2' } });
    fireEvent.change(screen.getByLabelText('Confidence (0-1)'), { target: { value: '0.9' } });
    fireEvent.change(screen.getByLabelText('Offset X'), { target: { value: '-4' } });
    fireEvent.change(screen.getByLabelText('Offset Y'), { target: { value: '7' } });

    fireEvent.click(screen.getAllByText('Save')[0]);

    await waitFor(() => expect(updateCommandMock).toHaveBeenCalledWith('c1', {
      name: 'DetectCmd',
      steps: [{ type: 'Action', targetId: 'a1', order: 0 }],
      detection: { referenceImageId: 'tpl2', confidence: 0.9, offsetX: -4, offsetY: 7 },
    }));
  });
});
