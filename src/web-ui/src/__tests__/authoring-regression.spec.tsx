import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { App } from '../App';

describe('Authoring click depth', () => {
  it('reaches Commands in one click from landing', async () => {
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Commands' }));
    expect(await screen.findByRole('heading', { name: 'Commands' })).toBeInTheDocument();
  });
});