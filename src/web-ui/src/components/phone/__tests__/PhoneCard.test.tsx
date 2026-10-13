import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { PhoneCard } from '../PhoneCard';

const renderCard = (props: Partial<React.ComponentProps<typeof PhoneCard>> = {}) =>
  render(<ul><PhoneCard title="Daily queue" {...props} /></ul>);

describe('PhoneCard', () => {
  it('shows the title and the status chip', () => {
    renderCard({ status: <span>Running</span> });
    expect(screen.getByText('Daily queue')).toBeInTheDocument();
    expect(screen.getByText('Running')).toBeInTheDocument();
  });

  it('calls onOpen when the body is tapped', () => {
    const onOpen = jest.fn();
    renderCard({ onOpen });
    fireEvent.click(screen.getByRole('button', { name: 'Open Daily queue' }));
    expect(onOpen).toHaveBeenCalledTimes(1);
  });

  it('has at most one action button and it does not open the card', () => {
    const onOpen = jest.fn();
    const onClick = jest.fn();
    renderCard({ onOpen, action: { label: 'Start', onClick } });
    expect(screen.getAllByRole('button')).toHaveLength(2);
    fireEvent.click(screen.getByRole('button', { name: 'Start' }));
    expect(onClick).toHaveBeenCalledTimes(1);
    expect(onOpen).not.toHaveBeenCalled();
  });

  it('disables the action', () => {
    renderCard({ action: { label: 'Stop', onClick: jest.fn(), disabled: true } });
    expect(screen.getByRole('button', { name: 'Stop' })).toBeDisabled();
  });

  it('keeps the full title in the title attribute so a long title can end with an ellipsis', () => {
    const long = 'A'.repeat(200);
    renderCard({ title: long });
    const el = screen.getByText(long);
    expect(el).toHaveClass('phone-card__title');
    expect(el).toHaveAttribute('title', long);
  });
});
