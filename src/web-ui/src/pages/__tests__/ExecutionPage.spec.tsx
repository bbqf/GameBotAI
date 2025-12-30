import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ExecutionPage } from '../Execution';
import { listActions } from '../../services/actionsApi';
import { createSession } from '../../services/sessionsApi';
import { listCommands, forceExecuteCommand } from '../../services/commands';
import { useGames } from '../../services/useGames';
import { sessionCache } from '../../lib/sessionCache';
import { ApiError } from '../../lib/api';

jest.mock('../../services/actionsApi');
jest.mock('../../services/sessionsApi');
jest.mock('../../services/commands');
jest.mock('../../services/useGames');

const listActionsMock = listActions as jest.MockedFunction<typeof listActions>;
const createSessionMock = createSession as jest.MockedFunction<typeof createSession>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;
const forceExecuteMock = forceExecuteCommand as jest.MockedFunction<typeof forceExecuteCommand>;
const useGamesMock = useGames as unknown as jest.MockedFunction<typeof useGames>;

const connectAction = { id: 'c1', name: 'Connect Action', gameId: 'g1', type: 'connect-to-game', attributes: { adbSerial: 'emulator-5554', gameId: 'g1' } } as any;
const connectCommand = { id: 'cmd1', name: 'Cmd 1', steps: [{ type: 'Action', targetId: 'c1', order: 0 }] } as any;

describe('ExecutionPage', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    localStorage.clear();
    useGamesMock.mockReturnValue({ data: [{ id: 'g1', name: 'Test Game' }], loading: false, error: undefined } as any);
    listActionsMock.mockResolvedValue([connectAction]);
    listCommandsMock.mockResolvedValue([connectCommand]);
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

  it('auto-injects cached sessionId when executing a command', async () => {
    createSessionMock.mockResolvedValue({ id: 's123', sessionId: 's123', status: 'RUNNING', gameId: 'g1' });
    forceExecuteMock.mockResolvedValue({ accepted: 2 } as any);
    sessionCache.set('g1', 'emulator-5554', 'sid-cached');

    render(<ExecutionPage />);

    const executeButton = await screen.findByRole('button', { name: /execute command/i });
    await waitFor(() => expect(executeButton).not.toBeDisabled());

    fireEvent.click(executeButton);

    await waitFor(() => expect(forceExecuteMock).toHaveBeenCalledWith('cmd1', 'sid-cached'));
    expect(await screen.findByRole('status')).toHaveTextContent('Command accepted: 2');
  });

  it('blocks execution when no cached session is available', async () => {
    forceExecuteMock.mockResolvedValue({ accepted: 1 } as any);

    render(<ExecutionPage />);

    const executeButton = await screen.findByRole('button', { name: /execute command/i });
    await waitFor(() => expect(executeButton).not.toBeDisabled());

    fireEvent.click(executeButton);

    expect(forceExecuteMock).not.toHaveBeenCalled();
    expect(await screen.findByRole('alert')).toHaveTextContent('No cached session');
  });
});
