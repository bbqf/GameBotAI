import React from 'react';
import { render, screen } from '@testing-library/react';
import { ExecutionPage } from '../../../pages/Execution';
import { useGames } from '../../../services/useGames';
import { listCommands, forceExecuteCommand } from '../../../services/commands';
import { getRunningSessions, stopSession } from '../../../services/sessionsApi';

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

const mockUseGames = useGames as jest.MockedFunction<typeof useGames>;
const mockListCommands = listCommands as jest.MockedFunction<typeof listCommands>;
const mockForceExecute = forceExecuteCommand as jest.MockedFunction<typeof forceExecuteCommand>;
const mockGetRunningSessions = getRunningSessions as jest.MockedFunction<typeof getRunningSessions>;
const mockStopSession = stopSession as jest.MockedFunction<typeof stopSession>;

const commandWithConnectStep = {
  id: 'cmd-1',
  name: 'Cmd',
  steps: [{ type: 'Command', targetId: 'cmd-connect', order: 0 }]
};

const runningSession = {
  sessionId: 'sess-123',
  gameId: 'game-1',
  emulatorId: 'emu-1',
  startedAtUtc: new Date().toISOString(),
  lastHeartbeatUtc: new Date().toISOString(),
  status: 'Running' as const
};

const isCi = process.env.GITHUB_ACTIONS === 'true' || process.env.CI === 'true';
const perfThresholdMs = isCi ? 400 : 200;

const deferred = <T,>() => {
  let resolve: (value: T) => void = () => {};
  let reject: (reason?: unknown) => void = () => {};
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
};

describe('Execution UI perf checks', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUseGames.mockReturnValue({ loading: false, data: [{ id: 'game-1', name: 'Test Game' }] });
    mockListCommands.mockResolvedValue([commandWithConnectStep as any]);
    mockGetRunningSessions.mockResolvedValue([runningSession as any]);
    mockForceExecute.mockResolvedValue({ accepted: 1 } as any);
    mockStopSession.mockResolvedValue(true as any);
  });

  it(`renders banner and running list under ${perfThresholdMs}ms after data arrives`, async () => {
    const commandsDeferred = deferred<typeof commandWithConnectStep[]>();
    const sessionsDeferred = deferred<typeof runningSession[]>();

    mockListCommands.mockImplementationOnce(() => commandsDeferred.promise as any);
    mockGetRunningSessions.mockImplementationOnce(() => sessionsDeferred.promise as any);

    render(<ExecutionPage />);

    commandsDeferred.resolve([commandWithConnectStep as any]);
    sessionsDeferred.resolve([runningSession as any]);

    await screen.findByText(/Cached session: sess-123/i);

    const start = performance.now();
    await screen.findByRole('heading', { name: /Running sessions/i });

    const end = performance.now();
    expect(end - start).toBeLessThan(perfThresholdMs);
  });
});
