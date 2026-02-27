import React, { useMemo, useState } from 'react';
import { FormActions, FormSection } from '../unified/FormLayout';
import { SearchableDropdown, SearchableOption } from '../SearchableDropdown';
import { ReorderableList, ReorderableListItem } from '../ReorderableList';
import './CommandForm.css';

export type StepEntry = {
  id: string;
  type: 'Action' | 'Command' | 'PrimitiveTap';
  targetId?: string;
  primitiveTap?: {
    detectionTarget: DetectionTargetForm;
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
  actionOptions: SearchableOption[];
  commandOptions: SearchableOption[];
  submitting?: boolean;
  loading?: boolean;
  errors?: Record<string, string>;
  onChange: (next: CommandFormValue) => void;
  onSubmit?: () => void;
  onCancel?: () => void;
  onCreateNewAction?: () => void;
};

const toStepItems = (steps: StepEntry[], actionOpts: SearchableOption[], commandOpts: SearchableOption[]): ReorderableListItem[] => {
  const actionMap = new Map(actionOpts.map((o) => [o.value, o] as const));
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

    const targetId = step.targetId ?? '';
    const match = step.type === 'Action' ? actionMap.get(targetId) : commandMap.get(targetId);
    const prefix = step.type === 'Action' ? 'Action' : 'Command';
    return {
      id: step.id,
      label: `${prefix}: ${match?.label ?? targetId}`,
      description: match?.description,
    };
  });
};

export const CommandForm: React.FC<CommandFormProps> = ({
  value,
  actionOptions,
  commandOptions,
  submitting,
  loading,
  errors,
  onChange,
  onSubmit,
  onCancel,
  onCreateNewAction,
}) => {
  const [pendingActionId, setPendingActionId] = useState<string | undefined>(undefined);
  const [pendingCommandId, setPendingCommandId] = useState<string | undefined>(undefined);
  const [pendingPrimitiveReferenceImageId, setPendingPrimitiveReferenceImageId] = useState('');
  const [pendingPrimitiveConfidence, setPendingPrimitiveConfidence] = useState('');
  const [pendingPrimitiveOffsetX, setPendingPrimitiveOffsetX] = useState('0');
  const [pendingPrimitiveOffsetY, setPendingPrimitiveOffsetY] = useState('0');

  const stepItems = useMemo(() => toStepItems(value.steps, actionOptions, commandOptions), [value.steps, actionOptions, commandOptions]);

  const addStep = (step: Omit<StepEntry, 'id'>) => {
    const next = [...value.steps, { ...step, id: makeId() }];
    onChange({ ...value, steps: next });
  };

  const removeStep = (itemId: string) => {
    onChange({ ...value, steps: value.steps.filter((s) => s.id !== itemId) });
  };

  const updateStepOrder = (items: ReorderableListItem[]) => {
    const idToStep = new Map(value.steps.map((s) => [s.id, s] as const));
    const ordered = items.map((it) => idToStep.get(it.id)).filter(Boolean) as StepEntry[];
    onChange({ ...value, steps: ordered });
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

      <FormSection title="Actions" description="Choose actions and set their order." id="command-actions">
        <SearchableDropdown
          id="command-actions-dropdown"
          label="Add action"
          options={actionOptions}
          value={pendingActionId}
          onChange={setPendingActionId}
          onCreateNew={onCreateNewAction}
          disabled={submitting || loading}
          placeholder="Select an action"
          createLabel="Create new action"
        />
        <div className="field">
          <button
            type="button"
            onClick={() => {
              if (!pendingActionId) return;
              addStep({ type: 'Action', targetId: pendingActionId });
              setPendingActionId(undefined);
            }}
            disabled={submitting || loading || !pendingActionId}
          >
            Add action step
          </button>
        </div>

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

        <ReorderableList
          items={stepItems}
          onChange={updateStepOrder}
          onDelete={(item) => removeStep(item.id)}
          disabled={submitting || loading}
          emptyMessage="No steps yet. Add actions or commands."
        />
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
