import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { App } from '../App';

jest.mock('../pages/CommandsPage', () => ({ CommandsPage: () => <div role="heading" aria-level={2}>Commands</div> }));
jest.mock('../pages/GamesPage', () => ({ GamesPage: () => <div role="heading" aria-level={2}>Games</div> }));
jest.mock('../pages/SequencesPage', () => ({ SequencesPage: () => <div role="heading" aria-level={2}>Sequences</div> }));
jest.mock('../pages/images/ImagesListPage', () => ({ ImagesListPage: (_: any) => <div role="heading" aria-level={2}>Images</div> }));
jest.mock('../pages/Configuration', () => ({
  CONFIGURATION_AREA_PATH: '/configuration',
  ConfigurationPage: () => <div data-testid="configuration-page-mock">Configuration content</div>
}));

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

    fireEvent.click(screen.getByRole('tab', { name: 'Images' }));
    expect(screen.getByRole('heading', { name: 'Images' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    expect(screen.getByRole('heading', { name: 'Configuration' })).toBeInTheDocument();
  });

  it('renders the bottom tab bar on narrow screens and is keyboard/focus usable', () => {
    setMatchMedia(true);
    render(<App />);
    expect(screen.queryByLabelText('Navigation menu')).toBeNull();
    const more = screen.getByRole('button', { name: /More/ });
    more.focus();
    expect(more).toHaveFocus();
    fireEvent.click(more);
    fireEvent.click(screen.getByRole('button', { name: 'Configuration' }));
    expect(screen.getByRole('heading', { name: 'Configuration' })).toBeInTheDocument();
  });
});