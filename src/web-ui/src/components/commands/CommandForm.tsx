import React, { useMemo, useState } from 'react';
import { DndContext, DragEndEvent, DragOverEvent, DragStartEvent, PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import { arrayMove } from '@dnd-kit/sortable';
import { FormActions, FormSection } from '../unified/FormLayout';
import { SearchableDropdown, SearchableOption } from '../SearchableDropdown';
import type { ReorderableListItem } from '../ReorderableList';
import { SortableSequenceStepList } from '../SortableSequenceStepList';
import './CommandForm.css';

export type StepEntry = {
  id: string;
  type: 'Command' | 'PrimitiveTap' | 'WaitForImage';
  targetId?: string;
  primitiveTap?: {
    detectionTarget: DetectionTargetForm;
  };
  waitForImage?: {
    detectionTarget?: DetectionTargetForm;
    timeoutMs?: string;
  };
};

export type DetectionTargetForm = {
  referenceImageId: string;
  confidence?: string;
  offsetX?: string;
  offsetY?: string;
};

export type CommandFormValue = {
  name: string;
  steps: StepEntry[];
  detection?: DetectionTargetForm;
};

const makeId = () => (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : Math.random().toString(36).slice(2));

export type CommandFormProps = {
  value: CommandFormValue;
  commandOptions: SearchableOption[];
  submitting?: boolean;
  loading?: boolean;
  errors?: Record<string, string>;
  onChange: (next: CommandFormValue) => void;
  onSubmit?: () => void;
  onCancel?: () => void;
};

const toStepItems = (steps: StepEntry[], commandOpts: SearchableOption[]): ReorderableListItem[] => {
  const commandMap = new Map(commandOpts.map((o) => [o.value, o] as const));
  return steps.map((step) => {
    if (step.type === 'PrimitiveTap') {
      const imageId = step.primitiveTap?.detectionTarget.referenceImageId ?? '(missing image)';
      const offsetX = step.primitiveTap?.detectionTarget.offsetX ?? '0';
      const offsetY = step.primitiveTap?.detectionTarget.offsetY ?? '0';
      return {
        id: step.id,
        label: `Primitive tap: ${imageId}`,
        description: `Offset (${offsetX}, ${offsetY})`,
      };
    }

    if (step.type === 'WaitForImage') {
      const imageId = step.waitForImage?.detectionTarget?.referenceImageId?.trim() || 'any image';
      const timeoutMs = step.waitForImage?.timeoutMs?.trim() || '1000';
      const confidence = step.waitForImage?.detectionTarget?.confidence?.trim();
      return {
        id: step.id,
        label: `Wait for image: ${imageId}`,
        description: confidence ? `Timeout ${timeoutMs} ms, confidence ${confidence}` : `Timeout ${timeoutMs} ms`,
      };
    }

    const targetId = step.targetId ?? '';
    const match = commandMap.get(targetId);
    return {
      id: step.id,
      label: `Command: ${match?.label ?? targetId}`,
      description: match?.description,
    };
  });
};

export const CommandForm: React.FC<CommandFormProps> = ({
  value,
  commandOptions,
  submitting,
  loading,
  errors,
  onChange,
  onSubmit,
  onCancel,
}) => {
  const [pendingCommandId, setPendingCommandId] = useState<string | undefined>(undefined);
  const [pendingPrimitiveReferenceImageId, setPendingPrimitiveReferenceImageId] = useState('');
  const [pendingPrimitiveConfidence, setPendingPrimitiveConfidence] = useState('');
  const [pendingPrimitiveOffsetX, setPendingPrimitiveOffsetX] = useState('0');
  const [pendingPrimitiveOffsetY, setPendingPrimitiveOffsetY] = useState('0');
  const [pendingWaitReferenceImageId, setPendingWaitReferenceImageId] = useState('');
  const [pendingWaitConfidence, setPendingWaitConfidence] = useState('');
  const [pendingWaitTimeoutMs, setPendingWaitTimeoutMs] = useState('1000');
  const [activeStepId, setActiveStepId] = useState<string | null>(null);
  const [overId, setOverId] = useState<string | null>(null);

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }));

  const stepItems = useMemo(() => toStepItems(value.steps, commandOptions), [value.steps, commandOptions]);

  const addStep = (step: Omit<StepEntry, 'id'>) => {
    const next = [...value.steps, { ...step, id: makeId() }];
    onChange({ ...value, steps: next });
  };

  const removeStep = (itemId: string) => {
    onChange({ ...value, steps: value.steps.filter((s) => s.id !== itemId) });
  };

  const handleDragStart = (event: DragStartEvent) => {
    setActiveStepId(event.active.id as string);
    setOverId(null);
  };

  const handleDragOver = (event: DragOverEvent) => {
    setOverId(event.over?.id as string ?? null);
  };

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    setActiveStepId(null);
    setOverId(null);
    if (!over || active.id === over.id) return;
    const oldIndex = value.steps.findIndex((s) => s.id === active.id);
    const newIndex = value.steps.findIndex((s) => s.id === over.id);
    if (oldIndex === -1 || newIndex === -1) return;
    onChange({ ...value, steps: arrayMove(value.steps, oldIndex, newIndex) });
  };

  const handleDragCancel = () => {
    setActiveStepId(null);
    setOverId(null);
  };

  return (
    <form
      className="command-form"
      aria-label="Command form"
      onSubmit={(e) => {
        e.preventDefault();
        onSubmit?.();
      }}
    >
      <FormSection title="Basics" description="Primary details for the command." id="command-basics">
        <div className="field">
          <label htmlFor="command-name">Name *</label>
          <input
            id="command-name"
            value={value.name}
            onChange={(e) => onChange({ ...value, name: e.target.value })}
            aria-invalid={Boolean(errors?.name)}
            aria-describedby={errors?.name ? 'command-name-error' : undefined}
            disabled={submitting}
          />
          {errors?.name && <div id="command-name-error" className="field-error" role="alert">{errors.name}</div>}
        </div>
      </FormSection>

      <FormSection title="Steps" description="Choose command or primitive tap steps and set their order." id="command-steps">

        <SearchableDropdown
          id="command-commands-dropdown"
          label="Add command"
          options={commandOptions}
          value={pendingCommandId}
          onChange={setPendingCommandId}
          disabled={submitting || loading}
          placeholder="Select a command"
        />
        <div className="field">
          <button
            type="button"
            onClick={() => {
              if (!pendingCommandId) return;
              addStep({ type: 'Command', targetId: pendingCommandId });
              setPendingCommandId(undefined);
            }}
            disabled={submitting || loading || !pendingCommandId}
          >
            Add command step
          </button>
        </div>

        <div className="field grid-3">
          <div>
            <label htmlFor="command-primitive-reference">Primitive tap image ID</label>
            <input
              id="command-primitive-reference"
              value={pendingPrimitiveReferenceImageId}
              onChange={(e) => setPendingPrimitiveReferenceImageId(e.target.value)}
              disabled={submitting || loading}
            />
          </div>
          <div>
            <label htmlFor="command-primitive-confidence">Primitive confidence (0-1)</label>
            <input
              id="command-primitive-confidence"
              type="number"
              step="0.01"
              min="0"
              max="1"
              value={pendingPrimitiveConfidence}
              onChange={(e) => setPendingPrimitiveConfidence(e.target.value)}
              disabled={submitting || loading}
            />
          </div>
          <div>
            <label htmlFor="command-primitive-offset-x">Primitive offset X</label>
            <input
              id="command-primitive-offset-x"
              type="number"
              value={pendingPrimitiveOffsetX}
              onChange={(e) => setPendingPrimitiveOffsetX(e.target.value)}
              disabled={submitting || loading}
            />
          </div>
        </div>
        <div className="field">
          <label htmlFor="command-primitive-offset-y">Primitive offset Y</label>
          <input
            id="command-primitive-offset-y"
            type="number"
            value={pendingPrimitiveOffsetY}
            onChange={(e) => setPendingPrimitiveOffsetY(e.target.value)}
            disabled={submitting || loading}
          />
        </div>
        <div className="field">
          <button
            type="button"
            onClick={() => {
              const imageId = pendingPrimitiveReferenceImageId.trim();
              if (!imageId) return;
              addStep({
                type: 'PrimitiveTap',
                primitiveTap: {
                  detectionTarget: {
                    referenceImageId: imageId,
                    confidence: pendingPrimitiveConfidence || undefined,
                    offsetX: pendingPrimitiveOffsetX || undefined,
                    offsetY: pendingPrimitiveOffsetY || undefined,
                  }
                }
              });
              setPendingPrimitiveReferenceImageId('');
              setPendingPrimitiveConfidence('');
              setPendingPrimitiveOffsetX('0');
              setPendingPrimitiveOffsetY('0');
            }}
            disabled={submitting || loading || !pendingPrimitiveReferenceImageId.trim()}
          >
            Add primitive tap step
          </button>
        </div>

        <div className="field grid-3">
          <div>
            <label htmlFor="command-wait-reference">Wait image ID</label>
            <input
              id="command-wait-reference"
              value={pendingWaitReferenceImageId}
              onChange={(e) => {
                const nextValue = e.target.value;
                setPendingWaitReferenceImageId(nextValue);
                if (!nextValue.trim()) {
                  setPendingWaitConfidence('');
                }
              }}
              disabled={submitting || loading}
            />
          </div>
          <div>
            <label htmlFor="command-wait-confidence">Wait confidence (0-1)</label>
            <input
              id="command-wait-confidence"
              type="number"
              step="0.01"
              min="0"
              max="1"
              value={pendingWaitConfidence}
              onChange={(e) => setPendingWaitConfidence(e.target.value)}
              disabled={submitting || loading || !pendingWaitReferenceImageId.trim()}
            />
          </div>
          <div>
            <label htmlFor="command-wait-timeout">Wait timeout (ms)</label>
            <input
              id="command-wait-timeout"
              type="number"
              min="0"
              value={pendingWaitTimeoutMs}
              onChange={(e) => setPendingWaitTimeoutMs(e.target.value)}
              disabled={submitting || loading}
            />
          </div>
        </div>
        <div className="field">
          <button
            type="button"
            onClick={() => {
              const imageId = pendingWaitReferenceImageId.trim();
              addStep({
                type: 'WaitForImage',
                waitForImage: {
                  detectionTarget: imageId
                    ? {
                      referenceImageId: imageId,
                      confidence: pendingWaitConfidence || undefined,
                    }
                    : undefined,
                  timeoutMs: pendingWaitTimeoutMs || '1000',
                }
              });
              setPendingWaitReferenceImageId('');
              setPendingWaitConfidence('');
              setPendingWaitTimeoutMs('1000');
            }}
            disabled={submitting || loading || !pendingWaitTimeoutMs.trim()}
          >
            Add wait for image step
          </button>
        </div>

        <DndContext
          sensors={sensors}
          onDragStart={handleDragStart}
          onDragOver={handleDragOver}
          onDragEnd={handleDragEnd}
          onDragCancel={handleDragCancel}
        >
          <SortableSequenceStepList
            items={stepItems}
            onDelete={(item) => removeStep(item.id)}
            disabled={submitting || loading}
            emptyMessage="No steps yet. Add command, primitive tap, or wait-for-image steps."
            activeId={activeStepId}
            overId={overId}
          />
        </DndContext>
        <div className="form-hint">Steps run top-to-bottom; reorder to change execution order before saving.</div>
        {errors?.steps && <div className="field-error" role="alert">{errors.steps}</div>}
      </FormSection>

      <FormSection title="Detection" description="Optional detection target used for coordinate resolution." id="command-detection">
        <div className="field">
          <label htmlFor="command-detection-reference">Reference image ID</label>
          <input
            id="command-detection-reference"
            value={value.detection?.referenceImageId ?? ''}
            onChange={(e) => onChange({ ...value, detection: { ...(value.detection ?? {}), referenceImageId: e.target.value } })}
            disabled={submitting}
          />
          <div className="form-hint">Use an existing captured image ID; leave blank to skip detection.</div>
        </div>
        <div className="field grid-3">
          <div>
            <label htmlFor="command-detection-confidence">Confidence (0-1)</label>
            <input
              id="command-detection-confidence"
              type="number"
              step="0.01"
              min="0"
              max="1"
              value={value.detection?.confidence ?? ''}
              onChange={(e) => onChange({ ...value, detection: { ...(value.detection ?? {}), confidence: e.target.value } })}
              disabled={submitting}
            />
            <div className="form-hint">Lower values accept looser matches; 0.8 is a typical start.</div>
          </div>
          <div>
            <label htmlFor="command-detection-offset-x">Offset X</label>
            <input
              id="command-detection-offset-x"
              type="number"
              value={value.detection?.offsetX ?? ''}
              onChange={(e) => onChange({ ...value, detection: { ...(value.detection ?? {}), offsetX: e.target.value } })}
              disabled={submitting}
            />
            <div className="form-hint">Pixels to move right from the detected point (negative to move left).</div>
          </div>
          <div>
            <label htmlFor="command-detection-offset-y">Offset Y</label>
            <input
              id="command-detection-offset-y"
              type="number"
              value={value.detection?.offsetY ?? ''}
              onChange={(e) => onChange({ ...value, detection: { ...(value.detection ?? {}), offsetY: e.target.value } })}
              disabled={submitting}
            />
            <div className="form-hint">Pixels to move down from the detected point (negative to move up).</div>
          </div>
        </div>
        {errors?.detection && <div className="field-error" role="alert">{errors.detection}</div>}
      </FormSection>

      <FormActions submitting={submitting} onCancel={onCancel}>
        {loading && <span className="form-hint">Loading…</span>}
      </FormActions>
    </form>
  );
};
