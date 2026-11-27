import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { IconButton } from '../IconButton';
import { Icon } from '../Icon';

describe('IconButton', () => {
  it('has an accessible name and a tooltip', () => {
    render(<IconButton icon="edit" label="Edit Daily" title="Edit" />);
    const button = screen.getByRole('button', { name: 'Edit Daily' });
    expect(button).toHaveAttribute('title', 'Edit');
  });

  it('uses the label as the tooltip by default', () => {
    render(<IconButton icon="edit" label="Edit Daily" />);
    expect(screen.getByRole('button', { name: 'Edit Daily' })).toHaveAttribute('title', 'Edit Daily');
  });

  it('calls onClick and does not call it when off', () => {
    const onClick = jest.fn();
    const { rerender } = render(<IconButton icon="play" label="Start" onClick={onClick} />);
    fireEvent.click(screen.getByRole('button', { name: 'Start' }));
    expect(onClick).toHaveBeenCalledTimes(1);
    rerender(<IconButton icon="play" label="Start" onClick={onClick} disabled />);
    expect(screen.getByRole('button', { name: 'Start' })).toBeDisabled();
  });

  it('sets the danger, touch, and extra classes and the menu attributes', () => {
    render(<IconButton icon="delete" label="Delete" tone="danger" size="touch" className="x" testId="t" ariaExpanded={false} ariaHasPopup="menu" />);
    const button = screen.getByRole('button', { name: 'Delete' });
    expect(button).toHaveClass('ui-icon-button', 'ui-icon-button--danger', 'ui-icon-button--touch', 'x');
    expect(button).toHaveAttribute('data-testid', 't');
    expect(button).toHaveAttribute('aria-haspopup', 'menu');
    expect(button).toHaveAttribute('aria-expanded', 'false');
  });

  it('hides the icon from screen readers', () => {
    const { container } = render(<Icon name="play" size={24} />);
    const svg = container.querySelector('svg');
    expect(svg).toHaveAttribute('aria-hidden', 'true');
    expect(svg).toHaveAttribute('width', '24');
  });
});