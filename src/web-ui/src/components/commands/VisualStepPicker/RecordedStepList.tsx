import React from 'react';
import { SortableContext, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import type { RecordedStep } from '../../../types/picker';

type RecordedStepListProps = {
  steps: RecordedStep[];
  isExecuting: boolean;
  onRemove: (id: string) => void;
  onReorder: (steps: RecordedStep[]) => void;
  onRunStep: (id: string) => void;
};

const StepIcon: React.FC<{ type: RecordedStep['type'] }> = ({ type }) => {
  if (type === 'PrimitiveTap') return <span title="Tap">👆</span>;
  if (type === 'KeyInput') return <span title="Key">⌨</span>;
  return <span title="Swipe">↔</span>;
};

const StatusBadge: React.FC<{ step: RecordedStep }> = ({ step }) => {
  if (step.executionStatus === 'success') {
    return <span className="step-picker-step-list__status step-picker-step-list__status--success" title="Success">✓</span>;
  }
  if (step.executionStatus === 'error') {
    const msg = (step as RecordedStep & { errorMessage?: string }).errorMessage ?? 'Execution failed';
    return <span className="step-picker-step-list__status step-picker-step-list__status--error" title={msg}>✗</span>;
  }
  return null;
};

const SortableStepItem: React.FC<{
  step: RecordedStep;
  isExecuting: boolean;
  onRemove: (id: string) => void;
  onRunStep: (id: string) => void;
}> = ({ step, isExecuting, onRemove, onRunStep }) => {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: step.id,
  });

  const style: React.CSSProperties = {
    transform: CSS.Translate.toString(transform),
    transition,
  };

  const isRunning = step.executionStatus === 'running';
  const runDisabled = isRunning || isExecuting;

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={[
        'step-picker-step-list__item',
        isDragging ? 'step-picker-step-list__item--dragging' : '',
        isRunning ? 'step-picker-step-list__item--running' : '',
      ].filter(Boolean).join(' ')}
    >
      <span
        className="step-picker-step-list__drag-handle"
        {...attributes}
        {...(isExecuting ? {} : listeners)}
        aria-label="Drag to reorder"
        aria-disabled={isExecuting}
      >
        ⠿
      </span>
      <StepIcon type={step.type} />
      <span className="step-picker-step-list__label" title={step.label}>
        {step.label}
      </span>
      <StatusBadge step={step} />
      <button
        type="button"
        className="btn btn-secondary step-picker-step-list__run"
        onClick={() => onRunStep(step.id)}
        disabled={runDisabled}
        aria-label={`Run step: ${step.label}`}
      >
        {isRunning ? '⏳' : '▶'}
      </button>
      <button
        type="button"
        className="btn btn-secondary step-picker-step-list__remove"
        onClick={() => onRemove(step.id)}
        disabled={isExecuting}
        aria-label={`Remove step: ${step.label}`}
      >
        ✕
      </button>
    </div>
  );
};

export const RecordedStepList: React.FC<RecordedStepListProps> = ({ steps, isExecuting, onRemove, onRunStep }) => {
  return (
    <div className="step-picker-step-list" role="list" aria-label="Recorded steps">
      <SortableContext items={steps.map((s) => s.id)} strategy={verticalListSortingStrategy}>
        {steps.map((step) => (
          <SortableStepItem
            key={step.id}
            step={step}
            isExecuting={isExecuting}
            onRemove={onRemove}
            onRunStep={onRunStep}
          />
        ))}
      </SortableContext>
    </div>
  );
};
