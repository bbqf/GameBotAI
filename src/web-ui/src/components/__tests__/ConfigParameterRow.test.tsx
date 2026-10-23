import React from 'react';
import { render, screen } from '@testing-library/react';
import { ConfigParameterRow } from '../ConfigParameterRow';
import type { ConfigurationParameter } from '../../services/config';

const param = (over: Partial<ConfigurationParameter>): ConfigurationParameter => ({
  name: 'GAMEBOT_PORT',
  source: 'NetworkFile',
  value: '9090',
  isSecret: false,
  ...over,
});

describe('ConfigParameterRow network rows', () => {
  it.each(['NetworkFile', 'Configuration', 'Environment'] as const)('shows %s as read-only text', (source) => {
    render(<ConfigParameterRow param={param({ source })} onChange={jest.fn()} />);

    expect(screen.getByText('9090')).toBeInTheDocument();
    expect(screen.queryByLabelText('Value for GAMEBOT_PORT')).not.toBeInTheDocument();
    expect(screen.getByText(source)).toBeInTheDocument();
  });

  it('keeps the host and port rows read-only when the source is Default', () => {
    render(<ConfigParameterRow param={param({ name: 'GAMEBOT_BIND_HOST', source: 'Default', value: '127.0.0.1' })} onChange={jest.fn()} />);

    expect(screen.queryByLabelText('Value for GAMEBOT_BIND_HOST')).not.toBeInTheDocument();
    expect(screen.getByText('127.0.0.1')).toBeInTheDocument();
  });

  it('still lets the user edit another row with the Default source', () => {
    render(<ConfigParameterRow param={param({ name: 'GAMEBOT_ADB_RETRIES', source: 'Default', value: 2 })} onChange={jest.fn()} />);

    expect(screen.getByLabelText('Value for GAMEBOT_ADB_RETRIES')).toBeInTheDocument();
  });
});
