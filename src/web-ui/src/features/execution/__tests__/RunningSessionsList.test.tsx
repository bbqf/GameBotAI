import React from 'react';
import { render, screen, waitFor, fireEvent, within } from '@testing-library/react';
import { ExecutionPage } from '../../../pages/Execution';
import { listActions } from '../../../services/actionsApi';
import { listCommands } from '../../../services/commands';
import { useGames } from '../../../services/useGames';
import { getRunningSessions, startSession, stopSession } from '../../../services/sessionsApi';

jest.mock('../../../services/actionsApi', () => ({ listActions: jest.fn() }));
jest.mock('../../../services/commands', () => ({ listCommands: jest.fn() }));
jest.mock('../../../services/useGames', () => ({ useGames: jest.fn() }));
jest.mock('../../../services/sessionsApi', () => ({
  getRunningSessions: jest.fn(),
  startSession: jest.fn(),
  stopSession: jest.fn()
}));

type ActionsMock = typeof listActions;
type CommandsMock = typeof listCommands;
type GamesHook = typeof useGames;
type GetRunningMock = typeof getRunningSessions;
type StartSessionMock = typeof startSession;
type StopSessionMock = typeof stopSession;

const mockListActions = listActions as jest.MockedFunction<ActionsMock>;
const mockListCommands = listCommands as jest.MockedFunction<CommandsMock>;
const mockUseGames = useGames as jest.MockedFunction<GamesHook>;
const mockGetRunning = getRunningSessions as jest.MockedFunction<GetRunningMock>;
const mockStartSession = startSession as jest.MockedFunction<StartSessionMock>;
const mockStopSession = stopSession as jest.MockedFunction<StopSessionMock>;

const connectAction = { id: 'a1', name: 'Connect', gameId: 'g1', type: 'connect-to-game', attributes: { adbSerial: 'emu-1' } } as any;
const runningOne = { sessionId: 's-1', gameId: 'g1', emulatorId: 'emu-1', startedAtUtc: new Date().toISOString(), lastHeartbeatUtc: new Date().toISOString(), status: 'Running' as const };
const runningTwo = { sessionId: 's-2', gameId: 'g1', emulatorId: 'emu-1', startedAtUtc: new Date().toISOString(), lastHeartbeatUtc: new Date().toISOString(), status: 'Running' as const };

describe('Running sessions list', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUseGames.mockReturnValue({ loading: false, data: [{ id: 'g1', name: 'Game One' }], error: undefined });
    mockListActions.mockResolvedValue([connectAction]);
    mockListCommands.mockResolvedValue([] as any);
    mockGetRunning.mockResolvedValue([runningOne] as any);
    mockStartSession.mockResolvedValue({ sessionId: runningTwo.sessionId, runningSessions: [runningTwo] } as any);
    mockStopSession.mockResolvedValue(true as any);
  });

  it('shows running sessions from the service', async () => {
    render(<ExecutionPage />);

    const runningSection = await screen.findByRole('region', { name: /Running sessions/i });
    expect(within(runningSection).getByText(/Session: s-1/i)).toBeInTheDocument();
    expect(within(runningSection).getByRole('status', { name: /Status: Running/i })).toBeInTheDocument();
  });

  it('removes a session after stop refresh', async () => {
    mockGetRunning.mockResolvedValueOnce([runningOne] as any).mockResolvedValueOnce([] as any);

    render(<ExecutionPage />);

    const runningSection = await screen.findByRole('region', { name: /Running sessions/i });
    within(runningSection).getByText(/Session: s-1/i);
    const stopButtons = within(runningSection).getAllByRole('button', { name: 'Stop' });
    fireEvent.click(stopButtons[0]);

    await waitFor(() => expect(mockStopSession).toHaveBeenCalledWith('s-1'));
    await waitFor(() => expect(within(runningSection).queryByText(/Session: s-1/i)).not.toBeInTheDocument());
  });

  it('replaces running session after start refresh', async () => {
    mockGetRunning.mockResolvedValueOnce([runningOne] as any).mockResolvedValueOnce([runningTwo] as any);

    render(<ExecutionPage />);

    const startButton = await screen.findByRole('button', { name: /start session/i });
    await waitFor(() => expect(startButton).toBeEnabled());
    fireEvent.click(startButton);

    await waitFor(() => expect(mockStartSession).toHaveBeenCalled());
    const runningSection = await screen.findByRole('region', { name: /Running sessions/i });
    await waitFor(() => expect(within(runningSection).queryByText(/Session: s-1/i)).not.toBeInTheDocument());
    expect(await within(runningSection).findByText(/Session: s-2/i)).toBeInTheDocument();
  });
});
