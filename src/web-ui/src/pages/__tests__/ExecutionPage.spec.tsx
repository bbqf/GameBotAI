import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ExecutionPage } from '../Execution';
import { getRunningSessions, startSession } from '../../services/sessionsApi';
import { listCommands } from '../../services/commands';
import { useGames } from '../../services/useGames';
import { useAdbDevices } from '../../services/useAdbDevices';
import { ApiError } from '../../lib/api';

jest.mock('../../services/sessionsApi');
jest.mock('../../services/commands');
jest.mock('../../services/useGames');
jest.mock('../../services/useAdbDevices');

const getRunningSessionsMock = getRunningSessions as jest.MockedFunction<typeof getRunningSessions>;
const startSessionMock = startSession as jest.MockedFunction<typeof startSession>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;
const useGamesMock = useGames as jest.MockedFunction<typeof useGames>;
const useAdbDevicesMock = useAdbDevices as jest.MockedFunction<typeof useAdbDevices>;

const connectCommand = { id: 'cmd1', name: 'Cmd 1', steps: [{ type: 'Action', targetId: 'c1', order: 0 }] } as any;

describe('ExecutionPage', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    localStorage.clear();
    useGamesMock.mockReturnValue({ data: [{ id: 'g1', name: 'Test Game' }], loading: false, error: undefined } as any);
    useAdbDevicesMock.mockReturnValue({
      devices: [{ serial: 'emulator-5554', state: 'device' }],
      loading: false,
      error: undefined,
      refresh: jest.fn()
    } as any);
    listCommandsMock.mockResolvedValue([connectCommand]);
    getRunningSessionsMock.mockResolvedValue([] as any);
  });

  it('starts a session and surfaces the running entry', async () => {
    const running = {
      sessionId: 's123',
      gameId: 'g1',
      emulatorId: 'emulator-5554',
      startedAtUtc: new Date().toISOString(),
      lastHeartbeatUtc: new Date().toISOString(),
      status: 'Running' as const
    };

    getRunningSessionsMock.mockResolvedValueOnce([] as any).mockResolvedValueOnce([running] as any);
    startSessionMock.mockResolvedValue({ sessionId: 's123', runningSessions: [running] } as any);

    render(<ExecutionPage />);

    const runButton = await screen.findByRole('button', { name: /start session/i });
    await waitFor(() => expect(runButton).not.toBeDisabled());

    fireEvent.click(runButton);

    await waitFor(() => expect(startSessionMock).toHaveBeenCalledWith({ gameId: 'g1', adbSerial: 'emulator-5554' }));
    expect(await screen.findByText(/Session ready: s123/i)).toBeInTheDocument();
    const sessionTexts = await screen.findAllByText(/Session: s123/i);
    expect(sessionTexts.length).toBeGreaterThan(0);
  });

  it('shows an error when start fails', async () => {
    startSessionMock.mockRejectedValue(new ApiError(500, 'Failed to start'));

    render(<ExecutionPage />);

    const runButton = await screen.findByRole('button', { name: /start session/i });
    await waitFor(() => expect(runButton).not.toBeDisabled());
    fireEvent.click(runButton);

    expect(await screen.findByRole('alert')).toHaveTextContent('Failed to start');
  });
});
