import React from 'react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { ScheduleType } from '../../services/queueTemplates';
import { TimerMode } from './QueueEntryList';
import { SchedulingAreaId, SchedulingCard } from './schedulingAreas';
import type { ParameterBinding, ParameterDeclaration } from '../parameters/types';
import { IconButton } from './IconButton';
import { describeRunNext, RunNextSupport } from './runNext';
import { EntrySettingsPanel, timerModeOf } from './EntrySettingsPanel';
import { scheduleSummary } from './scheduleSummary';

type SchedulingSequenceCardProps = {
  card: SchedulingCard;
  areaId: SchedulingAreaId;
  disabled?: boolean;
  /** True when the Entry settings panel of this card is open. The editor holds this state (feature 185). */
  open?: boolean;
  /** Toggle the Entry settings panel of this card. */
  onToggleOpen?: (entryId: string) => void;
  onRemove: (entryId: string) => void;
  /** Non-drag reassign path: change the card's schedule option (moves it to the matching area). */
  onReassign: (entryId: string, scheduleType: ScheduleType) => void;
  onTimerTimeChange?: (entryId: string, timerTimeOfDay: string) => void;
  onTimerModeChange?: (entryId: string, mode: TimerMode) => void;
  onTimerRelativeOffsetChange?: (entryId: string, offset: string) => void;
  /** Toggle whether this entry runs during a queue run (persisted to the template on save). */
  onToggleEnabled?: (entryId: string, enabled: boolean) => void;
  /**
   * Parameters the referenced sequence declares (feature 078). Rendered as a binding form so the
   * operator can supply a per-entry value — the mechanism that lets several entries share one
   * sequence and differ only by a value.
   */
  sequenceParameters?: ParameterDeclaration[];
  /** Persist this entry's supplied values (declared bindings and ad-hoc names alike). */
  onParameterValuesChange?: (entryId: string, values: ParameterBinding[]) => void;
  /**
   * Run next (feature 184). The row shows the Run next icon button, with the reason as text when it is off. Left out,
   * the row shows no Run next button.
   */
  runNext?: RunNextSupport;
};

export const SchedulingSequenceCard: React.FC<SchedulingSequenceCardProps> = ({
  card,
  areaId,
  disabled,
  open = false,
  onToggleOpen,
  onRemove,
  onReassign,
  onTimerTimeChange,
  onTimerModeChange,
  onTimerRelativeOffsetChange,
  onToggleEnabled,
  sequenceParameters,
  onParameterValuesChange,
  runNext,
}) => {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: card.entryId,
    data: { areaId },
    disabled,
  });

  const style: React.CSSProperties = {
    transform: transform ? CSS.Translate.toString(transform) : undefined,
    transition,
    opacity: isDragging ? 0.4 : undefined,
    position: 'relative',
  };

  const schedule = card.schedule;
  const label = card.label;
  const enabled = card.enabled;
  // Feature 184: Run next. Only an editor that passes the support shows it.
  const runNextView = runNext ? describeRunNext(runNext, card.entryId, enabled) : null;
  const isScheduled = areaId === 'scheduled';
  const isRelative = isScheduled && timerModeOf(schedule) === 'relative';
  // Feature 185: the closed row shows one line of schedule text. The timer mode decides which Timer field it reads.
  const summary = scheduleSummary({ ...schedule, timerMode: isScheduled ? timerModeOf(schedule) : undefined });

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={`scheduling-card${enabled ? '' : ' scheduling-card--disabled'}${open ? ' scheduling-card--open' : ''}`}
      data-testid="queue-entry"
      data-enabled={enabled}
    >
      <div className="scheduling-card__row">
        <span
          className="scheduling-card__handle"
          aria-label="Drag to reorder"
          title="Drag to reorder"
          {...attributes}
          {...listeners}
          style={{ cursor: disabled ? 'not-allowed' : 'grab', userSelect: 'none', padding: '0 6px', color: '#888' }}
        >
          ⠿
        </span>

        <label
          className="scheduling-card__toggle"
          title={enabled ? 'Enabled — click to disable' : 'Disabled — click to enable'}
        >
          <input
            type="checkbox"
            role="switch"
            checked={enabled}
            disabled={disabled || !onToggleEnabled}
            aria-label={`${enabled ? 'Disable' : 'Enable'} ${label}`}
            onChange={(e) => onToggleEnabled?.(card.entryId, e.target.checked)}
          />
        </label>

        <span className="scheduling-card__name">
          {label}
          {!enabled && <span className="badge badge-muted" role="status" aria-label="Disabled"> Off</span>}
          {card.stale && <span className="badge badge-warning" role="status"> (stale)</span>}
          {areaId === 'startOfExecution' && <span className="badge badge-info" role="status" aria-label="At Queue Start"> At Queue Start</span>}
          {areaId === 'beforeEachRun' && <span className="badge badge-info" role="status" aria-label="Before Each Run"> Before Each Run</span>}
          {areaId === 'afterEveryStep' &&<span className="badge badge-info" role="status" aria-label="After Every Step"> After Every Step</span>}
          {isScheduled && !isRelative && <span className="badge badge-info" role="status" aria-label="Timer"> Timer</span>}
          {(schedule.parameterValues?.length ?? 0) > 0 && (
            <span className="badge badge-param" role="status" aria-label="Has parameter overrides"> Parameters</span>
          )}
          {isRelative && <span className="badge badge-info" role="status" aria-label="Relative timer"> Timer · relative</span>}
        </span>

        <span className="scheduling-card__summary" data-testid="schedule-summary" title={summary}>{summary}</span>

        {runNextView && (
          <>
            {runNextView.note && (
              <span className="scheduling-card__run-next-note" role="status">{runNextView.note}</span>
            )}
            <IconButton
              icon="runNext"
              label={`Run next ${label}`}
              title={runNextView.tooltip}
              disabled={disabled || !runNextView.enabled}
              onClick={() => runNext?.onRunNext?.(card.entryId)}
            />
          </>
        )}

        <IconButton
          icon="settings"
          label={`Entry settings for ${label}`}
          title="Entry settings"
          ariaExpanded={open}
          onClick={() => onToggleOpen?.(card.entryId)}
        />

        <IconButton
          icon="remove"
          label={`Remove ${label}`}
          title="Remove"
          tone="destructive"
          disabled={disabled}
          onClick={() => onRemove(card.entryId)}
        />
      </div>

      {open && (
        <EntrySettingsPanel
          entryId={card.entryId}
          label={label}
          areaId={areaId}
          schedule={schedule}
          disabled={disabled}
          onReassign={onReassign}
          onTimerTimeChange={onTimerTimeChange}
          onTimerModeChange={onTimerModeChange}
          onTimerRelativeOffsetChange={onTimerRelativeOffsetChange}
          sequenceParameters={sequenceParameters}
          onParameterValuesChange={onParameterValuesChange}
        />
      )}
    </div>
  );
};
