import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { GamesPage } from '../GamesPage';
import { listGames, createGame, getGame, updateGame } from '../../services/games';

jest.mock('../../services/games');

const listGamesMock = listGames as jest.MockedFunction<typeof listGames>;
const createGameMock = createGame as jest.MockedFunction<typeof createGame>;
const getGameMock = getGame as jest.MockedFunction<typeof getGame>;
const updateGameMock = updateGame as jest.MockedFunction<typeof updateGame>;

beforeEach(() => {
  jest.resetAllMocks();
  listGamesMock.mockResolvedValue([] as any);
});

describe('GamesPage', () => {
  it('validates and creates a game with metadata', async () => {
    render(<GamesPage />);

    await waitFor(() => expect(listGamesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Game'));
    fireEvent.click(screen.getByText('Save'));
    expect(await screen.findByText('Name is required')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'My Game' } });
    fireEvent.click(screen.getByText('Add metadata'));
    const keyInput = screen.getAllByLabelText('Key')[0];
    const valueInput = screen.getAllByLabelText('Value')[0];
    fireEvent.change(keyInput, { target: { value: 'mode' } });
    fireEvent.change(valueInput, { target: { value: 'arcade' } });

    createGameMock.mockResolvedValue({} as any);
    listGamesMock.mockResolvedValueOnce([] as any); // refresh after create

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createGameMock).toHaveBeenCalledWith({ name: 'My Game', metadata: { mode: 'arcade' } }));
  });

  it('loads and updates an existing game', async () => {
    listGamesMock.mockResolvedValue([{ id: 'g1', name: 'Game 1', metadata: { mode: 'arcade' } }] as any);
    getGameMock.mockResolvedValue({ id: 'g1', name: 'Game 1', metadata: { mode: 'arcade' } } as any);
    updateGameMock.mockResolvedValue({} as any);

    render(<GamesPage />);

    await screen.findByText('Game 1');
    fireEvent.click(screen.getByText('Game 1'));

    await screen.findByText('Edit Game');
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Game 1 Updated' } });
    fireEvent.click(screen.getAllByText('Delete')[0]);
    fireEvent.click(screen.getByText('Add metadata'));
    const keyInput = screen.getAllByLabelText('Key')[0];
    const valueInput = screen.getAllByLabelText('Value')[0];
    fireEvent.change(keyInput, { target: { value: 'difficulty' } });
    fireEvent.change(valueInput, { target: { value: 'hard' } });

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateGameMock).toHaveBeenCalledWith('g1', {
      name: 'Game 1 Updated',
      metadata: { difficulty: 'hard' },
    }));
  });
});
