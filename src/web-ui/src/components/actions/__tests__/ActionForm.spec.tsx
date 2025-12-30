import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { ActionForm, ActionFormValue } from '../ActionForm';
import { ActionType, ValidationMessage } from '../../../types/actions';
import { useAdbDevices } from '../../../services/useAdbDevices';

jest.mock('../../../services/useAdbDevices');

const useAdbDevicesMock = useAdbDevices as jest.MockedFunction<typeof useAdbDevices>;

describe('ActionForm', () => {
  const actionTypes: ActionType[] = [
    {
      key: 'tap',
      displayName: 'Tap',
      attributeDefinitions: [
        { key: 'x', label: 'X', dataType: 'number', required: true, constraints: { min: 0, max: 100 } },
      ]
    },
    {
      key: 'connect-to-game',
      displayName: 'Connect to game',
      attributeDefinitions: [
        { key: 'adbSerial', label: 'ADB Serial', dataType: 'string', required: true }
      ]
    }
  ];
  const games = [{ id: 'g1', name: 'Test Game' }];
  const baseValue: ActionFormValue = { name: '', gameId: 'g1', type: 'tap', attributes: {} };

  beforeEach(() => {
    useAdbDevicesMock.mockReturnValue({ loading: false, devices: [], refresh: jest.fn() });
  });

  it('renders fields and shows validation errors', () => {
    const errors: ValidationMessage[] = [
      { field: 'name', message: 'Name is required' },
      { field: 'x', message: 'Must be at least 0' },
    ];

    render(
      <ActionForm
        actionTypes={actionTypes}
        games={games as any}
        value={baseValue}
        errors={errors}
        onChange={() => undefined}
      />
    );

    expect(screen.getByLabelText('Game *')).toBeInTheDocument();
    expect(screen.getByLabelText('Name *')).toBeInTheDocument();
    expect(screen.getByText('Name is required')).toBeInTheDocument();
    expect(screen.getByText('Must be at least 0')).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Action Type *' })).toBeInTheDocument();
    expect(screen.getByRole('spinbutton', { name: 'X *' })).toBeInTheDocument();
  });

  it('emits changes and submit handler', () => {
    const handleChange = jest.fn();
    const handleSubmit = jest.fn();

    render(
      <ActionForm
        actionTypes={actionTypes}
        games={games as any}
        value={baseValue}
        errors={[]}
        onChange={handleChange}
        onSubmit={handleSubmit}
      />
    );

    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'New Action' } });
    expect(handleChange).toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText('X *'), { target: { value: '5' } });
    expect(handleChange).toHaveBeenCalledTimes(2);

    fireEvent.submit(screen.getByRole('form'));
    expect(handleSubmit).toHaveBeenCalled();
  });

  it('renders adbSerial suggestions for connect-to-game', () => {
    useAdbDevicesMock.mockReturnValue({
      loading: false,
      error: undefined,
      devices: [{ serial: 'emulator-5554', state: 'device' }],
      refresh: jest.fn()
    });

    render(
      <ActionForm
        actionTypes={actionTypes}
        games={games as any}
        value={{ name: 'c', gameId: 'g1', type: 'connect-to-game', attributes: { adbSerial: '' } }}
        errors={[]}
        onChange={() => undefined}
      />
    );

    expect(screen.getByLabelText('ADB Serial *')).toBeInTheDocument();
    expect(screen.getByDisplayValue('')).toBeInTheDocument();
    expect(screen.getByDisplayValue('')).toHaveAttribute('list');
    expect(screen.getByText(/emulator-5554/)).toBeInTheDocument();
  });

  it('disables save when no games exist', () => {
    render(
      <ActionForm
        actionTypes={actionTypes}
        games={[] as any}
        value={baseValue}
        errors={[]}
        onChange={() => undefined}
      />
    );

    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
    expect(screen.getByText(/No games available/)).toBeInTheDocument();
  });
});
