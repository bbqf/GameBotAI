import React, { useMemo, useState } from 'react';
import { ImageSelectorDropdown } from '../images/ImageSelectorDropdown';
import { DndContext, DragEndEvent, DragOverEvent, DragStartEvent, PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import { arrayMove } from '@dnd-kit/sortable';
import { FormActions, FormSection } from '../unified/FormLayout';
import type { SearchableOption } from '../SearchableDropdown';
import type { ReorderableListItem } from '../ReorderableList';
import { SortableSequenceStepList } from '../SortableSequenceStepList';
import { ActionTypeSelector, PrimitiveActionType } from './ActionTypeSelector';
import { TapPanel } from './TapPanel';
import { WaitForImagePanel } from './WaitForImagePanel';
import { EnsureGameRunningPanel } from './EnsureGameRunningPanel';
import './CommandForm.css';

export type StepEntry = {
  id: string;
  type: 'Command' | 'PrimitiveTap' | 'WaitForImage' | 'EnsureGameRunning';
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
        label: `Tap: ${imageId}`,
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

    if (step.type === 'EnsureGameRunning') {
      return {
        id: step.id,
        label: 'Ensure game running',
        description: 'Checks foreground app; starts game if not running',
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

const EDITABLE_TYPES = new Set<string>(['PrimitiveTap', 'WaitForImage', 'EnsureGameRunning']);

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
  const [pendingActionType, setPendingActionType] = useState<PrimitiveActionType | ''>('');
  const [editingStepId, setEditingStepId] = useState<string | null>(null);
  const [activeStepId, setActiveStepId] = useState<string | null>(null);
  const [overId, setOverId] = useState<string | null>(null);

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }));
  const stepItems = useMemo(() => toStepItems(value.steps, commandOptions), [value.steps, commandOptions]);

  const addStep = (step: Omit<StepEntry, 'id'>) => {
    onChange({ ...value, steps: [...value.steps, { ...step, id: makeId() }] });
  };

  const updateStep = (id: string, step: Omit<StepEntry, 'id'>) => {
    const idx = value.steps.findIndex((s) => s.id === id);
    if (idx === -1) return;
    const next = [...value.steps];
    next[idx] = { ...step, id };
    onChange({ ...value, steps: next });
  };

  const removeStep = (itemId: string) => {
    onChange({ ...value, steps: value.steps.filter((s) => s.id !== itemId) });
  };

  const handleActionTypeChange = (next: PrimitiveActionType | '') => {
    setPendingActionType(next);
    setEditingStepId(null);
  };

  const handleEditStep = (item: ReorderableListItem) => {
    const step = value.steps.find((s) => s.id === item.id);
    if (!step || !EDITABLE_TYPES.has(step.type)) return;
    setEditingStepId(step.id);
    setPendingActionType(step.type as PrimitiveActionType);
  };

  const handlePanelConfirm = (step: Omit<StepEntry, 'id'>) => {
    if (editingStepId) {
      updateStep(editingStepId, step);
    } else {
      addStep(step);
    }
    setPendingActionType('');
    setEditingStepId(null);
  };

  const handlePanelCancel = () => {
    setPendingActionType('');
    setEditingStepId(null);
  };

  const editingStep = editingStepId ? value.steps.find((s) => s.id === editingStepId) : undefined;

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

      <FormSection title="Steps" description="Select an action type to add or edit steps." id="command-steps">
        <ActionTypeSelector
          value={pendingActionType}
          onChange={handleActionTypeChange}
          disabled={submitting || loading}
        />

        {pendingActionType === 'PrimitiveTap' && (
          <TapPanel
            initialValue={editingStep?.primitiveTap?.detectionTarget}
            onConfirm={(tapValue) =>
              handlePanelConfirm({
                type: 'PrimitiveTap',
                primitiveTap: { detectionTarget: tapValue },
              })
            }
            onCancel={handlePanelCancel}
            disabled={submitting}
          />
        )}

        {pendingActionType === 'WaitForImage' && (
          <WaitForImagePanel
            initialValue={
              editingStep?.waitForImage
                ? {
                    timeoutMs: editingStep.waitForImage.timeoutMs,
                    referenceImageId: editingStep.waitForImage.detectionTarget?.referenceImageId,
                    confidence: editingStep.waitForImage.detectionTarget?.confidence,
                  }
                : undefined
            }
            onConfirm={(wfValue) =>
              handlePanelConfirm({
                type: 'WaitForImage',
                waitForImage: {
                  timeoutMs: wfValue.timeoutMs,
                  detectionTarget: wfValue.referenceImageId?.trim()
                    ? { referenceImageId: wfValue.referenceImageId, confidence: wfValue.confidence }
                    : undefined,
                },
              })
            }
            onCancel={handlePanelCancel}
            disabled={submitting}
          />
        )}

        {pendingActionType === 'EnsureGameRunning' && (
          <EnsureGameRunningPanel
            onConfirm={() => handlePanelConfirm({ type: 'EnsureGameRunning' })}
            onCancel={handlePanelCancel}
            disabled={submitting}
          />
        )}

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
            onEdit={handleEditStep}
            disabled={submitting || loading}
            emptyMessage="No steps yet. Select an action type above to add steps."
            activeId={activeStepId}
            overId={overId}
          />
        </DndContext>
        <div className="form-hint">Steps run top-to-bottom; reorder to change execution order before saving.</div>
        {errors?.steps && <div className="field-error" role="alert">{errors.steps}</div>}
      </FormSection>

      <FormSection title="Detection" description="Optional detection target used for coordinate resolution." id="command-detection">
        <div className="field">
          <ImageSelectorDropdown
            id="command-detection-reference"
            label="Reference image ID"
            value={value.detection?.referenceImageId ?? ''}
            onChange={(id) => onChange({ ...value, detection: { ...(value.detection ?? {}), referenceImageId: id } })}
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
              onChange={(e) => onChange({ ...value, detection: { referenceImageId: '', ...(value.detection ?? {}), confidence: e.target.value } })}
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
              onChange={(e) => onChange({ ...value, detection: { referenceImageId: '', ...(value.detection ?? {}), offsetX: e.target.value } })}
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
              onChange={(e) => onChange({ ...value, detection: { referenceImageId: '', ...(value.detection ?? {}), offsetY: e.target.value } })}
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
