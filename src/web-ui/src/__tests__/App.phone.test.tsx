import React from 'react';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { App } from '../App';
import { mockViewport, ViewportMock } from '../testUtils/viewport';
import * as queuesApi from '../services/queues';

jest.mock('../services/queues');
jest.mock('../pages/Configuration', () => ({
  CONFIGURATION_AREA_PATH: '/configuration',
  ConfigurationPage: () => <input aria-label="mock token field" />
}));
jest.mock('../pages/CommandsPage', () => ({ CommandsPage: () => <div>Commands page</div> }));
jest.mock('../pages/QueuesPage', () => ({ QueuesPage: () => <div data-testid="desktop-queues-page">Desktop queues</div> }));

const queuesMock = queuesApi as jest.Mocked<typeof queuesApi>;

describe('App in phone mode', () => {
  let mock: ViewportMock;
  const originalFetch = global.fetch;

  beforeEach(() => {
    window.history.replaceState(null, '', '/');
    queuesMock.listQueues.mockResolvedValue([]);
    global.fetch = jest.fn().mockResolvedValue({ ok: true, status: 200 }) as unknown as typeof fetch;
  });

  afterEach(() => {
    mock?.restore();
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  it('opens Queues by default and shows the phone shell, not the desktop navigation', async () => {
    mock = mockViewport(360, 640);
    render(<App />);
    expect(await screen.findByTestId('phone-queues')).toBeInTheDocument();
    expect(screen.queryByTestId('desktop-queues-page')).toBeNull();
    expect(screen.getByRole('navigation', { name: 'Primary Navigation' })).toHaveClass('phone-tabbar');
    expect(screen.queryByRole('tab', { name: 'Authoring' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Queues' })).toHaveAttribute('aria-current', 'page');
    expect(window.location.search).toContain('area=queues');
  });

  it('lets the area parameter win over the phone default', async () => {
    window.history.replaceState(null, '', '/?area=configuration');
    mock = mockViewport(360, 640);
    render(<App />);
    expect(await screen.findByLabelText('mock token field')).toBeInTheDocument();
  });

  it('opens Authoring by default on a desktop screen', () => {
    mock = mockViewport(1280, 800);
    render(<App />);
    expect(screen.getByRole('tab', { name: 'Authoring' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.queryByTestId('phone-queues')).toBeNull();
  });

  it('uses the desktop queues page on a desktop screen', () => {
    window.history.replaceState(null, '', '/?area=queues');
    mock = mockViewport(1280, 800);
    render(<App />);
    expect(screen.getByTestId('desktop-queues-page')).toBeInTheDocument();
    expect(queuesMock.listQueues).not.toHaveBeenCalled();
  });

  it('navigates with the tab bar', async () => {
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByTestId('phone-queues');
    fireEvent.click(screen.getByRole('button', { name: /More/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Configuration' }));
    expect(await screen.findByLabelText('mock token field')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Configuration', level: 1 })).toBeInTheDocument();
  });

  it('switches mode without a reload and keeps the form state of the area', async () => {
    window.history.replaceState(null, '', '/?area=configuration');
    mock = mockViewport(1280, 800);
    render(<App />);
    const field = screen.getByLabelText('mock token field') as HTMLInputElement;
    fireEvent.change(field, { target: { value: 'typed' } });
    expect(field.value).toBe('typed');
    expect(screen.getByRole('tab', { name: 'Configuration' })).toBeInTheDocument();

    act(() => mock.setViewport(360, 640));
    expect(screen.queryByRole('tab', { name: 'Configuration' })).toBeNull();
    expect(screen.getByRole('navigation', { name: 'Primary Navigation' })).toHaveClass('phone-tabbar');
    // Same DOM node: the area did not remount.
    expect(screen.getByLabelText('mock token field')).toBe(field);
    expect((screen.getByLabelText('mock token field') as HTMLInputElement).value).toBe('typed');

    act(() => mock.setViewport(1280, 800));
    expect(screen.getByRole('tab', { name: 'Configuration' })).toBeInTheDocument();
    expect(screen.getByLabelText('mock token field')).toBe(field);
  });

  it('shows the connection banner when the service does not answer', async () => {
    global.fetch = jest.fn().mockRejectedValue(new Error('down')) as unknown as typeof fetch;
    mock = mockViewport(360, 640);
    render(<App />);
    expect(await screen.findByText('Connection lost')).toBeInTheDocument();
  });
});
