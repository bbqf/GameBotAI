import React, { useState } from 'react';
import { ScheduleType } from '../../services/queueTemplates';
import { EntrySchedule, TimerMode } from './QueueEntryList';
import { SchedulingAreaId } from './schedulingAreas';
import { ParameterBindingForm } from '../parameters/ParameterBindingForm';
import type { ParameterBinding, ParameterDeclaration } from '../parameters/types';

export type EntrySettingsPanelProps = {
  entryId: string;
  /** The name of the entry. The labels of the controls use it. */
  label: string;
  areaId: SchedulingAreaId;
  schedule: EntrySchedule;
  disabled?: boolean;
  /** Non-drag reassign path: change the schedule type (moves the card to the matching area). */
  onReassign: (entryId: string, scheduleType: ScheduleType) => void;
  onTimerTimeChange?: (entryId: string, timerTimeOfDay: string) => void;
  onTimerModeChange?: (entryId: string, mode: TimerMode) => void;
  onTimerRelativeOffsetChange?: (entryId: string, offset: string) => void;
  /** The parameters that the sequence declares (feature 078). */
  sequenceParameters?: ParameterDeclaration[];
  /** Left out, the panel has no Parameters part. */
  onParameterValuesChange?: (entryId: string, values: ParameterBinding[]) => void;
};

const SCHEDULE_LABELS: Record<ScheduleType, string> = {
  OncePerRun: 'Once Per Run',
  // Display label only — the stored/wire identifier remains "EveryStep" (FR-002/FR-010).
  EveryStep: 'After Every Step',
  Timer: 'Timer',
  AtQueueStart: 'At Queue Start',
  BeforeEachRun: 'Before Each Run',
};

const pad2 = (n: number): string => n.toString().padStart(2, '0');

const parseOffset = (raw: string | undefined): { h: number; m: number; s: number } => {
  const [h, m, s] = (raw ?? '').split(':').map((x) => parseInt(x, 10));
  return {
    h: Number.isFinite(h) ? h : 0,
    m: Number.isFinite(m) ? m : 0,
    s: Number.isFinite(s) ? s : 0,
  };
};

const composeOffset = (h: number, m: number, s: number): string => `${pad2(h)}:${pad2(m)}:${pad2(s)}`;

/** The timer mode of an entry. The card summary uses the same rule. */
export const timerModeOf = (schedule: EntrySchedule): TimerMode =>
  schedule.timerMode ?? (schedule.timerRelativeOffset ? 'relative' : 'timeOfDay');

/**
 * The Entry settings panel of a card (feature 185). The Schedule part comes first. The Parameters part follows.
 * The part with the parameters shows when the sequence declares parameters or the entry has values (rule D-05).
 */
export const EntrySettingsPanel: React.FC<EntrySettingsPanelProps> = ({
  entryId,
  label,
  areaId,
  schedule,
  disabled,
  onReassign,
  onTimerTimeChange,
  onTimerModeChange,
  onTimerRelativeOffsetChange,
  sequenceParameters,
  onParameterValuesChange,
}) => {
  const [adHocShown, setAdHocShown] = useState(false);
  const isScheduled = areaId === 'scheduled';
  const timerMode = timerModeOf(schedule);
  const isRelative = isScheduled && timerMode === 'relative';
  const offsetParts = parseOffset(schedule.timerRelativeOffset);
  const declarations = sequenceParameters ?? [];
  const values = schedule.parameterValues ?? [];
  const showParameters = !!onParameterValuesChange && (declarations.length > 0 || values.length > 0 || adHocShown);

  const emitOffset = (h: number, m: number, s: number) => {
    // Client-side non-negative validation: never emit a negative component (FR-021).
    if (h < 0 || m < 0 || s < 0) return;
    onTimerRelativeOffsetChange?.(entryId, composeOffset(h, m, s));
  };

  return (
    <div className="entry-settings-panel" data-testid="entry-settings-panel">
      <section className="entry-settings-panel__schedule" aria-label={`Schedule settings for ${label}`}>
        <h4 className="entry-settings-panel__heading">Schedule</h4>
        <div className="entry-settings-panel__controls">
          <select
            aria-label={`Schedule type for ${label}`}
            value={schedule.scheduleType}
            disabled={disabled}
            onChange={(e) => onReassign(entryId, e.target.value as ScheduleType)}
          >
            {(Object.keys(SCHEDULE_LABELS) as ScheduleType[]).map((t) => (
              <option key={t} value={t}>{SCHEDULE_LABELS[t]}</option>
            ))}
          </select>

          {isScheduled && onTimerModeChange && (
            <select
              aria-label={`Timer mode for ${label}`}
              value={timerMode}
              disabled={disabled}
              onChange={(e) => onTimerModeChange(entryId, e.target.value as TimerMode)}
            >
              <option value="timeOfDay">Time of day</option>
              <option value="relative">Relative</option>
            </select>
          )}

          {isScheduled && !isRelative && onTimerTimeChange && (
            <input
              type="time"
              aria-label={`Timer time for ${label}`}
              value={schedule.timerTimeOfDay}
              disabled={disabled}
              onChange={(e) => onTimerTimeChange(entryId, e.target.value)}
            />
          )}

          {isRelative && onTimerRelativeOffsetChange && (
            <span className="timer-relative-offset" aria-label={`Relative offset for ${label}`}>
              <input
                type="number"
                min={0}
                aria-label={`Offset hours for ${label}`}
                value={offsetParts.h}
                disabled={disabled}
                onChange={(e) => emitOffset(parseInt(e.target.value, 10) || 0, offsetParts.m, offsetParts.s)}
              />
              <span aria-hidden="true">h</span>
              <input
                type="number"
                min={0}
                max={59}
                aria-label={`Offset minutes for ${label}`}
                value={offsetParts.m}
                disabled={disabled}
                onChange={(e) => emitOffset(offsetParts.h, parseInt(e.target.value, 10) || 0, offsetParts.s)}
              />
              <span aria-hidden="true">m</span>
              <input
                type="number"
                min={0}
                max={59}
                aria-label={`Offset seconds for ${label}`}
                value={offsetParts.s}
                disabled={disabled}
                onChange={(e) => emitOffset(offsetParts.h, offsetParts.m, parseInt(e.target.value, 10) || 0)}
              />
              <span aria-hidden="true">s</span>
            </span>
          )}
        </div>
      </section>

      {showParameters && onParameterValuesChange && (
        <div className="entry-settings-panel__parameters">
          <ParameterBindingForm
            declarations={declarations}
            bindings={values}
            effective={schedule.effectiveParameters}
            // A queue-template entry is the outermost call site, so an ad-hoc name here reaches any
            // command underneath at any depth (FR-012a).
            allowAdHoc
            disabled={disabled}
            onChange={(next) => onParameterValuesChange(entryId, next)}
          />
        </div>
      )}

      {!showParameters && onParameterValuesChange && (
        <div className="entry-settings-panel__add-value">
          <button type="button" disabled={disabled} onClick={() => setAdHocShown(true)}>
            Add a value
          </button>
        </div>
      )}
    </div>
  );
};
