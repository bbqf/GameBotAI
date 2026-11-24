import React from 'react';
import { render, screen } from '@testing-library/react';
import { QueueDeviceState } from '../QueueDeviceState';
import { NotLiveBadge } from '../NotLiveBadge';

describe('QueueDeviceState (feature 199)', () => {
  it('shows nothing when the run has no report', () => {
    const { container, rerender } = render(<QueueDeviceState liveness={null} recoveryOn={true} />);
    expect(container).toBeEmptyDOMElement();
    rerender(<QueueDeviceState liveness={undefined} recoveryOn={false} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('shows "Device: live" and nothing else for a live device', () => {
    render(<QueueDeviceState liveness={{ state: 'live' }} recoveryOn={true} />);
    expect(screen.getByRole('status')).toHaveTextContent(/^Device: live$/);
  });

  it('shows the reason, the alert state, and the recovery state of a not-live device', () => {
    render(
      <QueueDeviceState
        liveness={{
          state: 'not_live',
          reason: 'capture_stalled',
          notLiveSince: '2026-11-24T10:05:00Z',
          alertSent: true,
          recoveryAttempts: 1,
          recoveryState: 'running'
        }}
        recoveryOn={true}
      />
    );
    const status = screen.getByRole('status');
    expect(status).toHaveTextContent('Device: not live (capture_stalled) since');
    expect(status).toHaveTextContent('Alert sent');
    expect(status).toHaveTextContent('Recovery: running, attempts 1');
  });

  it('shows "No alert yet" and "Recovery: off" when the queue has no recovery', () => {
    render(<QueueDeviceState liveness={{ state: 'not_live', reason: 'input_timeout', alertSent: false }} recoveryOn={false} />);
    const status = screen.getByRole('status');
    expect(status).toHaveTextContent('Device: not live (input_timeout)');
    expect(status).toHaveTextContent('No alert yet');
    expect(status).toHaveTextContent('Recovery: off');
  });

  it('shows the exhausted state', () => {
    render(<QueueDeviceState liveness={{ state: 'not_live', reason: 'capture_stalled', recoveryState: 'exhausted', recoveryAttempts: 2 }} recoveryOn={true} />);
    expect(screen.getByRole('status')).toHaveTextContent('Recovery: exhausted, attempts 2');
  });
});

describe('NotLiveBadge (feature 199)', () => {
  it('shows the text Not live', () => {
    render(<NotLiveBadge />);
    expect(screen.getByText('Not live')).toBeInTheDocument();
  });
});
