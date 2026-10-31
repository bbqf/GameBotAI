import React from 'react';
import { readFileSync } from 'fs';
import { join } from 'path';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueuePauseControls } from '../QueuePauseControls';
import {
  getQueueHealth,
  pauseQueue,
  resumeQueue,
  QueueHealthDto
} from '../../../services/queues';
import { ApiError } from '../../../lib/api';

jest.mock('../../../services/queues');

const getHealthMock = getQueueHealth as jest.MockedFunction<typeof getQueueHealth>;
const pauseQueueMock = pauseQueue as jest.MockedFunction<typeof pauseQueue>;
const resumeQueueMock = resumeQueue as jest.MockedFunction<typeof resumeQueue>;

const health = (over: Partial<QueueHealthDto> = {}): QueueHealthDto => ({
  paused: false, pausedAt: null, pauseKind: null, pauseReason: null, operatorPause: 'none', ...over
});

const flush = async () => {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
};

describe('QueuePauseControls', () => {
  beforeEach(() => {
    jest.resetAllMocks();
  });

  it('shows nothing when the queue has no health block (not running)', async () => {
    getHealthMock.mockResolvedValue(null);

    const { container } = render(<QueuePauseControls queueId="q1" />);
    await flush();

    expect(container).toBeEmptyDOMElement();
  });

  it('shows a Pause button for a running queue that is not paused', async () => {
    getHealthMock.mockResolvedValue(health());

    render(<QueuePauseControls queueId="q1" />);

    expect(await screen.findByRole('button', { name: 'Pause' })).toBeInTheDocument();
    expect(screen.queryByTestId('queue-pause-state')).not.toBeInTheDocument();
  });

  it('asks for a pause and then shows "pausing" with a way to cancel', async () => {
    getHealthMock.mockResolvedValue(health());
    pauseQueueMock.mockResolvedValue({ id: 'q1', operatorPause: 'pausing', requestedAt: '2026-10-30T12:00:00+00:00' });

    render(<QueuePauseControls queueId="q1" />);
    fireEvent.click(await screen.findByRole('button', { name: 'Pause' }));

    expect(await screen.findByTestId('queue-pause-state')).toHaveTextContent('Pausing (waiting for the running sequence)');
    expect(pauseQueueMock).toHaveBeenCalledWith('q1');
    expect(screen.getByRole('button', { name: 'Cancel pause' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Pause' })).not.toBeInTheDocument();
  });

  it('shows "paused (between sequences)" with the time and a Resume button', async () => {
    getHealthMock.mockResolvedValue(health({
      paused: true, pauseKind: 'operator', operatorPause: 'paused', pausedAt: '2026-10-30T12:00:00+00:00'
    }));

    render(<QueuePauseControls queueId="q1" />);

    const state = await screen.findByTestId('queue-pause-state');
    expect(state).toHaveTextContent('Paused (between sequences)');
    expect(state).toHaveTextContent('since');
    expect(screen.getByRole('button', { name: 'Resume' })).toBeInTheDocument();
  });

  it('resumes the queue and goes back to the Pause button', async () => {
    getHealthMock.mockResolvedValue(health({ paused: true, pauseKind: 'operator', operatorPause: 'paused', pausedAt: '2026-10-30T12:00:00+00:00' }));
    resumeQueueMock.mockResolvedValue({ id: 'q1', status: 'Running', resumed: true, kind: 'operator' });

    render(<QueuePauseControls queueId="q1" />);
    fireEvent.click(await screen.findByRole('button', { name: 'Resume' }));

    expect(await screen.findByRole('button', { name: 'Pause' })).toBeInTheDocument();
    expect(resumeQueueMock).toHaveBeenCalledWith('q1');
    expect(screen.queryByTestId('queue-pause-state')).not.toBeInTheDocument();
  });

  it('cancels a pause request with the same resume call', async () => {
    getHealthMock.mockResolvedValue(health({ operatorPause: 'pausing' }));
    resumeQueueMock.mockResolvedValue({ id: 'q1', status: 'Running', resumed: true, kind: 'operator' });

    render(<QueuePauseControls queueId="q1" />);
    fireEvent.click(await screen.findByRole('button', { name: 'Cancel pause' }));

    expect(await screen.findByRole('button', { name: 'Pause' })).toBeInTheDocument();
    expect(resumeQueueMock).toHaveBeenCalledWith('q1');
  });

  it('shows the message of a refused request', async () => {
    getHealthMock.mockResolvedValue(health());
    pauseQueueMock.mockRejectedValue(new ApiError(409, 'The queue does not run, so it cannot pause.'));

    render(<QueuePauseControls queueId="q1" />);
    fireEvent.click(await screen.findByRole('button', { name: 'Pause' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The queue does not run, so it cannot pause.');
  });

  it('keeps the Pause button while another kind of pause is in force', async () => {
    getHealthMock.mockResolvedValue(health({ paused: true, pauseKind: 'failurePolicy', operatorPause: 'none' }));

    render(<QueuePauseControls queueId="q1" />);

    expect(await screen.findByRole('button', { name: 'Pause' })).toBeInTheDocument();
    expect(screen.queryByText('Paused (between sequences)')).not.toBeInTheDocument();
  });

  it('tells the parent after a change', async () => {
    getHealthMock.mockResolvedValue(health());
    pauseQueueMock.mockResolvedValue({ id: 'q1', operatorPause: 'pausing' });
    const onChanged = jest.fn();

    render(<QueuePauseControls queueId="q1" onChanged={onChanged} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Pause' }));

    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1));
  });

  it('does not ask for health while it is disabled', async () => {
    getHealthMock.mockResolvedValue(health());

    render(<QueuePauseControls queueId="q1" enabled={false} />);
    await flush();

    expect(getHealthMock).not.toHaveBeenCalled();
  });

  it('uses the words Pause and Resume, never Hold, and marks the state as a status', async () => {
    getHealthMock.mockResolvedValue(health({ operatorPause: 'paused', paused: true, pauseKind: 'operator', pausedAt: '2026-10-30T12:00:00+00:00' }));

    render(<QueuePauseControls queueId="q1" />);

    const state = await screen.findByTestId('queue-pause-state');
    expect(state).toHaveAttribute('role', 'status');
    expect(screen.queryByText(/hold/i)).not.toBeInTheDocument();
  });

  it('shows the extra controls that the parent gives it', async () => {
    getHealthMock.mockResolvedValue(health());

    render(<QueuePauseControls queueId="q1"><button type="button">Review changes</button></QueuePauseControls>);

    expect(await screen.findByRole('button', { name: 'Review changes' })).toBeInTheDocument();
  });

  it('reads nothing itself when the parent gives the health, and follows the parent', async () => {
    const { rerender } = render(<QueuePauseControls queueId="q1" health={health()} />);
    expect(screen.getByRole('button', { name: 'Pause' })).toBeInTheDocument();

    rerender(<QueuePauseControls queueId="q1" health={health({ paused: true, pauseKind: 'operator', operatorPause: 'paused' })} />);

    expect(await screen.findByTestId('queue-pause-state')).toHaveTextContent('Paused (between sequences)');
    expect(getHealthMock).not.toHaveBeenCalled();
  });

  it('has no Review button: the notice of template changes owns it (feature 173)', async () => {
    getHealthMock.mockResolvedValue(health({ paused: true, pauseKind: 'operator', operatorPause: 'paused' }));

    render(<QueuePauseControls queueId="q1" />);
    await screen.findByRole('button', { name: 'Resume' });

    expect(screen.queryByRole('button', { name: /Review/i })).not.toBeInTheDocument();
    expect(screen.queryByTestId('review-template-changes')).not.toBeInTheDocument();
  });
  it('shows nothing when the parent gives a null health', () => {
    const { container } = render(<QueuePauseControls queueId="q1" health={null} />);

    expect(container).toBeEmptyDOMElement();
  });
});

describe('QueuePauseControls styles', () => {
  const css = readFileSync(join(__dirname, '..', 'QueuePauseControls.css'), 'utf8');

  it('gives each button a hit area of at least 44 px and a font of at least 16 px', () => {
    expect(css).toMatch(/min-height:\s*44px/);
    expect(css).toMatch(/min-width:\s*44px/);
    expect(css).toMatch(/font-size:\s*16px/);
  });

  it('lets the row wrap so that a phone shows no horizontal scroll', () => {
    expect(css).toMatch(/flex-wrap:\s*wrap/);
  });
});
