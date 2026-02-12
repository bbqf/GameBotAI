import React from 'react';
import { RunningSessionDto } from '../../services/sessionsApi';
import { StatusChip } from './StatusChip';
import './RunDetails.css';

export type RunDetailsProps = {
  loading?: boolean;
  session?: RunningSessionDto;
  gameName?: string;
};

const formatDate = (value?: string) => {
  if (!value) return '—';
  const dt = new Date(value);
  if (Number.isNaN(dt.getTime())) return '—';
  return dt.toLocaleString();
};

export const RunDetails: React.FC<RunDetailsProps> = ({ loading, session, gameName }) => {
  if (loading) {
    return (
      <div className="run-details" aria-busy="true" aria-label="Run details loading">
        <div className="run-details__header">
          <div className="skeleton-line skeleton-line--wide" />
          <div className="skeleton-line" style={{ width: 120 }} />
        </div>
        <div className="run-details__skeleton">
          <div className="skeleton-line" />
          <div className="skeleton-line" />
          <div className="skeleton-line" />
          <div className="skeleton-line skeleton-line--wide" />
        </div>
      </div>
    );
  }

  if (!session) {
    return <div className="form-hint">Select a running session to see details.</div>;
  }

  return (
    <div className="run-details" aria-label="Run details">
      <div className="run-details__header">
        <div>
          <div className="run-details__label">Session</div>
          <div className="run-details__value">{session.sessionId}</div>
        </div>
        <StatusChip status={session.status} />
      </div>
      <div className="run-details__meta">
        <div>
          <div className="run-details__label">Game</div>
          <div className="run-details__value">{gameName || session.gameId}</div>
        </div>
        <div>
          <div className="run-details__label">Emulator</div>
          <div className="run-details__value">{session.emulatorId || '—'}</div>
        </div>
        <div>
          <div className="run-details__label">Started</div>
          <div className="run-details__value">{formatDate(session.startedAtUtc)}</div>
        </div>
        <div>
          <div className="run-details__label">Last heartbeat</div>
          <div className="run-details__value">{formatDate(session.lastHeartbeatUtc)}</div>
        </div>
      </div>
    </div>
  );
};
