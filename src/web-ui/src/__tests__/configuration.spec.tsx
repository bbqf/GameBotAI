import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { App } from '../App';

describe('Configuration area', () => {
  it('shows host/token controls at the top', () => {
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    expect(screen.getByLabelText('API Base URL')).toBeInTheDocument();
    expect(screen.getByLabelText('Bearer Token')).toBeInTheDocument();
  });
});