import React from 'react';
import './StatusChip.css';

export type StatusKind = 'pending' | 'running' | 'succeeded' | 'failed' | 'stopping' | string;

const normalize = (status?: string): StatusKind => `${status ?? ''}`.toLowerCase();

const labelFor = (status: StatusKind): string => {
  switch (status) {
    case 'running':
      return 'Running';
    case 'pending':
      return 'Pending';
    case 'succeeded':
    case 'success':
      return 'Succeeded';
    case 'failed':
    case 'error':
      return 'Failed';
    case 'stopping':
      return 'Stopping';
    default:
      return 'Unknown';
  }
};

const classFor = (status: StatusKind): string => {
  switch (status) {
    case 'running':
      return 'status-chip--running';
    case 'pending':
      return 'status-chip--pending';
    case 'succeeded':
    case 'success':
      return 'status-chip--succeeded';
    case 'failed':
    case 'error':
      return 'status-chip--failed';
    case 'stopping':
      return 'status-chip--stopping';
    default:
      return 'status-chip--unknown';
  }
};

export type StatusChipProps = {
  status?: string;
};

export const StatusChip: React.FC<StatusChipProps> = ({ status }) => {
  const normalized = normalize(status);
  const label = labelFor(normalized);
  const className = `status-chip ${classFor(normalized)}`;

  return (
    <span className={className} aria-label={`Status: ${label}`} role="status">
      <span className="status-chip__dot" aria-hidden />
      <span>{label}</span>
    </span>
  );
};
