import React from 'react';
import { SortableContext, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import type { RecordedStep } from '../../../types/picker';

type RecordedStepListProps = {
  steps: RecordedStep[];
  onRemove: (id: string) => void;
  onReorder: (steps: RecordedStep[]) => void;
};

const StepIcon: React.FC<{ type: RecordedStep['type'] }> = ({ type }) => {
  if (type === 'PrimitiveTap') return <span title="Tap">👆</span>;
  if (type === 'KeyInput') return <span title="Key">⌨</span>;
  return <span title="Swipe">↔</span>;
};

const SortableStepItem: React.FC<{ step: RecordedStep; onRemove: (id: string) => void }> = ({
  step,
  onRemove,
}) => {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: step.id,
  });

  const style: React.CSSProperties = {
    transform: CSS.Translate.toString(transform),
    transition,
  };

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={`step-picker-step-list__item${isDragging ? ' step-picker-step-list__item--dragging' : ''}`}
    >
      <span
        className="step-picker-step-list__drag-handle"
        {...attributes}
        {...listeners}
        aria-label="Drag to reorder"
      >
        ⠿
      </span>
      <StepIcon type={step.type} />
      <span className="step-picker-step-list__label" title={step.label}>
        {step.label}
      </span>
      <button
        type="button"
        className="btn btn-secondary step-picker-step-list__remove"
        onClick={() => onRemove(step.id)}
        aria-label={`Remove step: ${step.label}`}
      >
        ✕
      </button>
    </div>
  );
};

export const RecordedStepList: React.FC<RecordedStepListProps> = ({ steps, onRemove }) => {
  return (
    <div className="step-picker-step-list" role="list" aria-label="Recorded steps">
      <SortableContext items={steps.map((s) => s.id)} strategy={verticalListSortingStrategy}>
        {steps.map((step) => (
          <SortableStepItem key={step.id} step={step} onRemove={onRemove} />
        ))}
      </SortableContext>
    </div>
  );
};
