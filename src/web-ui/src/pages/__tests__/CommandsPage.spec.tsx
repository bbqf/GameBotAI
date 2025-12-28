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

describe('CommandsPage', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listCommandsMock.mockResolvedValue([] as any);
    listActionsMock.mockResolvedValue([{ id: 'a1', name: 'Action One', description: 'desc' }] as any);
    listDomainActionsMock.mockResolvedValue([] as any);
    listGamesMock.mockResolvedValue([] as any);
  });

  it('creates a command with validation', async () => {
    render(<CommandsPage />);

    await waitFor(() => expect(listCommandsMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Command'));
    fireEvent.click(screen.getByText('Save'));
    expect(await screen.findByText('Name is required')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Test Cmd' } });
    fireEvent.change(screen.getByLabelText('Add action'), { target: { value: 'a1' } });
    fireEvent.click(screen.getByText('Add action step'));

    createCommandMock.mockResolvedValue({} as any);

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createCommandMock).toHaveBeenCalledWith({ name: 'Test Cmd', steps: [{ type: 'Action', targetId: 'a1', order: 0 }], detectionTarget: undefined }));
  });

  it('loads and saves an existing command', async () => {
    listCommandsMock.mockResolvedValue([{ id: 'c1', name: 'Cmd', steps: [{ type: 'Action', targetId: 'a1', order: 0 }] } as any]);
    getCommandMock.mockResolvedValue({ id: 'c1', name: 'Cmd', steps: [{ type: 'Action', targetId: 'a1', order: 0 }] } as any);
    updateCommandMock.mockResolvedValue({} as any);

    render(<CommandsPage />);
    await screen.findByText('Cmd');
    fireEvent.click(screen.getByText('Cmd'));

    await screen.findByText('Edit Command');
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Cmd Updated' } });
    fireEvent.click(screen.getAllByText('Save')[0]);

    await waitFor(() => expect(updateCommandMock).toHaveBeenCalledWith('c1', {
      name: 'Cmd Updated',
      steps: [{ type: 'Action', targetId: 'a1', order: 0 }],
      detectionTarget: undefined,
    }));
  });

  it('reorders actions and preserves order on save', async () => {
    listCommandsMock.mockResolvedValue([{ id: 'c1', name: 'Cmd', steps: [
      { type: 'Action', targetId: 'a1', order: 0 },
      { type: 'Action', targetId: 'a2', order: 1 },
    ] } as any]);
    getCommandMock.mockResolvedValue({ id: 'c1', name: 'Cmd', steps: [
      { type: 'Action', targetId: 'a1', order: 0 },
      { type: 'Action', targetId: 'a2', order: 1 },
    ] } as any);
    listActionsMock.mockResolvedValue([
      { id: 'a1', name: 'Action One' } as any,
      { id: 'a2', name: 'Action Two' } as any,
    ]);
    updateCommandMock.mockResolvedValue({} as any);

    render(<CommandsPage />);
    await screen.findByText('Cmd');
    fireEvent.click(screen.getByText('Cmd'));

    await screen.findByText('Edit Command');

    const actionsSection = screen.getByText('Actions').closest('section')!;
    const moveUpButtons = actionsSection.querySelectorAll('button[aria-label="Move up"]');
    fireEvent.click(moveUpButtons[1]);

    fireEvent.click(screen.getAllByText('Save')[0]);

    await waitFor(() => expect(updateCommandMock).toHaveBeenCalledWith('c1', {
      name: 'Cmd',
      steps: [
        { type: 'Action', targetId: 'a2', order: 0 },
        { type: 'Action', targetId: 'a1', order: 1 },
      ],
      detectionTarget: undefined,
    }));
  });
});
