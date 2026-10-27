import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { KeyInputPanel } from '../KeyInputPanel';

describe('KeyInputPanel', () => {
  it('shows a digit example and a raw key code example in the placeholder', () => {
    render(<KeyInputPanel onConfirm={() => {}} onCancel={() => {}} />);
    const placeholder = screen.getByLabelText(/key identifier/i).getAttribute('placeholder') ?? '';
    expect(placeholder).toContain('6');
    expect(placeholder).toContain('85');
  });

  it('shows the digit and raw key code rule in a help line', () => {
    render(<KeyInputPanel onConfirm={() => {}} onCancel={() => {}} />);
    const help = screen.getByText(/raw Android key code/i);
    expect(help).toHaveTextContent('0 to 9');
    expect(help).toHaveTextContent('NUMPAD_0');
    expect(help).toHaveTextContent('0 to 300');
  });

  it.each([['6'], ['85'], ['NUMPAD_6']])('sends the trimmed key %s when Add is clicked', (key) => {
    const onConfirm = jest.fn();
    render(<KeyInputPanel onConfirm={onConfirm} onCancel={() => {}} />);
    fireEvent.change(screen.getByLabelText(/key identifier/i), { target: { value: `  ${key}  ` } });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm).toHaveBeenCalledWith({ key });
  });

  it('shows an error and sends nothing when the key is empty', () => {
    const onConfirm = jest.fn();
    render(<KeyInputPanel onConfirm={onConfirm} onCancel={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Key identifier is required.');
    expect(onConfirm).not.toHaveBeenCalled();
  });
});
