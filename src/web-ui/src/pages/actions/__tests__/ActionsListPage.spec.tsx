import React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { ActionsListPage } from '../ActionsListPage';
import { useActionTypes } from '../../../services/useActionTypes';
import { useGames } from '../../../services/useGames';
import { listActions, duplicateAction, getAction } from '../../../services/actionsApi';

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
});
