import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { App } from '../App';

describe('Execution placeholder', () => {
  it('shows empty-state messaging and allows returning', () => {
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Execution' }));
    expect(screen.getByText(/execution workspace/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Authoring' }));
    expect(screen.getByRole('heading', { name: 'Authoring' })).toBeInTheDocument();
  });
});