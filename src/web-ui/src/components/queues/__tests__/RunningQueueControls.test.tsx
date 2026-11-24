import React from 'react';
import { render, screen } from '@testing-library/react';
import { RunningQueueControls } from '../RunningQueueControls';
import { getQueueHealth } from '../../../services/queues';

jest.mock('../../../services/queues');

const getQueueHealthMock = getQueueHealth as jest.MockedFunction<typeof getQueueHealth>;

describe('RunningQueueControls device state (feature 199)', () => {
  beforeEach(() => {
    jest.resetAllMocks();
  });

  it('shows "Device: not live" for a running queue with a not-live device', async () => {
    getQueueHealthMock.mockResolvedValue({
      paused: false,
      operatorPause: 'none',
      deviceLiveness: { state: 'not_live', reason: 'capture_stalled', alertSent: false, recoveryState: 'idle', recoveryAttempts: 0 }
    });

    render(<RunningQueueControls queueId="q1" hasTemplate={false} recoveryOn={true} />);

    expect(await screen.findByText(/Device: not live \(capture_stalled\)/)).toBeInTheDocument();
    expect(screen.getByText('Recovery: idle, attempts 0')).toBeInTheDocument();
  });

  it('shows no device line when the run has no report', async () => {
    getQueueHealthMock.mockResolvedValue({ paused: false, operatorPause: 'none' });

    render(<RunningQueueControls queueId="q1" hasTemplate={false} />);

    await new Promise((r) => setTimeout(r, 0));
    expect(screen.queryByText(/^Device:/)).not.toBeInTheDocument();
  });
});
