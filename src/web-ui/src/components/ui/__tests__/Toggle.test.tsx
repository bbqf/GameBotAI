import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { Toggle } from '../Toggle';

describe('Toggle', () => {
  it('is a switch with the label as its name', () => {
    render(<Toggle checked label="Enabled" onChange={jest.fn()} />);
    const sw = screen.getByRole('switch', { name: 'Enabled' });
    expect(sw).toHaveAttribute('aria-checked', 'true');
  });

  it('shows the off state', () => {
    render(<Toggle checked={false} label="Enabled" onChange={jest.fn()} />);
    expect(screen.getByRole('switch', { name: 'Enabled' })).toHaveAttribute('aria-checked', 'false');
  });

  it('calls onChange for a click on the switch and on the label', () => {
    const onChange = jest.fn();
    render(<Toggle checked={false} label="Enabled" onChange={onChange} />);
    fireEvent.click(screen.getByRole('switch', { name: 'Enabled' }));
    fireEvent.click(screen.getByText('Enabled'));
    expect(onChange).toHaveBeenCalledTimes(2);
    expect(onChange).toHaveBeenCalledWith(true);
  });

  it('does not change when it is disabled', () => {
    const onChange = jest.fn();
    render(<Toggle checked label="Enabled" disabled onChange={onChange} />);
    fireEvent.click(screen.getByRole('switch', { name: 'Enabled' }));
    fireEvent.click(screen.getByText('Enabled'));
    expect(onChange).not.toHaveBeenCalled();
  });

  it('is a native button, so Space and Enter work on the focused switch', () => {
    render(<Toggle checked label="Enabled" onChange={jest.fn()} />);
    const sw = screen.getByRole('switch', { name: 'Enabled' });
    expect(sw.tagName).toBe('BUTTON');
    sw.focus();
    expect(sw).toHaveFocus();
  });
});
