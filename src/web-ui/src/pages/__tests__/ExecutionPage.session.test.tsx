import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ExecutionPage } from '../Execution';
import { listActions } from '../../services/actionsApi';
import { useGames } from '../../services/useGames';
import { listCommands, forceExecuteCommand } from '../../services/commands';
import { getRunningSessions, stopSession } from '../../services/sessionsApi';

jest.mock('../../services/actionsApi', () => ({
  listActions: jest.fn()
}));

jest.mock('../../services/useGames', () => ({
  useGames: jest.fn()
}));

jest.mock('../../services/commands', () => ({
  listCommands: jest.fn(),
  forceExecuteCommand: jest.fn()
}));

jest.mock('../../services/sessionsApi', () => ({
  getRunningSessions: jest.fn(),
  stopSession: jest.fn()
}));

type ActionMock = typeof listActions;
type GamesHookMock = typeof useGames;
type CommandsMock = typeof listCommands;
type ExecuteMock = typeof forceExecuteCommand;
type RunningMock = typeof getRunningSessions;
type StopMock = typeof stopSession;

const mockListActions = listActions as jest.MockedFunction<ActionMock>;
const mockUseGames = useGames as jest.MockedFunction<GamesHookMock>;
const mockListCommands = listCommands as jest.MockedFunction<CommandsMock>;
const mockForceExecute = forceExecuteCommand as jest.MockedFunction<ExecuteMock>;
const mockGetRunningSessions = getRunningSessions as jest.MockedFunction<RunningMock>;
const mockStopSession = stopSession as jest.MockedFunction<StopMock>;

const connectAction = {
  id: 'action-1',
  name: 'Connect',
  gameId: 'game-1',
  type: 'connect-to-game',
  attributes: { adbSerial: 'emu-1' }
};

const commandWithConnectStep = {
  id: 'cmd-1',
  name: 'Cmd',
  steps: [{ type: 'Action', targetId: 'action-1', order: 0 }]
};

const runningSession = {
  sessionId: 'sess-123',
  gameId: 'game-1',
  emulatorId: 'emu-1',
  startedAtUtc: new Date().toISOString(),
  lastHeartbeatUtc: new Date().toISOString(),
  status: 'Running' as const
};

describe('ExecutionPage session reuse', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUseGames.mockReturnValue({ loading: false, data: [{ id: 'game-1', name: 'Test Game' }] });
    mockListActions.mockResolvedValue([connectAction as any]);
    mockListCommands.mockResolvedValue([commandWithConnectStep as any]);
    mockGetRunningSessions.mockResolvedValue([runningSession as any]);
    mockForceExecute.mockResolvedValue({ accepted: 1 } as any);
    mockStopSession.mockResolvedValue(true as any);
  });

  it('auto-uses cached running session when executing a command without manual session id', async () => {
    render(<ExecutionPage />);

    await screen.findByText(/Cached session: sess-123/i);
    await screen.findByRole('button', { name: 'Execute command' });

    fireEvent.click(screen.getByRole('button', { name: 'Execute command' }));

    await waitFor(() => expect(mockForceExecute).toHaveBeenCalledWith('cmd-1', 'sess-123'));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('clears cached banner after stop and refresh', async () => {
    mockGetRunningSessions.mockResolvedValueOnce([runningSession as any]).mockResolvedValueOnce([] as any);

    render(<ExecutionPage />);

    await screen.findByText(/Cached session: sess-123/i);

    fireEvent.click(screen.getByRole('button', { name: 'Stop session' }));

    await waitFor(() => expect(mockStopSession).toHaveBeenCalledWith('sess-123'));
    await waitFor(() => expect(screen.queryByText(/Cached session: sess-123/i)).not.toBeInTheDocument());
  });
});
