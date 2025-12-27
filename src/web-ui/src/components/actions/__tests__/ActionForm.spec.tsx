import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { ActionForm, ActionFormValue } from '../ActionForm';
import { ActionType, ValidationMessage } from '../../../types/actions';

describe('ActionForm', () => {
  const actionTypes: ActionType[] = [
    {
      key: 'tap',
      displayName: 'Tap',
      attributeDefinitions: [
        { key: 'x', label: 'X', dataType: 'number', required: true, constraints: { min: 0, max: 100 } },
        { key: 'mode', label: 'Mode', dataType: 'enum', required: true, constraints: { allowedValues: ['fast', 'slow'] } }
      ]
    }
  ];
  const games = [{ id: 'g1', name: 'Test Game' }];
  const baseValue: ActionFormValue = { name: '', gameId: 'g1', type: 'tap', attributes: {} };

  it('renders fields and shows validation errors', () => {
    const errors: ValidationMessage[] = [
      { field: 'name', message: 'Name is required' },
      { field: 'x', message: 'Must be at least 0' },
      { field: 'mode', message: 'Value not allowed' }
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
    expect(screen.getByText('Value not allowed')).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Action Type *' })).toBeInTheDocument();
    expect(screen.getByRole('spinbutton', { name: 'X *' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Mode *' })).toBeInTheDocument();
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

    fireEvent.change(screen.getByLabelText('Mode *'), { target: { value: 'fast' } });
    expect(handleChange).toHaveBeenCalledTimes(2);

    fireEvent.submit(screen.getByRole('form'));
    expect(handleSubmit).toHaveBeenCalled();
  });
});
