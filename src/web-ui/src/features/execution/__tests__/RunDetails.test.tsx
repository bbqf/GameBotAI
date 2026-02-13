import React from 'react';
import { render, screen } from '@testing-library/react';
import { RunDetails } from '../RunDetails';
import { RunningSessionDto } from '../../../services/sessionsApi';

const sampleSession: RunningSessionDto = {
  sessionId: 'sess-1',
  gameId: 'game-1',
  emulatorId: 'emu-1',
  startedAtUtc: new Date('2024-01-01T10:00:00Z').toISOString(),
  lastHeartbeatUtc: new Date('2024-01-01T10:05:00Z').toISOString(),
  status: 'Running'
};

describe('RunDetails', () => {
  it('renders a skeleton when loading', () => {
    const { container } = render(<RunDetails loading />);

    const skeleton = screen.getByLabelText('Run details loading');
    expect(skeleton).toBeInTheDocument();
    expect(container.querySelectorAll('.skeleton-line').length).toBeGreaterThan(0);
  });

  it('renders session details with status chip', () => {
    render(<RunDetails session={sampleSession} gameName="Game One" />);

    expect(screen.getByText('sess-1')).toBeInTheDocument();
    expect(screen.getByText('Game One')).toBeInTheDocument();
    expect(screen.getByRole('status', { name: /Status: Running/i })).toBeInTheDocument();
  });

  it('renders empty state when no session is provided', () => {
    render(<RunDetails />);

    expect(screen.getByText(/Select a running session/i)).toBeInTheDocument();
  });
});
