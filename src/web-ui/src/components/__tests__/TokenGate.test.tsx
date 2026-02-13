import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { TokenGate } from '../TokenGate';
import { setToken } from '../../lib/token';
import { setBaseUrl, baseUrl$, getBaseUrl } from '../../lib/config';

jest.mock('../../lib/token', () => ({
  setToken: jest.fn()
}));

jest.mock('../../lib/config', () => ({
  baseUrl$: { subscribe: jest.fn(() => jest.fn()) },
  getBaseUrl: jest.fn(() => 'http://localhost'),
  setBaseUrl: jest.fn()
}));

describe('TokenGate', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    localStorage.clear();
    localStorage.setItem('gamebot.rememberToken', 'false');
  });

  it('updates base url and remembers token when checked', () => {
    const onTokenChange = jest.fn();
    const onRememberChange = jest.fn();
    const onBaseUrlChange = jest.fn();

    render(
      <TokenGate
        token="abc"
        onTokenChange={onTokenChange}
        onRememberChange={onRememberChange}
        onBaseUrlChange={onBaseUrlChange}
      />
    );

    const baseUrlInput = screen.getByLabelText('API Base URL');
    fireEvent.change(baseUrlInput, { target: { value: 'http://api' } });

    expect(getBaseUrl).toHaveBeenCalled();
    expect(setBaseUrl).toHaveBeenCalledWith('http://api');
    expect(onBaseUrlChange).toHaveBeenCalledWith('http://api');
    expect(baseUrl$.subscribe).toHaveBeenCalled();

    const rememberCheckbox = screen.getByRole('checkbox');
    fireEvent.click(rememberCheckbox);

    expect(onRememberChange).toHaveBeenCalled();
    expect(setToken).toHaveBeenCalledWith('abc');
  });

  it('does not persist token when unchecked or empty', () => {
    const onTokenChange = jest.fn();
    const onRememberChange = jest.fn();
    const onBaseUrlChange = jest.fn();

    render(
      <TokenGate
        token=""
        onTokenChange={onTokenChange}
        onRememberChange={onRememberChange}
        onBaseUrlChange={onBaseUrlChange}
      />
    );

    const rememberCheckbox = screen.getByRole('checkbox');
    fireEvent.click(rememberCheckbox);

    expect(onRememberChange).toHaveBeenCalled();
    expect(setToken).not.toHaveBeenCalled();
  });
});
