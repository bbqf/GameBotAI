import React from 'react';
import { render, screen } from '@testing-library/react';
import { StatusChip } from '../StatusChip';

describe('StatusChip', () => {
  it('shows running status with styling', () => {
    render(<StatusChip status="Running" />);

    const chip = screen.getByRole('status', { name: /Status: Running/i });
    expect(chip).toHaveClass('status-chip--running');
    expect(chip).toHaveTextContent('Running');
  });

  it('falls back to unknown when status is missing', () => {
    render(<StatusChip />);

    const chip = screen.getByRole('status', { name: /Status: Unknown/i });
    expect(chip).toHaveClass('status-chip--unknown');
    expect(chip).toHaveTextContent('Unknown');
  });
});
