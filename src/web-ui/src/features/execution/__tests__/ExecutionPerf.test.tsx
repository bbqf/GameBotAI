import React from 'react';
import { render, screen } from '@testing-library/react';
import { ExecutionPage } from '../../../pages/Execution';
import { listActions } from '../../../services/actionsApi';
import { useGames } from '../../../services/useGames';
import { listCommands, forceExecuteCommand } from '../../../services/commands';
import { getRunningSessions, stopSession } from '../../../services/sessionsApi';

jest.mock('../../../services/actionsApi', () => ({
  listActions: jest.fn()
}));

jest.mock('../../../services/useGames', () => ({
  useGames: jest.fn()
}));

jest.mock('../../../services/commands', () => ({
  listCommands: jest.fn(),
  forceExecuteCommand: jest.fn()
}));

jest.mock('../../../services/sessionsApi', () => ({
  getRunningSessions: jest.fn(),
  stopSession: jest.fn(),
  startSession: jest.fn()
}));

const mockListActions = listActions as jest.MockedFunction<typeof listActions>;
const mockUseGames = useGames as jest.MockedFunction<typeof useGames>;
const mockListCommands = listCommands as jest.MockedFunction<typeof listCommands>;
const mockForceExecute = forceExecuteCommand as jest.MockedFunction<typeof forceExecuteCommand>;
const mockGetRunningSessions = getRunningSessions as jest.MockedFunction<typeof getRunningSessions>;
const mockStopSession = stopSession as jest.MockedFunction<typeof stopSession>;

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

describe('Execution UI perf checks', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUseGames.mockReturnValue({ loading: false, data: [{ id: 'game-1', name: 'Test Game' }] });
    mockListActions.mockResolvedValue([connectAction as any]);
    mockListCommands.mockResolvedValue([commandWithConnectStep as any]);
    mockGetRunningSessions.mockResolvedValue([runningSession as any]);
    mockForceExecute.mockResolvedValue({ accepted: 1 } as any);
    mockStopSession.mockResolvedValue(true as any);
  });

  it('renders banner and running list under 100ms after data arrives', async () => {
    const start = performance.now();
    render(<ExecutionPage />);

    await screen.findByText(/Cached session: sess-123/i);
    await screen.findByRole('heading', { name: /Running sessions/i });

    const end = performance.now();
    expect(end - start).toBeLessThan(100);
  });
});
