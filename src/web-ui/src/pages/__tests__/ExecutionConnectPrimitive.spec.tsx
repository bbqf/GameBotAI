import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ExecutionPage } from '../Execution';
import { getRunningSessions, startSession } from '../../services/sessionsApi';
import { listCommands } from '../../services/commands';
import { listSequences } from '../../services/sequences';
import { useGames } from '../../services/useGames';
import { useAdbDevices } from '../../services/useAdbDevices';

jest.mock('../../services/sessionsApi');
jest.mock('../../services/commands');
jest.mock('../../services/sequences');
jest.mock('../../services/useGames');
jest.mock('../../services/useAdbDevices');

const getRunningSessionsMock = getRunningSessions as jest.MockedFunction<typeof getRunningSessions>;
const startSessionMock = startSession as jest.MockedFunction<typeof startSession>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;
const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;
const useGamesMock = useGames as jest.MockedFunction<typeof useGames>;
const useAdbDevicesMock = useAdbDevices as jest.MockedFunction<typeof useAdbDevices>;

describe('Execution connect primitive UX', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    useGamesMock.mockReturnValue({ data: [{ id: 'g1', name: 'Game 1' }], loading: false, error: undefined } as any);
    useAdbDevicesMock.mockReturnValue({
      devices: [{ serial: 'emu-1', state: 'device' }],
      loading: false,
      error: undefined,
      refresh: jest.fn()
    } as any);
    getRunningSessionsMock.mockResolvedValue([] as any);
    listCommandsMock.mockResolvedValue([] as any);
    listSequencesMock.mockResolvedValue([] as any);
  });

  it('submits game and adb serial through startSession', async () => {
    startSessionMock.mockResolvedValue({ sessionId: 'sess-1', runningSessions: [] } as any);

    render(<ExecutionPage />);

    const button = await screen.findByRole('button', { name: /start session/i });
    fireEvent.click(button);

    await waitFor(() => expect(startSessionMock).toHaveBeenCalledWith({ gameId: 'g1', adbSerial: 'emu-1' }));
    expect(await screen.findByText(/Session ready: sess-1/i)).toBeInTheDocument();
  });

  it('disables session start when selection data is unavailable', async () => {
    useGamesMock.mockReturnValue({ data: [], loading: false, error: undefined } as any);
    useAdbDevicesMock.mockReturnValue({ devices: [], loading: false, error: undefined, refresh: jest.fn() } as any);

    render(<ExecutionPage />);

    const button = await screen.findByRole('button', { name: /start session/i });
    expect(button).toBeDisabled();

    expect(await screen.findByText('No games available.')).toBeInTheDocument();
    expect(await screen.findByText('No adb devices detected.')).toBeInTheDocument();
    expect(startSessionMock).not.toHaveBeenCalled();
  });
});
