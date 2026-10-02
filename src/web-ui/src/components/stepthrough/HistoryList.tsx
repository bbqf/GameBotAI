import React from 'react';
import type { StepThroughHistoryEntryDto, StepThroughNodeDto } from '../../services/stepThrough';

type HistoryListProps = {
  history: StepThroughHistoryEntryDto[];
  nodes: StepThroughNodeDto[];
};

const KIND_TEXT: Record<StepThroughHistoryEntryDto['kind'], string | undefined> = {
  step: undefined,
  enter: 'decision',
  exit: 'loop end'
};

const formatTime = (iso: string): string => {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleTimeString();
};

/** The history of the step runs, in time order, in a form that matches the execution log (FR-008). */
export const HistoryList: React.FC<HistoryListProps> = ({ history, nodes }) => {
  if (history.length === 0) {
    return <div className="form-hint" role="status">No step ran yet. Click Run next step.</div>;
  }

  const labels = new Map(nodes.map((node) => [node.path, node.label]));
  return (
    <ol className="step-through-history" aria-label="History">
      {history.map((entry) => (
        <li key={entry.seq} className="step-through-history-entry" data-status={entry.status}>
          <div className="step-through-history-head">
            <span className="step-through-seq">#{entry.seq}</span>
            <span className={`step-through-badge step-through-badge--${entry.status.toLowerCase()}`}>{entry.status}</span>
            <strong>{labels.get(entry.path) ?? entry.stepId ?? entry.path}</strong>
            {KIND_TEXT[entry.kind] && <span className="step-through-type">{KIND_TEXT[entry.kind]}</span>}
            {entry.iteration != null && <span className="step-through-type">iteration {entry.iteration}</span>}
            {entry.outcome && <span className="step-through-type">{entry.outcome}</span>}
            <span className="step-through-time">{formatTime(entry.startedAt)} · {entry.durationMs} ms</span>
          </div>
          {entry.message && <div className="step-through-message">{entry.message}</div>}
          {entry.effects.map((effect) => (
            <div key={effect} className="step-through-effect">Previewed, not applied: {effect}</div>
          ))}
          {entry.notes.map((note) => (
            <div key={note} className="step-through-note">{note}</div>
          ))}
        </li>
      ))}
    </ol>
  );
};
