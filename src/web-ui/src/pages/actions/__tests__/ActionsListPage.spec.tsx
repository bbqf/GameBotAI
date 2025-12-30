import React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { ActionsListPage } from '../ActionsListPage';
import { useActionTypes } from '../../../services/useActionTypes';
import { useGames } from '../../../services/useGames';
import { listActions, duplicateAction, getAction, updateAction } from '../../../services/actionsApi';

jest.mock('../../../services/useAdbDevices', () => ({ useAdbDevices: () => ({ loading: false, devices: [], refresh: jest.fn() }) }));

jest.mock('../../../services/useActionTypes');
jest.mock('../../../services/useGames');
jest.mock('../../../services/actionsApi');

type UseActionTypesMock = jest.MockedFunction<typeof useActionTypes>;
const useActionTypesMock = useActionTypes as unknown as UseActionTypesMock;
type UseGamesMock = jest.MockedFunction<typeof useGames>;
const useGamesMock = useGames as unknown as UseGamesMock;
const listActionsMock = listActions as jest.MockedFunction<typeof listActions>;
const duplicateActionMock = duplicateAction as jest.MockedFunction<typeof duplicateAction>;
const getActionMock = getAction as jest.MockedFunction<typeof getAction>;
const updateActionMock = updateAction as jest.MockedFunction<typeof updateAction>;

describe('ActionsListPage', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    useActionTypesMock.mockReturnValue({
      loading: false,
      error: undefined,
      data: { version: 'v1', items: [{ key: 'tap', displayName: 'Tap', attributeDefinitions: [] }] }
    } as any);
    useGamesMock.mockReturnValue({ loading: false, error: undefined, data: [{ id: 'g1', name: 'Test Game' }] });
  });

  it('lists actions and duplicates', async () => {
    listActionsMock.mockResolvedValue([{ id: '1', name: 'Tap here', gameId: 'g1', type: 'tap', attributes: {} } as any]);
    getActionMock.mockResolvedValue({ id: '1', name: 'Tap here', gameId: 'g1', type: 'tap', attributes: {} } as any);
    duplicateActionMock.mockResolvedValue({} as any);

    render(<ActionsListPage />);

    expect(await screen.findByText('Tap here')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Tap here'));

    const duplicateButton = await screen.findByText('Duplicate');
    fireEvent.click(duplicateButton);

    await waitFor(() => expect(duplicateActionMock).toHaveBeenCalledWith('1'));
  });

  it('coerces string numeric attributes when saving a tap', async () => {
    useActionTypesMock.mockReturnValue({
      loading: false,
      error: undefined,
      data: { version: 'v1', items: [{ key: 'tap', displayName: 'Tap', attributeDefinitions: [
        { key: 'x', label: 'X', dataType: 'number', required: true },
        { key: 'y', label: 'Y', dataType: 'number', required: true }
      ] }] }
    } as any);

    listActionsMock.mockResolvedValue([{ id: '1', name: 'Tap here', gameId: 'g1', type: 'tap', attributes: { x: '10', y: '20' } } as any]);
    getActionMock.mockResolvedValue({ id: '1', name: 'Tap here', gameId: 'g1', type: 'tap', attributes: { x: '10', y: '20' } } as any);
    updateActionMock.mockResolvedValue({} as any);

    render(<ActionsListPage />);

    fireEvent.click(await screen.findByText('Tap here'));

    const saveButton = await screen.findByRole('button', { name: 'Save' });
    fireEvent.click(saveButton);

    await waitFor(() => {
      expect(updateActionMock).toHaveBeenCalledWith('1', {
        name: 'Tap here',
        gameId: 'g1',
        type: 'tap',
        attributes: { x: 10, y: 20 }
      });
    });
  });

  it('returns to list after saving', async () => {
    listActionsMock.mockResolvedValue([{ id: '1', name: 'Tap here', gameId: 'g1', type: 'tap', attributes: {} } as any]);
    getActionMock.mockResolvedValue({ id: '1', name: 'Tap here', gameId: 'g1', type: 'tap', attributes: {} } as any);
    updateActionMock.mockResolvedValue({} as any);

    render(<ActionsListPage />);

    fireEvent.click(await screen.findByText('Tap here'));

    fireEvent.click(await screen.findByRole('button', { name: 'Save' }));

    await waitFor(() => {
      expect(updateActionMock).toHaveBeenCalledTimes(1);
      expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
    });

    expect(await screen.findByText('Action updated successfully.')).toBeInTheDocument();
  });

  it('prefills create form when copying', async () => {
    listActionsMock.mockResolvedValue([{ id: '2', name: 'Original', gameId: 'g1', type: 'tap', attributes: { x: 5 } } as any]);
    getActionMock.mockResolvedValue({ id: '2', name: 'Original', gameId: 'g1', type: 'tap', attributes: { x: 5 } } as any);
    duplicateActionMock.mockResolvedValue({} as any);

    render(<ActionsListPage />);

    await screen.findByText('Original');
    fireEvent.click(screen.getByText('Original'));

    fireEvent.click(await screen.findByText('Create from copy'));

    expect(await screen.findByLabelText('Name *')).toHaveValue('Original copy');
    expect(screen.getByLabelText('Action Type *')).toHaveValue('tap');
    expect(screen.getByLabelText('Game *')).toHaveValue('g1');
  });

  it('persists gameId in attributes for connect-to-game actions', async () => {
    useActionTypesMock.mockReturnValue({
      loading: false,
      error: undefined,
      data: { version: 'v1', items: [{ key: 'connect-to-game', displayName: 'Connect', attributeDefinitions: [{ key: 'adbSerial', label: 'ADB', dataType: 'string', required: true }] }] }
    } as any);

    listActionsMock.mockResolvedValue([{ id: 'c1', name: 'Connect', gameId: 'g1', type: 'connect-to-game', attributes: { adbSerial: 'emulator-5554' } } as any]);
    getActionMock.mockResolvedValue({ id: 'c1', name: 'Connect', gameId: 'g1', type: 'connect-to-game', attributes: { adbSerial: 'emulator-5554' } } as any);
    updateActionMock.mockResolvedValue({} as any);

    render(<ActionsListPage />);

    const connectButton = await screen.findByRole('button', { name: 'Connect' });
    fireEvent.click(connectButton);
    const saveButton = await screen.findByRole('button', { name: 'Save' });
    fireEvent.click(saveButton);

    await waitFor(() => {
      expect(updateActionMock).toHaveBeenCalledWith('c1', {
        name: 'Connect',
        gameId: 'g1',
        type: 'connect-to-game',
        attributes: { adbSerial: 'emulator-5554', gameId: 'g1' }
      });
    });
  });
});
