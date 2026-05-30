import React from 'react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import type { StepDragData } from '../types/stepEntry';

type SortableStepItemProps = {
  id: string;
  scopeId: string;
  disabled?: boolean;
  children: React.ReactNode;
};

export const SortableStepItem: React.FC<SortableStepItemProps> = ({ id, scopeId, disabled, children }) => {
  const data: StepDragData = { scopeId, type: 'step' };
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id,
    data,
    disabled,
  });

  const style: React.CSSProperties = {
    transform: transform ? CSS.Translate.toString(transform) : undefined,
    transition,
    opacity: isDragging ? 0.4 : undefined,
    position: 'relative',
  };

  return (
    <div ref={setNodeRef} style={style} className="sortable-step-item">
      <span
        className="sortable-step-item__handle"
        aria-label="Drag to reorder"
        title="Drag to reorder"
        {...attributes}
        {...listeners}
        style={{ cursor: disabled ? 'not-allowed' : 'grab', userSelect: 'none', padding: '0 6px', color: '#888' }}
      >
        ⠿
      </span>
      {children}
    </div>
  );
};
