import React, { useMemo } from 'react';
import type { StepThroughHistoryEntryDto, StepThroughNodeDto } from '../../services/stepThrough';

type StepListProps = {
  nodes: StepThroughNodeDto[];
  cursor?: string | null;
  runningPath?: string | null;
  history: StepThroughHistoryEntryDto[];
  /** True while a step runs or a request is open. The author cannot select a step then (FR-011). */
  disabled: boolean;
  onSelect: (path: string) => void;
};

const INDENT_PX = 20;

/** The step list with the nesting, the marker of the next step, and the last status of each step (FR-004). */
export const StepList: React.FC<StepListProps> = ({ nodes, cursor, runningPath, history, disabled, onSelect }) => {
  const lastStatusByPath = useMemo(() => {
    const map = new Map<string, StepThroughHistoryEntryDto>();
    history.forEach((entry) => {
      if (entry.kind === 'step') map.set(entry.path, entry);
    });
    return map;
  }, [history]);

  return (
    <ol className="step-through-list" aria-label="Steps">
      {nodes.map((node) => {
        const isNext = node.path === cursor;
        const isRunning = node.path === runningPath;
        const last = lastStatusByPath.get(node.path);
        return (
          <li
            key={node.path}
            data-path={node.path}
            className={`step-through-row${isNext ? ' step-through-row--next' : ''}${node.container ? ' step-through-row--header' : ''}`}
            style={{ paddingLeft: `${node.depth * INDENT_PX}px` }}
            aria-current={isNext ? 'step' : undefined}
          >
            <span className="step-through-marker" aria-hidden="true">{isRunning ? '…' : isNext ? '▶' : ''}</span>
            {node.branch === 'else' && <span className="step-through-branch">else</span>}
            {node.selectable ? (
              <button
                type="button"
                className="step-through-row-button"
                onClick={() => onSelect(node.path)}
                disabled={disabled}
                title="Make this the next step"
              >
                {node.label}
              </button>
            ) : (
              <span className="step-through-header-label">{node.label}</span>
            )}
            <span className="step-through-type">{node.type}</span>
            {isRunning && <span className="step-through-badge step-through-badge--running">running</span>}
            {isNext && !isRunning && <span className="step-through-next-text">next</span>}
            {last && !isRunning && (
              <span className={`step-through-badge step-through-badge--${last.status.toLowerCase()}`}>{last.status}</span>
            )}
          </li>
        );
      })}
    </ol>
  );
};
