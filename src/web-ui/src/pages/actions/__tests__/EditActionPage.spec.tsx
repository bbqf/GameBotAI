import React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { EditActionPage } from '../EditActionPage';
import { useActionTypes } from '../../../services/useActionTypes';
import { useGames } from '../../../services/useGames';
import { getAction, updateAction } from '../../../services/actionsApi';

jest.mock('../../../services/useActionTypes');
jest.mock('../../../services/useGames');
jest.mock('../../../services/actionsApi');

type ActionTypeMock = jest.MockedFunction<typeof useActionTypes>;
const useActionTypesMock = useActionTypes as unknown as ActionTypeMock;
type UseGamesMock = jest.MockedFunction<typeof useGames>;
const useGamesMock = useGames as unknown as UseGamesMock;
const getActionMock = getAction as jest.MockedFunction<typeof getAction>;
const updateActionMock = updateAction as jest.MockedFunction<typeof updateAction>;

describe('EditActionPage', () => {
  beforeEach(() => {
    jest.resetAllMocks();
  });

  it('loads existing action and updates on submit', async () => {
    useActionTypesMock.mockReturnValue({
      loading: false,
      error: undefined,
      data: {
        version: 'v1',
        items: [{ key: 'tap', displayName: 'Tap', attributeDefinitions: [{ key: 'x', label: 'X', dataType: 'number', required: true }] }]
      }
    } as any);

    useGamesMock.mockReturnValue({ loading: false, error: undefined, data: [{ id: 'g1', name: 'Test Game' }] });

    getActionMock.mockResolvedValue({ id: '1', name: 'Tap here', gameId: 'g1', type: 'tap', attributes: { x: 5 } } as any);
    updateActionMock.mockResolvedValue({} as any);

    render(<EditActionPage actionId="1" />);

    const nameInput = await screen.findByLabelText('Name *');
    expect(nameInput).toHaveValue('Tap here');

    fireEvent.change(nameInput, { target: { value: 'New name' } });
    fireEvent.submit(screen.getByRole('form'));

    await waitFor(() => expect(updateActionMock).toHaveBeenCalled());
    expect(updateActionMock).toHaveBeenCalledWith('1', { name: 'New name', gameId: 'g1', type: 'tap', attributes: { x: 5 } });
  });

  it('confirms type change and drops incompatible fields', async () => {
    useActionTypesMock.mockReturnValue({
      loading: false,
      error: undefined,
      data: {
        version: 'v1',
        items: [
          { key: 'tap', displayName: 'Tap', attributeDefinitions: [{ key: 'x', label: 'X', dataType: 'number', required: true }] },
          { key: 'swipe', displayName: 'Swipe', attributeDefinitions: [{ key: 'distance', label: 'Distance', dataType: 'number', required: true }] }
        ]
      }
    } as any);

    useGamesMock.mockReturnValue({ loading: false, error: undefined, data: [{ id: 'g2', name: 'Another Game' }] });

    getActionMock.mockResolvedValue({ id: '2', name: 'Tap there', gameId: 'g2', type: 'tap', attributes: { x: 10 } } as any);
    updateActionMock.mockResolvedValue({} as any);

    const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(true);

    render(<EditActionPage actionId="2" />);

    await screen.findByLabelText('Name *');

    fireEvent.change(screen.getByLabelText('Action Type *'), { target: { value: 'swipe' } });

    expect(confirmSpy).toHaveBeenCalled();
    expect(screen.queryByLabelText('X *')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Distance *')).toBeInTheDocument();
  });
});
