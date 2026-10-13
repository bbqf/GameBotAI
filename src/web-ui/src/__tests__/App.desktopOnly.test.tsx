import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { App } from '../App';
import { mockViewport, ViewportMock } from '../testUtils/viewport';
import * as queuesApi from '../services/queues';

jest.mock('../services/queues');
const authoringMounted = jest.fn();
const logsMounted = jest.fn();
jest.mock('../pages/CommandsPage', () => ({ CommandsPage: () => { authoringMounted(); return <div>Commands page</div>; } }));
jest.mock('../pages/ExecutionLogs', () => ({ ExecutionLogsPage: () => { logsMounted(); return <div>Logs page</div>; } }));

const queuesMock = queuesApi as jest.Mocked<typeof queuesApi>;

describe('Desktop-only areas', () => {
  let mock: ViewportMock;
  const originalFetch = global.fetch;

  beforeEach(() => {
    queuesMock.listQueues.mockResolvedValue([]);
    global.fetch = jest.fn().mockResolvedValue({ ok: true, status: 200 }) as unknown as typeof fetch;
  });

  afterEach(() => {
    mock?.restore();
    global.fetch = originalFetch;
    jest.clearAllMocks();
    window.history.replaceState(null, '', '/');
  });

  it('shows the notice for Authoring on a phone (deep link) and never mounts the page', async () => {
    window.history.replaceState(null, '', '/?area=authoring');
    mock = mockViewport(360, 640);
    render(<App />);
    expect(screen.getByRole('heading', { name: 'Authoring', level: 2 })).toBeInTheDocument();
    expect(screen.getByText('Use a larger screen for this area.')).toBeInTheDocument();
    expect(authoringMounted).not.toHaveBeenCalled();
  });

  it('shows the notice for Execution Logs on the /execution-logs path', () => {
    window.history.replaceState(null, '', '/execution-logs');
    mock = mockViewport(360, 640);
    render(<App />);
    expect(screen.getByRole('heading', { name: 'Execution Logs', level: 2 })).toBeInTheDocument();
    expect(logsMounted).not.toHaveBeenCalled();
  });

  it('reaches the notice through the More sheet and goes back to Queues', async () => {
    mock = mockViewport(360, 640);
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: /More/ }));
    fireEvent.click(screen.getByRole('button', { name: /Authoring/ }));
    expect(screen.getByText('Use a larger screen for this area.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Back to Queues' }));
    expect(await screen.findByTestId('phone-queues')).toBeInTheDocument();
  });

  it('renders the areas as before on a desktop screen', () => {
    window.history.replaceState(null, '', '/?area=execution-logs');
    mock = mockViewport(1280, 800);
    render(<App />);
    expect(screen.getByText('Logs page')).toBeInTheDocument();
    expect(screen.queryByText('Use a larger screen for this area.')).toBeNull();
    fireEvent.click(screen.getByRole('tab', { name: 'Authoring' }));
    expect(screen.getByText('Commands page')).toBeInTheDocument();
  });
});
