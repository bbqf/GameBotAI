import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ExecutionPage } from '../Execution';
import { listActions } from '../../services/actionsApi';
import { useGames } from '../../services/useGames';
import { listCommands, forceExecuteCommand } from '../../services/commands';
import { getRunningSessions, stopSession } from '../../services/sessionsApi';
import { listSequences, executeSequence } from '../../services/sequences';

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

jest.mock('../../services/sequences', () => ({
  listSequences: jest.fn(),
  executeSequence: jest.fn()
}));

type ActionMock = typeof listActions;
type GamesHookMock = typeof useGames;
type CommandsMock = typeof listCommands;
type ExecuteMock = typeof forceExecuteCommand;
type RunningMock = typeof getRunningSessions;
type StopMock = typeof stopSession;
type ListSequencesMock = typeof listSequences;
type ExecuteSequenceMock = typeof executeSequence;

const mockListActions = listActions as jest.MockedFunction<ActionMock>;
const mockUseGames = useGames as jest.MockedFunction<GamesHookMock>;
const mockListCommands = listCommands as jest.MockedFunction<CommandsMock>;
const mockForceExecute = forceExecuteCommand as jest.MockedFunction<ExecuteMock>;
const mockGetRunningSessions = getRunningSessions as jest.MockedFunction<RunningMock>;
const mockStopSession = stopSession as jest.MockedFunction<StopMock>;
const mockListSequences = listSequences as jest.MockedFunction<ListSequencesMock>;
const mockExecuteSequence = executeSequence as jest.MockedFunction<ExecuteSequenceMock>;

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
    mockListSequences.mockResolvedValue([]);
    mockExecuteSequence.mockResolvedValue({} as any);
  });

  it('auto-uses cached running session when executing a command without manual session id', async () => {
    render(<ExecutionPage />);

    await waitFor(() => expect(screen.getByLabelText(/Connect action/i)).toHaveValue('action-1'));
    const runningSection = await screen.findByRole('region', { name: /Running sessions/i });
    await within(runningSection).findByText(/Session: sess-123/i, {}, { timeout: 3000 });
    await screen.findByText(/Cached session: sess-123/i, {}, { timeout: 3000 });
    await screen.findByRole('button', { name: 'Execute command' });

    fireEvent.click(screen.getByRole('button', { name: 'Execute command' }));

    await waitFor(() => expect(mockForceExecute).toHaveBeenCalledWith('cmd-1', 'sess-123'));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('clears cached banner after stop and refresh', async () => {
    mockGetRunningSessions.mockResolvedValueOnce([runningSession as any]).mockResolvedValueOnce([] as any);

    render(<ExecutionPage />);

    await waitFor(() => expect(screen.getByLabelText(/Connect action/i)).toHaveValue('action-1'));
    await screen.findByText(/Cached session: sess-123/i, {}, { timeout: 3000 });

    fireEvent.click(screen.getByRole('button', { name: 'Stop session' }));

    await waitFor(() => expect(mockStopSession).toHaveBeenCalledWith('sess-123'));
    await waitFor(() => expect(screen.queryByText(/Cached session: sess-123/i)).not.toBeInTheDocument());
  });
});

describe('ExecutionPage sequence execution', () => {
  const sequence1 = { id: 'seq-1', name: 'Sequence One', steps: [] };
  const sequence2 = { id: 'seq-2', name: 'Sequence Two', steps: [] };

  beforeEach(() => {
    jest.clearAllMocks();
    mockUseGames.mockReturnValue({ loading: false, data: [] });
    mockListActions.mockResolvedValue([]);
    mockListCommands.mockResolvedValue([]);
    mockGetRunningSessions.mockResolvedValue([]);
    mockStopSession.mockResolvedValue(true as any);
    mockListSequences.mockResolvedValue([sequence1, sequence2] as any);
    mockExecuteSequence.mockResolvedValue({} as any);
  });

  it('renders sequence dropdown with loaded sequences', async () => {
    render(<ExecutionPage />);
    const select = await screen.findByLabelText('Sequence');
    expect(select).toBeInTheDocument();
    await waitFor(() => {
      const options = within(select as HTMLElement).getAllByRole('option');
      expect(options).toHaveLength(2);
      expect(options[0]).toHaveTextContent('Sequence One');
      expect(options[1]).toHaveTextContent('Sequence Two');
    });
  });

  it('executes selected sequence on button click', async () => {
    render(<ExecutionPage />);
    await screen.findByLabelText('Sequence');
    await waitFor(() => expect(screen.getByLabelText('Sequence')).toHaveValue('seq-1'));

    fireEvent.click(screen.getByRole('button', { name: 'Execute sequence' }));

    await waitFor(() => expect(mockExecuteSequence).toHaveBeenCalledWith('seq-1', undefined));
    expect(await screen.findByText('Sequence executed successfully.')).toBeInTheDocument();
  });

  it('shows error when sequence execution fails', async () => {
    mockExecuteSequence.mockRejectedValue(new Error('Sequence failed'));
    render(<ExecutionPage />);
    await waitFor(() => expect(screen.getByLabelText('Sequence')).toHaveValue('seq-1'));

    fireEvent.click(screen.getByRole('button', { name: 'Execute sequence' }));

    expect(await screen.findByText('Sequence failed')).toBeInTheDocument();
  });

  it('shows hint when no sequences available', async () => {
    mockListSequences.mockResolvedValue([]);
    render(<ExecutionPage />);
    expect(await screen.findByText('No sequences available.')).toBeInTheDocument();
  });
});
