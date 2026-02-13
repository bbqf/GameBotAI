import React from 'react';
import { render, screen } from '@testing-library/react';
import { ExecutionPage } from '../../../pages/Execution';
import { listActions } from '../../../services/actionsApi';
import { useGames } from '../../../services/useGames';
import { listCommands } from '../../../services/commands';
import { getRunningSessions } from '../../../services/sessionsApi';

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
const mockGetRunningSessions = getRunningSessions as jest.MockedFunction<typeof getRunningSessions>;

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

describe('Execution zoom layout', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUseGames.mockReturnValue({ loading: false, data: [{ id: 'game-1', name: 'Test Game' }] });
    mockListActions.mockResolvedValue([connectAction as any]);
    mockListCommands.mockResolvedValue([commandWithConnectStep as any]);
    mockGetRunningSessions.mockResolvedValue([] as any);
  });

  it('renders core controls at 125% zoom without missing actions', async () => {
    render(
      <div style={{ width: '1280px', zoom: 1.25 }}>
        <ExecutionPage />
      </div>
    );

    await screen.findByRole('button', { name: 'Start session' });
    await screen.findByRole('button', { name: 'Execute command' });
  });

  it('renders core controls at 150% zoom without missing actions', async () => {
    render(
      <div style={{ width: '1280px', zoom: 1.5 }}>
        <ExecutionPage />
      </div>
    );

    await screen.findByRole('button', { name: 'Start session' });
    await screen.findByRole('button', { name: 'Execute command' });
  });
});
