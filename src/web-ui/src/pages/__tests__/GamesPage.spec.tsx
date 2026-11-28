import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { GamesPage } from '../GamesPage';
import { listGames, createGame, getGame, updateGame, deleteGame } from '../../services/games';
import { ApiError } from '../../lib/api';

jest.mock('../../services/games');

const listGamesMock = listGames as jest.MockedFunction<typeof listGames>;
const createGameMock = createGame as jest.MockedFunction<typeof createGame>;
const getGameMock = getGame as jest.MockedFunction<typeof getGame>;
const updateGameMock = updateGame as jest.MockedFunction<typeof updateGame>;
const deleteGameMock = deleteGame as jest.MockedFunction<typeof deleteGame>;

beforeEach(() => {
  jest.resetAllMocks();
  listGamesMock.mockResolvedValue([] as any);
});

const withGame = () => {
  listGamesMock.mockResolvedValue([{ id: 'g1', name: 'Game 1' }] as any);
  getGameMock.mockResolvedValue({ id: 'g1', name: 'Game 1' } as any);
};

describe('GamesPage list', () => {
  it('shows the header with one Create game button and the filter above the list', async () => {
    render(<GamesPage />);
    expect(await screen.findByText('No games.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Games' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create game' })).toBeInTheDocument();
    const filter = screen.getByLabelText('Filter by name');
    expect(filter.closest('th')).toBeNull();
  });

  it('shows a dash for an empty package name and the value otherwise', async () => {
    listGamesMock.mockResolvedValue([
      { id: 'g1', name: 'Alpha' },
      { id: 'g2', name: 'Beta', packageName: 'com.example.beta' },
    ] as any);
    render(<GamesPage />);
    const alpha = (await screen.findByTestId('game-row-g1'));
    expect(within(alpha).getByText('—')).toBeInTheDocument();
    expect(within(screen.getByTestId('game-row-g2')).getByText('com.example.beta')).toBeInTheDocument();
  });

  it('filters the rows by name', async () => {
    listGamesMock.mockResolvedValue([{ id: 'g1', name: 'Alpha' }, { id: 'g2', name: 'Beta' }] as any);
    render(<GamesPage />);
    await screen.findByText('Alpha');
    fireEvent.change(screen.getByLabelText('Filter by name'), { target: { value: 'bet' } });
    expect(screen.queryByText('Alpha')).toBeNull();
    expect(screen.getByText('Beta')).toBeInTheDocument();
  });

  it('shows the error state with Try again', async () => {
    listGamesMock.mockRejectedValueOnce(new Error('Server down'));
    render(<GamesPage />);
    expect(await screen.findByText('Server down')).toBeInTheDocument();
    listGamesMock.mockResolvedValue([{ id: 'g1', name: 'Game 1' }] as any);
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByText('Game 1')).toBeInTheDocument();
  });

  it('opens the edit page with the Edit button', async () => {
    withGame();
    render(<GamesPage />);
    await screen.findByText('Game 1');
    fireEvent.click(screen.getByRole('button', { name: 'Edit Game 1' }));
    expect(await screen.findByRole('heading', { name: 'Game 1' })).toBeInTheDocument();
  });
});

describe('GamesPage form', () => {
  it('validates and creates a game', async () => {
    render(<GamesPage />);

    await waitFor(() => expect(listGamesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('button', { name: 'Create game' }));
    expect(screen.getByRole('heading', { name: 'New game' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Delete' })).toBeNull();
    fireEvent.click(screen.getByText('Save'));
    expect(await screen.findByText('Name is required')).toBeInTheDocument();
    expect(screen.getByLabelText('Name *')).toHaveAttribute('aria-invalid', 'true');

    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'My Game' } });
    createGameMock.mockResolvedValue({} as any);
    listGamesMock.mockResolvedValueOnce([] as any); // refresh after create

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createGameMock).toHaveBeenCalledWith({ name: 'My Game' }));
    expect(await screen.findByText('Game created.')).toBeInTheDocument();
  });

  it('loads and updates an existing game', async () => {
    withGame();
    updateGameMock.mockResolvedValue({} as any);

    render(<GamesPage />);

    await screen.findByText('Game 1');
    fireEvent.click(screen.getByText('Game 1'));

    const title = await screen.findByRole('heading', { name: 'Game 1' });
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Game 1 Updated' } });
    expect(screen.getByRole('heading', { name: 'Game 1' })).toBe(title);
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateGameMock).toHaveBeenCalledWith('g1', {
      name: 'Game 1 Updated'
    }));
    expect(await screen.findByText('Game updated.')).toBeInTheDocument();
  });

  it('puts Delete first and Save last in the footer bar of the edit page', async () => {
    withGame();
    const { container } = render(<GamesPage />);
    await screen.findByText('Game 1');
    fireEvent.click(screen.getByText('Game 1'));
    await screen.findByRole('heading', { name: 'Game 1' });
    const buttons = Array.from(container.querySelectorAll('.ui-footer-bar button')).map((b) => b.textContent);
    expect(buttons).toEqual(['Delete', 'Cancel', 'Save']);
  });

  it('goes back to the list with the back button', async () => {
    withGame();
    render(<GamesPage />);
    await screen.findByText('Game 1');
    fireEvent.click(screen.getByText('Game 1'));
    await screen.findByRole('heading', { name: 'Game 1' });
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(await screen.findByRole('heading', { name: 'Games' })).toBeInTheDocument();
  });

  it('asks before the back button leaves unsaved changes', async () => {
    withGame();
    const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(false);
    render(<GamesPage />);
    await screen.findByText('Game 1');
    fireEvent.click(screen.getByText('Game 1'));
    await screen.findByRole('heading', { name: 'Game 1' });
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Changed' } });
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(confirmSpy).toHaveBeenCalled();
    expect(screen.getByLabelText('Name *')).toBeInTheDocument();
    confirmSpy.mockRestore();
  });
});

describe('GamesPage delete', () => {
  const openRowDelete = async () => {
    withGame();
    render(<GamesPage />);
    await screen.findByText('Game 1');
    fireEvent.click(screen.getByRole('button', { name: 'More actions for Game 1' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: /Delete/ }));
    return screen.findByRole('dialog', { name: 'Delete game Game 1?' });
  };

  it('deletes from the row menu after the dialog confirm', async () => {
    deleteGameMock.mockResolvedValue(undefined as any);
    const dialog = await openRowDelete();
    expect(within(dialog).getByText('You cannot undo this.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus();
    listGamesMock.mockResolvedValue([] as any);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(deleteGameMock).toHaveBeenCalledTimes(1));
    expect(deleteGameMock).toHaveBeenCalledWith('g1');
    expect(await screen.findByText('Game deleted.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('shows the references for a 409 and turns the Delete button off', async () => {
    deleteGameMock.mockRejectedValue(new ApiError(409, 'Game is in use', undefined, undefined, { commands: [{ id: 'c1', name: 'Cmd One' }] }));
    const dialog = await openRowDelete();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    expect(await within(dialog).findByText('Referenced by:')).toBeInTheDocument();
    expect(within(dialog).getByText(/Cmd One/)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Delete' })).toBeDisabled();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('shows another error in the message area', async () => {
    deleteGameMock.mockRejectedValue(new Error('Network failure'));
    const dialog = await openRowDelete();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    expect(await screen.findByText('Network failure')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('deletes from the footer of the edit page and shows the list', async () => {
    withGame();
    deleteGameMock.mockResolvedValue(undefined as any);
    render(<GamesPage />);
    await screen.findByText('Game 1');
    fireEvent.click(screen.getByText('Game 1'));
    await screen.findByRole('heading', { name: 'Game 1' });
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    const dialog = await screen.findByRole('dialog', { name: 'Delete game Game 1?' });
    listGamesMock.mockResolvedValue([] as any);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    expect(await screen.findByText('Game deleted.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Games' })).toBeInTheDocument();
  });
});
