import React, { useEffect, useRef } from 'react';
import { DndContext, DragEndEvent, PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import { arrayMove } from '@dnd-kit/sortable';
import type { RecordedStep } from '../../../types/picker';
import { usePickerState, calcGestureDisplacement } from './usePickerState';
import { StepPickerOverlay } from './StepPickerOverlay';
import { RecordedStepList } from './RecordedStepList';
import { keyCodeMap } from './keyCodeMap';
import './VisualStepPicker.css';

type VisualStepPickerProps = {
  onConfirm: (steps: RecordedStep[]) => void;
  onCancel: () => void;
};

const SWIPE_THRESHOLD_PX = 10;

export const VisualStepPicker: React.FC<VisualStepPickerProps> = ({ onConfirm, onCancel }) => {
  const { state, openPicker, recapture, recordTap, recordKey, recordSwipe, removeStep, reorderSteps, runStep, runAll } =
    usePickerState();
  const focusRef = useRef<HTMLDivElement | null>(null);
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }));

  useEffect(() => {
    openPicker();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    focusRef.current?.focus();
  }, []);

  const handleKeyDown: React.KeyboardEventHandler = (e) => {
    e.preventDefault();
    if (state.status !== 'ready') return;
    const adbKey = keyCodeMap[e.code] ?? e.code;
    recordKey(adbKey);
  };

  const handleGesture = (
    start: { x: number; y: number },
    end: { x: number; y: number },
    durationMs: number
  ) => {
    const dist = calcGestureDisplacement(start, end);
    if (dist >= SWIPE_THRESHOLD_PX) {
      recordSwipe(start.x, start.y, end.x, end.y, durationMs);
    } else {
      recordTap(end.x, end.y);
    }
  };

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over || active.id === over.id) return;
    const oldIdx = state.steps.findIndex((s) => s.id === active.id);
    const newIdx = state.steps.findIndex((s) => s.id === over.id);
    if (oldIdx === -1 || newIdx === -1) return;
    reorderSteps(arrayMove(state.steps, oldIdx, newIdx));
  };

  const handleConfirm = () => {
    const cleanSteps = state.steps.map(({ executionStatus: _es, errorMessage: _em, ...rest }) => rest as RecordedStep);
    onConfirm(cleanSteps);
  };

  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-label="Visual Step Picker">
      <div
        className="visual-step-picker"
        ref={focusRef}
        role="button"
        aria-label="Step recorder keyboard capture"
        tabIndex={0}
        onKeyDown={handleKeyDown}
        style={{ outline: 'none' }}
      >
        <div className="visual-step-picker__header">
          <h3 className="visual-step-picker__title">Record Steps</h3>
          <button type="button" className="btn btn-secondary" onClick={recapture} disabled={state.status === 'loading'}>
            Re-capture
          </button>
        </div>

        <div className="visual-step-picker__body">
          <div className="visual-step-picker__screen">
            {state.status === 'error' && !state.screenshotUrl ? (
              <div className="visual-step-picker__error" role="alert">
                <p>{state.errorMessage ?? 'Failed to capture screenshot'}</p>
                <button type="button" className="btn btn-secondary" onClick={recapture}>
                  Retry
                </button>
              </div>
            ) : state.screenshotUrl ? (
              <>
                <StepPickerOverlay
                  screenshotUrl={state.screenshotUrl}
                  naturalWidth={state.naturalWidth}
                  naturalHeight={state.naturalHeight}
                  matches={state.matches}
                  status={state.status}
                  onGesture={handleGesture}
                />
                {state.status === 'error' && (
                  <div className="visual-step-picker__inline-error" role="alert">
                    Re-capture failed: {state.errorMessage}
                  </div>
                )}
              </>
            ) : (
              <div className="visual-step-picker__loading" aria-live="polite">
                Capturing screenshot…
              </div>
            )}
          </div>

          <div className="visual-step-picker__steps">
            <div className="visual-step-picker__steps-header">
              <span className="visual-step-picker__steps-label">Recorded steps ({state.steps.length})</span>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => void runAll()}
                disabled={state.steps.length === 0 || state.isExecuting}
                aria-label="Run all steps"
              >
                Run all
              </button>
            </div>
            <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
              <RecordedStepList
                steps={state.steps}
                isExecuting={state.isExecuting}
                onRemove={removeStep}
                onReorder={reorderSteps}
                onRunStep={(id) => void runStep(id)}
              />
            </DndContext>
            {state.steps.length === 0 && (
              <p className="visual-step-picker__empty-hint">
                Click a highlighted region to record a tap, drag to record a swipe, or press a key.
              </p>
            )}
          </div>
        </div>

        <div className="visual-step-picker__footer">
          <button type="button" className="btn btn-secondary" onClick={onCancel}>
            Cancel
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={handleConfirm}
            disabled={state.steps.length === 0}
          >
            Confirm ({state.steps.length})
          </button>
        </div>
      </div>
    </div>
  );
};
