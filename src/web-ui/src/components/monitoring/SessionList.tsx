import React from 'react';
import { ListView, StatusChip } from '../ui';
import type { ListColumn, ListState } from '../ui';
import type { LayoutMode } from '../../hooks/useLayoutMode';
import type { MonitoredSessionDto, MonitoredSessionState } from '../../services/monitoringApi';
import { NotLiveBadge } from '../queues/NotLiveBadge';
import { deviceKind, sessionChip } from './sessionView';

export type SessionListProps = {
  sessions: MonitoredSessionDto[];
  state?: ListState;
  onSelect: (id: string) => void;
  /** The default comes from the window size. */
  mode?: LayoutMode;
};

export const SessionStateChip: React.FC<{ state: MonitoredSessionState }> = ({ state }) => (
  <StatusChip {...sessionChip(state)} testId="session-state" />
);

const deviceCell = (s: MonitoredSessionDto): React.ReactNode => {
  const kind = deviceKind(s.ownerQueueDeviceLiveness);
  if (kind === 'not_live') return <NotLiveBadge />;
  return kind === 'live' ? 'Live' : '—';
};

const columns: ListColumn<MonitoredSessionDto>[] = [
  { id: 'name', header: 'Name', width: 'flex', render: (s) => s.name, text: (s) => s.name },
  { id: 'state', header: 'State', width: 132, render: (s) => <SessionStateChip state={s.state} /> },
  {
    id: 'device',
    header: 'Device',
    width: 120,
    render: deviceCell,
    text: (s) => ({ live: 'Live', not_live: 'Not live', none: '' })[deviceKind(s.ownerQueueDeviceLiveness)]
  },
  {
    id: 'queue',
    header: 'Queue',
    width: 220,
    render: (s) => s.ownerQueueName,
    text: (s) => s.ownerQueueName ?? ''
  },
  {
    id: 'sequence',
    header: 'Sequence',
    width: 260,
    render: (s) => s.currentSequenceName,
    text: (s) => s.currentSequenceName ?? ''
  }
];

/** The list of all sessions with a state chip, on the shared list component. */
export const SessionList: React.FC<SessionListProps> = ({ sessions, state = 'ready', onSelect, mode }) => (
  <ListView
    ariaLabel="Sessions"
    tableClassName="session-list"
    columns={columns}
    rows={sessions}
    getRowKey={(s) => s.id}
    onRowClick={(s) => onSelect(s.id)}
    rowTestId={(s) => `session-row-${s.id}`}
    openLabel={(s) => `Open ${s.name}`}
    state={state}
    mode={mode}
    phone={{
      title: (s) => s.name,
      chip: (s) => <SessionStateChip state={s.state} />,
      meta: (s) => (
        <>
          {s.ownerQueueName ?? 'No queue'}
          {deviceKind(s.ownerQueueDeviceLiveness) === 'not_live' && <> <NotLiveBadge /></>}
        </>
      )
    }}
  />
);
