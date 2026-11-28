import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { App } from '../App';

jest.mock('../pages/CommandsPage', () => ({ CommandsPage: () => <div role="heading" aria-level={2}>Commands</div> }));
jest.mock('../pages/GamesPage', () => ({ GamesPage: () => <div role="heading" aria-level={2}>Games</div> }));
jest.mock('../pages/SequencesPage', () => ({ SequencesPage: () => <div role="heading" aria-level={2}>Sequences</div> }));
jest.mock('../pages/images/ImagesListPage', () => ({ ImagesListPage: (_: any) => <div role="heading" aria-level={2}>Images</div> }));
jest.mock('../pages/MonitoringPage', () => ({
  MONITORING_AREA_PATH: '/monitoring',
  MonitoringPage: () => <div data-testid="monitoring-page-mock">Monitoring content</div>
}));
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

    fireEvent.click(screen.getByRole('tab', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    expect(screen.getByRole('heading', { name: 'Configuration' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Settings' })).toHaveAttribute('aria-selected', 'true');
  });

  it('keeps the desktop address rules: replaceState only, tab and area set, create and id removed (H-07, T-01 to T-03)', () => {
    setMatchMedia(false);
    window.history.replaceState(null, '', '/?tab=Images&create=commands&id=abc&keep=1');
    const push = jest.spyOn(window.history, 'pushState');
    const replace = jest.spyOn(window.history, 'replaceState');
    render(<App />);
    expect(window.location.search).toContain('tab=Images');
    expect(window.location.search).toContain('area=authoring');
    expect(window.location.search).toContain('keep=1');
    expect(window.location.search).not.toContain('create=');
    expect(window.location.search).not.toContain('id=');

    fireEvent.click(screen.getByRole('tab', { name: 'Games' }));
    expect(window.location.search).toContain('tab=Games');
    fireEvent.click(screen.getByRole('tab', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    expect(window.location.search).toContain('area=configuration');
    expect(replace).toHaveBeenCalled();
    expect(push).not.toHaveBeenCalled();
    push.mockRestore();
    replace.mockRestore();
    window.history.replaceState(null, '', '/');
  });

  it('renders the bottom tab bar on narrow screens and is keyboard/focus usable', () => {
    setMatchMedia(true);
    render(<App />);
    expect(screen.queryByLabelText('Navigation menu')).toBeNull();
    const more = screen.getByRole('button', { name: 'More' });
    more.focus();
    expect(more).toHaveFocus();
    fireEvent.click(more);
    fireEvent.click(screen.getByRole('button', { name: 'Configuration' }));
    expect(screen.getByRole('heading', { name: 'Configuration' })).toBeInTheDocument();
  });

  it('opens Monitoring from the tab, from ?area=monitoring and from /monitoring (feature 141)', () => {
    setMatchMedia(false);
    const { unmount } = render(<App />);
    const tabs = screen.getAllByRole('tab').map((t) => t.textContent?.trim());
    expect(tabs.indexOf('Monitor')).toBe(tabs.indexOf('Queues') + 1);
    fireEvent.click(screen.getByRole('tab', { name: 'Monitor' }));
    expect(screen.getByTestId('monitoring-page-mock')).toBeInTheDocument();
    expect(window.location.search).toContain('area=monitoring');
    unmount();

    window.history.replaceState(null, '', '/?area=monitoring');
    const second = render(<App />);
    expect(screen.getByTestId('monitoring-page-mock')).toBeInTheDocument();
    second.unmount();

    window.history.replaceState(null, '', '/monitoring');
    render(<App />);
    expect(screen.getByTestId('monitoring-page-mock')).toBeInTheDocument();
    window.history.replaceState(null, '', '/');
  });

  it('opens Monitor from the phone tab bar (feature 141)', () => {
    setMatchMedia(true);
    window.history.replaceState(null, '', '/');
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: 'Monitor' }));
    expect(screen.getByTestId('monitoring-page-mock')).toBeInTheDocument();
  });
});

describe('Desktop Settings and Authoring sub-tabs (feature 205)', () => {
  beforeEach(() => {
    setMatchMedia(false);
    window.history.replaceState(null, '', '/');
  });

  afterEach(() => {
    window.history.replaceState(null, '', '/');
  });

  it('opens Execution logs from the Settings tab and shows 5 sub-tabs', () => {
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Settings' }));
    const nav = screen.getByRole('navigation', { name: 'Settings Navigation' });
    const labels = within(nav).getAllByRole('tab').map((t) => t.textContent);
    expect(labels).toEqual(['Execution logs', 'Execution', 'Configuration', 'Gamepacks', 'Update']);
    expect(within(nav).getByRole('tab', { name: 'Execution logs' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('heading', { name: 'Execution logs' })).toBeInTheDocument();
  });

  it('keeps Settings selected when a sub-tab opens', () => {
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    expect(screen.getByRole('tab', { name: 'Settings' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'Configuration' })).toHaveAttribute('aria-selected', 'true');
  });

  it.each(['configuration', 'update', 'gamepacks', 'execution', 'execution-logs'])(
    'opens Settings and the matching sub-tab for ?area=%s',
    (area) => {
      window.history.replaceState(null, '', `/?area=${area}`);
      render(<App />);
      expect(screen.getByRole('tab', { name: 'Settings' })).toHaveAttribute('aria-selected', 'true');
      const nav = screen.getByRole('navigation', { name: 'Settings Navigation' });
      const selected = within(nav).getAllByRole('tab').filter((t) => t.getAttribute('aria-selected') === 'true');
      expect(selected).toHaveLength(1);
      expect(window.location.search).toContain(`area=${area}`);
    }
  );

  it('opens Execution logs for ?area=more', () => {
    window.history.replaceState(null, '', '/?area=more');
    render(<App />);
    expect(screen.getByRole('tab', { name: 'Execution logs' })).toHaveAttribute('aria-selected', 'true');
    expect(window.location.search).toContain('area=execution-logs');
  });

  it('shows the Authoring sub-tabs and no Authoring heading', () => {
    render(<App />);
    const nav = screen.getByRole('navigation', { name: 'Authoring Navigation' });
    expect(within(nav).getAllByRole('tab').map((t) => t.textContent)).toEqual(['Commands', 'Games', 'Sequences', 'Images', 'Backup & Restore']);
    expect(screen.queryByRole('heading', { name: 'Authoring' })).toBeNull();
    expect(screen.queryByText('GameBot Web UI')).toBeNull();
  });

  it('shows the 5 top tabs with the new names', () => {
    render(<App />);
    const labels = within(screen.getByRole('navigation', { name: 'Primary Navigation' })).getAllByRole('tab').map((t) => t.textContent);
    expect(labels).toEqual(['Queues', 'Monitor', 'Alerts', 'Authoring', 'Settings']);
  });
});

describe('Phone More page (feature 205)', () => {
  beforeEach(() => {
    setMatchMedia(true);
    window.history.replaceState(null, '', '/');
  });

  afterEach(() => {
    window.history.replaceState(null, '', '/');
  });

  it('opens the More page from the More tab, with no back arrow', () => {
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: 'More' }));
    expect(screen.getByRole('button', { name: 'More' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByText('Desktop only')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Back' })).toBeNull();
    expect(window.location.search).toContain('area=more');
  });

  it('opens the More page for ?area=more', () => {
    window.history.replaceState(null, '', '/?area=more');
    render(<App />);
    expect(screen.getByRole('button', { name: 'Configuration' })).toBeInTheDocument();
  });

  it('opens Configuration with a Back arrow that returns to More', () => {
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: 'More' }));
    fireEvent.click(screen.getByRole('button', { name: 'Configuration' }));
    expect(screen.getByRole('button', { name: 'More' })).toHaveAttribute('aria-current', 'page');
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByText('Desktop only')).toBeInTheDocument();
  });
});
