import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ExecutionPage } from '../Execution';
import { listActions } from '../../services/actionsApi';
import { createSession } from '../../services/sessionsApi';
import { useGames } from '../../services/useGames';
import { sessionCache } from '../../lib/sessionCache';
import { ApiError } from '../../lib/api';

jest.mock('../../services/actionsApi');
jest.mock('../../services/sessionsApi');
jest.mock('../../services/useGames');

const listActionsMock = listActions as jest.MockedFunction<typeof listActions>;
const createSessionMock = createSession as jest.MockedFunction<typeof createSession>;
const useGamesMock = useGames as unknown as jest.MockedFunction<typeof useGames>;

const connectAction = { id: 'c1', name: 'Connect Action', gameId: 'g1', type: 'connect-to-game', attributes: { adbSerial: 'emulator-5554', gameId: 'g1' } } as any;

describe('ExecutionPage', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    localStorage.clear();
    useGamesMock.mockReturnValue({ data: [{ id: 'g1', name: 'Test Game' }], loading: false, error: undefined } as any);
    listActionsMock.mockResolvedValue([connectAction]);
  });

  it('creates a session and caches the sessionId', async () => {
    createSessionMock.mockResolvedValue({ id: 's123', sessionId: 's123', status: 'RUNNING', gameId: 'g1' });

    render(<ExecutionPage />);

    const runButton = await screen.findByRole('button', { name: /start session/i });
    await waitFor(() => expect(runButton).not.toBeDisabled());

    fireEvent.click(runButton);

    await waitFor(() => expect(createSessionMock).toHaveBeenCalledWith({ gameId: 'g1', adbSerial: 'emulator-5554' }));
    expect(await screen.findByText(/Active sessionId/i)).toHaveTextContent('s123');
    expect(sessionCache.get('g1', 'emulator-5554')).toBe('s123');
  });

  it('shows an error and does not cache on failure', async () => {
    createSessionMock.mockRejectedValue(new ApiError(504, 'Session creation timed out'));

    render(<ExecutionPage />);

    const runButton = await screen.findByRole('button', { name: /start session/i });
    await waitFor(() => expect(runButton).not.toBeDisabled());
    fireEvent.click(runButton);

    expect(await screen.findByRole('alert')).toHaveTextContent('Session creation timed out');
    expect(sessionCache.get('g1', 'emulator-5554')).toBeUndefined();
  });
});
