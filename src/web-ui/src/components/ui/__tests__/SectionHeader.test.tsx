import React from 'react';
import { render, screen } from '@testing-library/react';
import { SectionHeader } from '../SectionHeader';

describe('SectionHeader', () => {
  it('shows the title as an h3', () => {
    render(<SectionHeader title="Targets" />);
    expect(screen.getByRole('heading', { level: 3, name: 'Targets' })).toBeInTheDocument();
  });

  it('shows the actions', () => {
    render(<SectionHeader title="Targets" actions={<button type="button">Do it</button>} />);
    expect(screen.getByRole('button', { name: 'Do it' })).toBeInTheDocument();
  });
});
