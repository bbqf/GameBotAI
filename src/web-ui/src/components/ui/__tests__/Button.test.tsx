import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { Button } from '../Button';

describe('Button', () => {
  it('is type button by default and secondary', () => {
    render(<Button>Save</Button>);
    const button = screen.getByRole('button', { name: 'Save' });
    expect(button).toHaveAttribute('type', 'button');
    expect(button).toHaveClass('ui-button', 'ui-button--secondary');
  });

  it.each(['primary', 'secondary', 'danger-outline', 'danger'] as const)('has the class for the variant %s', (variant) => {
    render(<Button variant={variant}>Go</Button>);
    expect(screen.getByRole('button', { name: 'Go' })).toHaveClass(`ui-button--${variant}`);
  });

  it('is off when disabled and does not call onClick', () => {
    const onClick = jest.fn();
    render(<Button disabled onClick={onClick}>Go</Button>);
    fireEvent.click(screen.getByRole('button', { name: 'Go' }));
    expect(onClick).not.toHaveBeenCalled();
  });

  it('shows an icon and calls onClick', () => {
    const onClick = jest.fn();
    const { container } = render(<Button icon="add" onClick={onClick} testId="b">Create</Button>);
    expect(container.querySelector('svg')).not.toBeNull();
    fireEvent.click(screen.getByTestId('b'));
    expect(onClick).toHaveBeenCalledTimes(1);
  });
});