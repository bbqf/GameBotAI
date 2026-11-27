import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { PageHeader } from '../PageHeader';

describe('PageHeader', () => {
  it('shows the title as a level 2 heading', () => {
    render(<PageHeader title="Queues" />);
    expect(screen.getByRole('heading', { level: 2, name: 'Queues' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Back' })).toBeNull();
  });

  it('shows a back button that calls onBack', () => {
    const onBack = jest.fn();
    render(<PageHeader title="Edit" onBack={onBack} />);
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(onBack).toHaveBeenCalledTimes(1);
  });

  it('shows the status chip and the actions', () => {
    render(<PageHeader title="Edit" status={{ state: 'running' }} actions={<button type="button">Save</button>} />);
    expect(screen.getByRole('status', { name: 'Status: Running' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
  });

  it('gives the title the class that keeps it on one line with an ellipsis', () => {
    render(<PageHeader title="A very long title" />);
    expect(screen.getByRole('heading', { level: 2 })).toHaveClass('ui-page-header__title');
  });
});
