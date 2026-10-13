import React from 'react';
import { PhoneCard } from '../phone/PhoneCard';
import type { MonitoredSessionDto, MonitoredSessionState } from '../../services/monitoringApi';
import '../../features/execution/StatusChip.css';

export type SessionListProps = {
  sessions: MonitoredSessionDto[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  /** True in the phone layout. Each item is then a PhoneCard. */
  phone?: boolean;
};

const stateLabel: Record<MonitoredSessionState, string> = {
  live: 'Live',
  no_answer: 'No answer',
  restarting: 'Restarting',
  stopping: 'Stopping'
};

const stateClass: Record<MonitoredSessionState, string> = {
  live: 'status-chip status-chip--succeeded',
  no_answer: 'status-chip status-chip--failed',
  restarting: 'status-chip status-chip--pending',
  stopping: 'status-chip status-chip--stopping'
};

export const SessionStateChip: React.FC<{ state: MonitoredSessionState }> = ({ state }) => (
  <span className={stateClass[state] ?? 'status-chip status-chip--unknown'} data-testid="session-state">
    <span className="status-chip__dot" aria-hidden />
    <span>{stateLabel[state] ?? state}</span>
  </span>
);

/** The list of all sessions with a state chip (feature 141, contract M-05 and M-11). */
export const SessionList: React.FC<SessionListProps> = ({ sessions, selectedId, onSelect, phone = false }) => (
  <ul className={phone ? 'phone-card-list' : 'monitoring-list'} aria-label="Sessions">
    {sessions.map((s) => (phone ? (
      <PhoneCard
        key={s.id}
        title={s.name}
        testId={`session-card-${s.id}`}
        status={<SessionStateChip state={s.state} />}
        onOpen={() => onSelect(s.id)}
      />
    ) : (
      <li key={s.id} className="monitoring-list__item" data-testid={`session-row-${s.id}`}>
        <button
          type="button"
          className={s.id === selectedId ? 'monitoring-list__button monitoring-list__button--selected' : 'monitoring-list__button'}
          aria-pressed={s.id === selectedId}
          aria-label={`Select ${s.name}`}
          onClick={() => onSelect(s.id)}
        >
          <span className="monitoring-list__name">{s.name}</span>
          <SessionStateChip state={s.state} />
        </button>
      </li>
    )))}
  </ul>
);
