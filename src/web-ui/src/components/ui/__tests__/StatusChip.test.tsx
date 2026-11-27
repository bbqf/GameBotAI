import React from 'react';
import { render, screen } from '@testing-library/react';
import { StatusChip, type ChipState } from '../StatusChip';

describe('StatusChip', () => {
  it.each(['running', 'paused', 'stopped', 'failed', 'done', 'unknown'] as ChipState[])('shows the state %s', (state) => {
    render(<StatusChip state={state} />);
    const word = state.charAt(0).toUpperCase() + state.slice(1);
    const chip = screen.getByRole('status', { name: `Status: ${word}` });
    expect(chip).toHaveClass('ui-chip', `ui-chip--${state}`);
    expect(chip).toHaveTextContent(word);
  });

  it('shows the given text and test id', () => {
    render(<StatusChip state="paused" text="Pausing" testId="queue-status" />);
    expect(screen.getByTestId('queue-status')).toHaveTextContent('Pausing');
  });
});