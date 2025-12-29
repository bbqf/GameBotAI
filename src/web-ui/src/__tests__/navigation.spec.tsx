import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { App } from '../App';

const setMatchMedia = (matches: boolean) => {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: jest.fn().mockImplementation((query: string) => ({
      matches,
      media: query,
      onchange: null,
      addEventListener: jest.fn(),
      removeEventListener: jest.fn(),
      dispatchEvent: jest.fn()
    }))
  });
};

describe('Top navigation', () => {
  it('shows active area and switches on click', () => {
    setMatchMedia(false);
    render(<App />);
    const authoringTab = screen.getByRole('tab', { name: 'Authoring' });
    expect(authoringTab).toHaveAttribute('aria-selected', 'true');

    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    expect(screen.getByRole('heading', { name: 'Configuration' })).toBeInTheDocument();
  });

  it('renders collapsed select on narrow screens and is keyboard/focus usable', () => {
    setMatchMedia(true);
    render(<App />);
    const select = screen.getByLabelText('Navigation menu');
    select.focus();
    expect(select).toHaveFocus();
    fireEvent.change(select, { target: { value: 'configuration' } });
    expect(screen.getByRole('heading', { name: 'Configuration' })).toBeInTheDocument();
  });
});