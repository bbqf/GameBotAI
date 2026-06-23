import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { CollisionDetection, DndContext, DragEndEvent, DragOverEvent, DragStartEvent, PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import { arrayMove } from '@dnd-kit/sortable';
import { listSequences, SequenceDto, createSequence, getSequence, updateSequence, deleteSequence, isSequenceConflictError } from '../services/sequences';
import { ConfirmDeleteModal } from '../components/ConfirmDeleteModal';
import { ApiError } from '../lib/api';
import { listCommands, CommandDto } from '../services/commands';
import { FormError } from '../components/Form';
import { FormActions, FormSection } from '../components/unified/FormLayout';
import { SearchableDropdown, SearchableOption } from '../components/SearchableDropdown';
import type { ReorderableListItem } from '../components/ReorderableList';
import { SortableSequenceStepList } from '../components/SortableSequenceStepList';
import { useUnsavedChangesPrompt } from '../hooks/useUnsavedChangesPrompt';
import { validatePerStepConditions } from '../lib/validation';
import { isLinearStepArray, toCommandStepIds, toInterStepDelayRange, toLinearSteps } from '../lib/sequenceMapping';
import { LoopBlock } from '../components/sequences/LoopBlock';
import type { LoopStepEntry, BreakStepEntry, StepEntry } from '../types/stepEntry';
import type { SequenceLinearStep, LoopConfigDto, SequencePrimitiveActionPayload, SequenceCommandReference } from '../types/sequenceFlow';
import { ImageSelectorDropdown } from '../components/images/ImageSelectorDropdown';

type RescheduleOption = 'AtQueueStart' | 'OncePerRun' | 'Timer' | 'EveryStep';

type SequenceStep = {
  id: string;
  stepId: string;
  stepType: 'Action' | 'Loop' | 'Break';
  actionType: 'command' | 'WaitForImage' | 'reschedule-self';
  commandId: string;
  commandReference?: SequenceCommandReference;
  waitReferenceImageId: string;
  waitConfidence: string;
  waitTimeoutMs: string;
  conditionType: 'none' | 'imageVisible' | 'commandOutcome';
  conditionNegate: boolean;
  imageId: string;
  minSimilarity: string;
  outcomeStepRef: string;
  expectedState: 'success' | 'failed' | 'skipped';
  loopEntry?: LoopStepEntry;
  // Self-reschedule action fields (feature 065); only meaningful when actionType === 'reschedule-self'.
  rescheduleOption?: RescheduleOption;
  rescheduleTimerMode?: 'relative' | 'timeOfDay';
  rescheduleTimerRelativeOffset?: string;
  rescheduleTimerTimeOfDay?: string;
};

const RESCHEDULE_OPTIONS: ReadonlyArray<{ value: RescheduleOption; label: string }> = [
  { value: 'AtQueueStart', label: 'At Queue Start' },
  { value: 'OncePerRun', label: 'Once Per Run' },
  { value: 'Timer', label: 'Timer' },
  { value: 'EveryStep', label: 'After Every Step' }
];

const createDefaultRescheduleStep = (stepId: string): SequenceStep => ({
  id: makeId(),
  stepId,
  stepType: 'Action',
  actionType: 'reschedule-self',
  commandId: '',
  commandReference: undefined,
  waitReferenceImageId: '',
  waitConfidence: '',
  waitTimeoutMs: '1000',
  conditionType: 'none',
  conditionNegate: false,
  imageId: '',
  minSimilarity: '',
  outcomeStepRef: '',
  expectedState: 'success',
  rescheduleOption: 'OncePerRun',
  rescheduleTimerMode: 'relative',
  rescheduleTimerRelativeOffset: '00:10:00',
  rescheduleTimerTimeOfDay: '12:00'
});

type SequenceFormValue = {
  name: string;
  steps: SequenceStep[];
  useCustomDelayRange: boolean;
  delayMin: string;
  delayMax: string;
};

// Collision detection that uses the cursor position rather than the dragged item's
// bounding box, and only considers droppables in the same scope as the active item.
// This prevents body steps inside nested loops from being picked as collision targets
// when dragging a top-level step, which was causing the drop indicator to not appear.
const closestCenterToCursor: CollisionDetection = ({ active, droppableContainers, droppableRects, pointerCoordinates }) => {
  if (!pointerCoordinates) return [];
  const activeScope = active.data.current?.scopeId as string | undefined;
  let minDist = Infinity;
  let closestId: string | number | null = null;
  for (const container of droppableContainers) {
    if (activeScope && container.data.current?.scopeId !== activeScope) continue;
    const rect = droppableRects.get(container.id);
    if (!rect) continue;
    const dist = Math.abs(pointerCoordinates.y - (rect.top + rect.height / 2));
    if (dist < minDist) { minDist = dist; closestId = container.id; }
  }
  return closestId !== null ? [{ id: closestId }] : [];
};

const makeId = () => (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : Math.random().toString(36).slice(2));

const emptyForm: SequenceFormValue = {
  name: '',
  steps: [],
  useCustomDelayRange: false,
  delayMin: '',
  delayMax: ''
};

const createDefaultStep = (commandId: string, stepId: string, commandReference?: SequenceCommandReference): SequenceStep => ({
  id: makeId(),
  stepId,
  stepType: 'Action',
  actionType: 'command',
  commandId,
  commandReference,
  waitReferenceImageId: '',
  waitConfidence: '',
  waitTimeoutMs: '1000',
  conditionType: 'none',
  conditionNegate: false,
  imageId: '',
  minSimilarity: '',
  outcomeStepRef: '',
  expectedState: 'success'
});

const createDefaultWaitStep = (stepId: string): SequenceStep => ({
  id: makeId(),
  stepId,
  stepType: 'Action',
  actionType: 'WaitForImage',
  commandId: '',
  commandReference: undefined,
  waitReferenceImageId: '',
  waitConfidence: '',
  waitTimeoutMs: '1000',
  conditionType: 'none',
  conditionNegate: false,
  imageId: '',
  minSimilarity: '',
  outcomeStepRef: '',
  expectedState: 'success'
});

const toStepEntries = (ids?: string[]): SequenceStep[] => (ids ?? []).map((cmdId, index) => createDefaultStep(cmdId, `step-${index + 1}`));

const getUnresolvedCommandLabel = (reference: SequenceCommandReference | undefined, commandId: string): string => {
  const commandName = reference?.commandName?.trim();
  return commandName ? `${commandName} (unresolved)` : `${commandId} (unresolved)`;
};

const getDisplayCommandLabel = (
  commandId: string,
  commandReference: SequenceCommandReference | undefined,
  commandLookup: Map<string, string>
): string => {
  const liveLabel = commandLookup.get(commandId);
  return liveLabel ?? getUnresolvedCommandLabel(commandReference, commandId);
};

const appendUnresolvedCommandOption = (
  optionsByValue: Map<string, SearchableOption>,
  commandId: string,
  commandReference: SequenceCommandReference | undefined
) => {
  const normalizedId = commandId.trim();
  if (!normalizedId || optionsByValue.has(normalizedId)) {
    return;
  }

  optionsByValue.set(normalizedId, {
    value: normalizedId,
    label: getUnresolvedCommandLabel(commandReference, normalizedId)
  });
};

const collectLoopBodyCommandOptions = (optionsByValue: Map<string, SearchableOption>, body: StepEntry[] | undefined) => {
  if (!body) {
    return;
  }

  for (const entry of body) {
    if (entry.type === 'Action') {
      appendUnresolvedCommandOption(optionsByValue, entry.commandId, entry.commandReference);
      continue;
    }

    if (entry.type === 'Loop') {
      collectLoopBodyCommandOptions(optionsByValue, entry.body);
    }
  }
};

const mergeCommandOptionsWithUnresolved = (liveOptions: SearchableOption[], steps: SequenceStep[]): SearchableOption[] => {
  const optionsByValue = new Map(liveOptions.map((option) => [option.value, option]));
  for (const step of steps) {
    if (step.stepType === 'Action' && step.actionType === 'command') {
      appendUnresolvedCommandOption(optionsByValue, step.commandId, step.commandReference);
      continue;
    }

    if (step.stepType === 'Loop' && step.loopEntry) {
      collectLoopBodyCommandOptions(optionsByValue, step.loopEntry.body);
    }
  }

  return Array.from(optionsByValue.values());
};

const nextGeneratedStepId = (steps: SequenceStep[]): string => {
  const highest = steps.reduce((max, step) => {
    const match = /^step-(\d+)$/i.exec(step.stepId.trim());
    if (!match) {
      return max;
    }

    const parsed = Number(match[1]);
    return Number.isFinite(parsed) ? Math.max(max, parsed) : max;
  }, 0);

  return `step-${highest + 1}`;
};

const getPrimitiveAction = (step: SequenceLinearStep): SequencePrimitiveActionPayload | null => {
  const nestedPrimitive = (step.primitiveAction as { primitiveAction?: SequencePrimitiveActionPayload } | null | undefined)?.primitiveAction;
  if (nestedPrimitive && typeof nestedPrimitive.type === 'string') {
    return nestedPrimitive;
  }

  if (step.primitiveAction && typeof step.primitiveAction.type === 'string') {
    return step.primitiveAction;
  }

  if (step.action && typeof step.action.type === 'string') {
    return {
      type: step.action.type,
      schemaVersion: 'v1',
      payload: step.action.parameters
    };
  }

  return null;
};

const linearBodyToStepEntries = (body: SequenceLinearStep[]): StepEntry[] => {
  return body.map<StepEntry>((child) => {
    if (child.stepType === 'Break') {
      return {
        type: 'Break',
        id: makeId(),
        stepId: child.stepId,
        breakCondition: child.breakCondition ?? undefined
      } satisfies BreakStepEntry;
    }
    const primitiveAction = getPrimitiveAction(child);
    const cmdId = typeof primitiveAction?.payload?.commandId === 'string'
      ? primitiveAction.payload.commandId
      : typeof child.action?.parameters?.commandId === 'string'
        ? child.action.parameters.commandId
      : child.stepId;
    return {
      type: 'Action',
      id: makeId(),
      stepId: child.stepId,
      commandId: cmdId,
      commandReference: child.commandReference ?? undefined,
      conditionType: child.condition?.type === 'imageVisible' ? 'imageVisible'
        : child.condition?.type === 'commandOutcome' ? 'commandOutcome' : 'none',
      conditionNegate: child.condition?.negate ?? false,
      imageId: child.condition?.type === 'imageVisible' ? child.condition.imageId : '',
      minSimilarity: child.condition?.type === 'imageVisible' && child.condition.minSimilarity != null ? String(child.condition.minSimilarity) : '',
      outcomeStepRef: child.condition?.type === 'commandOutcome' ? child.condition.stepRef : '',
      expectedState: child.condition?.type === 'commandOutcome' ? child.condition.expectedState : 'success'
    };
  });
};

const loopDtoToEntry = (step: SequenceLinearStep): LoopStepEntry => {
  const loop = step.loop!;
  const base: Pick<LoopStepEntry, 'type' | 'id' | 'stepId'> = { type: 'Loop', id: makeId(), stepId: step.stepId };
  const body = linearBodyToStepEntries(step.body ?? []);
  if (loop.loopType === 'count') {
    return { ...base, loopType: 'count', count: loop.count, maxIterations: loop.maxIterations ?? undefined, body };
  }
  if (loop.loopType === 'while') {
    return { ...base, loopType: 'while', condition: loop.condition, maxIterations: loop.maxIterations ?? undefined, body };
  }
  // repeatUntil
  return { ...base, loopType: 'repeatUntil', condition: loop.condition, maxIterations: loop.maxIterations ?? undefined, body };
};

const toStepEntriesFromLinear = (steps: SequenceLinearStep[]): SequenceStep[] => {
  return steps.map((step) => {
    if (step.stepType === 'Loop' && step.loop) {
      const loopEntry = loopDtoToEntry(step);
      return {
        id: makeId(),
        stepId: step.stepId,
        stepType: 'Loop' as const,
        actionType: 'command' as const,
        commandId: '',
        commandReference: undefined,
        waitReferenceImageId: '',
        waitConfidence: '',
        waitTimeoutMs: '1000',
        conditionType: 'none' as const,
        conditionNegate: false,
        imageId: '',
        minSimilarity: '',
        outcomeStepRef: '',
        expectedState: 'success' as const,
        loopEntry
      };
    }

    if (step.stepType === 'Break') {
      return {
        id: makeId(),
        stepId: step.stepId,
        stepType: 'Break' as const,
        actionType: 'command' as const,
        commandId: '',
        waitReferenceImageId: '',
        waitConfidence: '',
        waitTimeoutMs: '1000',
        conditionType: 'none' as const,
        conditionNegate: false,
        imageId: '',
        minSimilarity: '',
        outcomeStepRef: '',
        expectedState: 'success' as const
      };
    }

    const primitiveAction = getPrimitiveAction(step);
    if (primitiveAction?.type === 'reschedule-self') {
      const payload = primitiveAction.payload ?? {};
      const option = (typeof payload.option === 'string' ? payload.option : 'OncePerRun') as RescheduleOption;
      const relative = typeof payload.timerRelativeOffset === 'string' ? payload.timerRelativeOffset : undefined;
      const timeOfDay = typeof payload.timerTimeOfDay === 'string' ? payload.timerTimeOfDay : undefined;
      const base = createDefaultRescheduleStep(step.stepId);
      const rescheduleStep: SequenceStep = {
        ...base,
        rescheduleOption: option,
        rescheduleTimerMode: timeOfDay ? 'timeOfDay' : 'relative',
        rescheduleTimerRelativeOffset: relative ?? base.rescheduleTimerRelativeOffset,
        rescheduleTimerTimeOfDay: timeOfDay ? timeOfDay.slice(0, 5) : base.rescheduleTimerTimeOfDay,
      };
      if (step.condition?.type === 'imageVisible') {
        return {
          ...rescheduleStep,
          conditionType: 'imageVisible',
          conditionNegate: step.condition.negate ?? false,
          imageId: step.condition.imageId,
          minSimilarity: step.condition.minSimilarity == null ? '' : String(step.condition.minSimilarity),
        };
      }
      if (step.condition?.type === 'commandOutcome') {
        return {
          ...rescheduleStep,
          conditionType: 'commandOutcome',
          conditionNegate: step.condition.negate ?? false,
          outcomeStepRef: step.condition.stepRef,
          expectedState: step.condition.expectedState,
        };
      }
      return rescheduleStep;
    }
    if (primitiveAction?.type === 'WaitForImage') {
      const detectionTarget = primitiveAction.payload?.detectionTarget as Record<string, unknown> | undefined;
      const confidence = typeof detectionTarget?.confidence === 'number' ? String(detectionTarget.confidence) : '';
      const timeoutMs = typeof primitiveAction.payload?.timeoutMs === 'number' ? String(primitiveAction.payload.timeoutMs) : '1000';
      const baseStep = createDefaultWaitStep(step.stepId);

      if (step.condition?.type === 'imageVisible') {
        return {
          ...baseStep,
          conditionType: 'imageVisible',
          conditionNegate: step.condition.negate ?? false,
          imageId: step.condition.imageId,
          minSimilarity: step.condition.minSimilarity == null ? '' : String(step.condition.minSimilarity),
          waitReferenceImageId: typeof detectionTarget?.referenceImageId === 'string' ? detectionTarget.referenceImageId : '',
          waitConfidence: confidence,
          waitTimeoutMs: timeoutMs,
        };
      }

      if (step.condition?.type === 'commandOutcome') {
        return {
          ...baseStep,
          conditionType: 'commandOutcome',
          conditionNegate: step.condition.negate ?? false,
          outcomeStepRef: step.condition.stepRef,
          expectedState: step.condition.expectedState,
          waitReferenceImageId: typeof detectionTarget?.referenceImageId === 'string' ? detectionTarget.referenceImageId : '',
          waitConfidence: confidence,
          waitTimeoutMs: timeoutMs,
        };
      }

      return {
        ...baseStep,
        waitReferenceImageId: typeof detectionTarget?.referenceImageId === 'string' ? detectionTarget.referenceImageId : '',
        waitConfidence: confidence,
        waitTimeoutMs: timeoutMs,
      };
    }

    const commandId = typeof primitiveAction?.payload?.commandId === 'string'
      ? primitiveAction.payload.commandId
      : typeof step.action?.parameters?.commandId === 'string'
        ? step.action.parameters.commandId
        : step.stepId;

    if (step.condition?.type === 'imageVisible') {
      return {
        id: makeId(),
        stepId: step.stepId,
        stepType: 'Action' as const,
        actionType: 'command' as const,
        commandId,
        commandReference: step.commandReference ?? undefined,
        waitReferenceImageId: '',
        waitConfidence: '',
        waitTimeoutMs: '1000',
        conditionType: 'imageVisible',
        conditionNegate: step.condition.negate ?? false,
        imageId: step.condition.imageId,
        minSimilarity: step.condition.minSimilarity == null ? '' : String(step.condition.minSimilarity),
        outcomeStepRef: '',
        expectedState: 'success' as const,
      };
    }

    if (step.condition?.type === 'commandOutcome') {
      return {
        id: makeId(),
        stepId: step.stepId,
        stepType: 'Action' as const,
        actionType: 'command' as const,
        commandId,
        commandReference: step.commandReference ?? undefined,
        waitReferenceImageId: '',
        waitConfidence: '',
        waitTimeoutMs: '1000',
        conditionType: 'commandOutcome',
        conditionNegate: step.condition.negate ?? false,
        imageId: '',
        minSimilarity: '',
        outcomeStepRef: step.condition.stepRef,
        expectedState: step.condition.expectedState,
      };
    }

    return {
      ...createDefaultStep(commandId, step.stepId, step.commandReference ?? undefined),
      stepId: step.stepId
    };
  });
};

const buildCommandReferencePayload = (commandId: string, commandReference: SequenceCommandReference | undefined) => {
  if (!commandId.trim()) {
    return undefined;
  }

  const commandName = commandReference?.commandName?.trim();
  if (!commandName && commandReference?.isResolved !== false) {
    return undefined;
  }

  return {
    commandId: commandId.trim(),
    commandName: commandName || undefined,
    isResolved: commandReference?.isResolved ?? false
  };
};

const buildConditionPayload = (step: SequenceStep) => {
  if (step.conditionType === 'imageVisible') {
    return {
      type: 'imageVisible' as const,
      imageId: step.imageId.trim(),
      minSimilarity: step.minSimilarity.trim() === '' ? null : Number(step.minSimilarity),
      negate: step.conditionNegate || undefined
    };
  }
  if (step.conditionType === 'commandOutcome') {
    return {
      type: 'commandOutcome' as const,
      stepRef: step.outcomeStepRef.trim(),
      expectedState: step.expectedState,
      negate: step.conditionNegate || undefined
    };
  }
  return null;
};

const buildLoopConfigPayload = (loop: LoopStepEntry): LoopConfigDto | null => {
  if (loop.loopType === 'count') {
    return { loopType: 'count', count: loop.count ?? 1, maxIterations: loop.maxIterations ?? null };
  }
  if (loop.loopType === 'while' && loop.condition) {
    return { loopType: 'while', condition: loop.condition, maxIterations: loop.maxIterations ?? null };
  }
  if (loop.loopType === 'repeatUntil' && loop.condition) {
    return { loopType: 'repeatUntil', condition: loop.condition, maxIterations: loop.maxIterations ?? null };
  }
  return null;
};

const bodyEntryToPayloadStep = (entry: StepEntry): SequenceLinearStep => {
  if (entry.type === 'Break') {
    const br = entry as BreakStepEntry;
    return {
      stepId: br.stepId.trim(),
      stepType: 'Break',
      breakCondition: br.breakCondition ?? null
    };
  }
  // Action body step
  const a = entry as unknown as { stepId: string; commandId: string; commandReference?: SequenceCommandReference; conditionType: string; conditionNegate: boolean; imageId: string; minSimilarity: string; outcomeStepRef: string; expectedState: 'success' | 'failed' | 'skipped' };
  const cond = a.conditionType === 'imageVisible'
    ? { type: 'imageVisible' as const, imageId: a.imageId.trim(), minSimilarity: a.minSimilarity.trim() === '' ? null : Number(a.minSimilarity), negate: a.conditionNegate || undefined }
    : a.conditionType === 'commandOutcome'
      ? { type: 'commandOutcome' as const, stepRef: a.outcomeStepRef.trim(), expectedState: a.expectedState, negate: a.conditionNegate || undefined }
      : null;
  return {
    stepId: entry.stepId.trim(),
    stepType: 'Action',
    primitiveAction: { type: 'command', schemaVersion: 'v1', payload: { commandId: a.commandId } },
    commandReference: buildCommandReferencePayload(a.commandId, a.commandReference),
    condition: cond
  };
};

const toLinearPayloadSteps = (steps: SequenceStep[]): SequenceLinearStep[] => {
  return steps.map((step) => {
    if (step.stepType === 'Loop' && step.loopEntry) {
      return {
        stepId: step.stepId.trim(),
        stepType: 'Loop' as const,
        loop: buildLoopConfigPayload(step.loopEntry),
        body: step.loopEntry.body.map(bodyEntryToPayloadStep)
      };
    }

    if (step.stepType === 'Break') {
      return {
        stepId: step.stepId.trim(),
        stepType: 'Break' as const,
        breakCondition: null // top-level breaks are unconditional
      };
    }

    if (step.actionType === 'reschedule-self') {
      const option = step.rescheduleOption ?? 'OncePerRun';
      const payload: Record<string, unknown> = { option };
      if (option === 'Timer') {
        if (step.rescheduleTimerMode === 'timeOfDay') {
          const t = (step.rescheduleTimerTimeOfDay ?? '').trim();
          payload.timerTimeOfDay = t.length === 5 ? `${t}:00` : t;
        } else {
          payload.timerRelativeOffset = (step.rescheduleTimerRelativeOffset ?? '').trim();
        }
      }
      return {
        stepId: step.stepId.trim(),
        stepType: 'Action' as const,
        primitiveAction: { type: 'reschedule-self', schemaVersion: '1', payload },
        condition: buildConditionPayload(step)
      };
    }

    if (step.actionType === 'WaitForImage') {
      const detectionTarget = step.waitReferenceImageId.trim()
        ? {
          referenceImageId: step.waitReferenceImageId.trim(),
          confidence: step.waitConfidence.trim() === '' ? undefined : Number(step.waitConfidence),
        }
        : undefined;

      return {
        stepId: step.stepId.trim(),
        stepType: 'Action' as const,
        primitiveAction: {
          type: 'WaitForImage',
          schemaVersion: 'v1',
          payload: {
            timeoutMs: Number(step.waitTimeoutMs),
            ...(detectionTarget ? { detectionTarget } : {})
          }
        },
        condition: buildConditionPayload(step)
      };
    }

    return {
      stepId: step.stepId.trim(),
      stepType: 'Action' as const,
      primitiveAction: {
        type: 'command',
        schemaVersion: 'v1',
        payload: {
          commandId: step.commandId
        }
      },
      commandReference: buildCommandReferencePayload(step.commandId, step.commandReference),
      condition: buildConditionPayload(step)
    };
  });
};

type SequencesPageProps = {
  initialCreate?: boolean;
  initialEditId?: string;
};

export const SequencesPage: React.FC<SequencesPageProps> = ({ initialCreate, initialEditId }) => {
  const [sequences, setSequences] = useState<SequenceDto[]>([]);
  const [creating, setCreating] = useState(Boolean(initialCreate));
  const [commandOptions, setCommandOptions] = useState<SearchableOption[]>([]);
  const [errors, setErrors] = useState<Record<string, string> | undefined>(undefined);
  const [editingId, setEditingId] = useState<string | undefined>(undefined);
  const [form, setForm] = useState<SequenceFormValue>(emptyForm);
  const [pendingStepId, setPendingStepId] = useState<string | undefined>(undefined);
  const [pendingWaitReferenceImageId, setPendingWaitReferenceImageId] = useState('');
  const [pendingWaitConfidence, setPendingWaitConfidence] = useState('');
  const [pendingWaitTimeoutMs, setPendingWaitTimeoutMs] = useState('1000');
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteMessage, setDeleteMessage] = useState<string | undefined>(undefined);
  const [deleteReferences, setDeleteReferences] = useState<Record<string, Array<{ id: string; name: string }>> | undefined>(undefined);
  const [submitting, setSubmitting] = useState(false);
  const [loading, setLoading] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [activeStepId, setActiveStepId] = useState<string | null>(null);
  const [overId, setOverId] = useState<string | null>(null);
  const [isDragInvalid, setIsDragInvalid] = useState(false);

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }));

  const handleDragStart = (event: DragStartEvent) => {
    setActiveStepId(event.active.id as string);
    setOverId(null);
    setIsDragInvalid(false);
  };

  const handleDragOver = (event: DragOverEvent) => {
    const activeScope = event.active.data.current?.scopeId;
    const overScope = event.over?.data.current?.scopeId;
    setIsDragInvalid(!!activeScope && !!overScope && activeScope !== overScope);
    setOverId(event.over?.id as string ?? null);
  };

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    setActiveStepId(null);
    setOverId(null);
    setIsDragInvalid(false);
    if (!over || active.id === over.id) return;
    const activeScope = active.data.current?.scopeId as string | undefined;
    const overScope = over.data.current?.scopeId as string | undefined;
    if (!activeScope || !overScope || activeScope !== overScope) return;

    if (activeScope === 'root') {
      setForm((prev) => {
        const oldIndex = prev.steps.findIndex((s) => s.id === active.id);
        const newIndex = prev.steps.findIndex((s) => s.id === over.id);
        if (oldIndex === -1 || newIndex === -1) return prev;
        return { ...prev, steps: arrayMove(prev.steps, oldIndex, newIndex) };
      });
      setDirty(true);
    } else {
      setForm((prev) => {
        const loopStep = prev.steps.find((s) => s.stepType === 'Loop' && s.loopEntry?.id === activeScope);
        if (!loopStep?.loopEntry) return prev;
        const body = loopStep.loopEntry.body;
        const oldIndex = body.findIndex((s) => s.id === active.id);
        const newIndex = body.findIndex((s) => s.id === over.id);
        if (oldIndex === -1 || newIndex === -1) return prev;
        const newBody = arrayMove(body, oldIndex, newIndex);
        return {
          ...prev,
          steps: prev.steps.map((s) =>
            s.id === loopStep.id ? { ...s, loopEntry: { ...s.loopEntry!, body: newBody } } : s
          ),
        };
      });
      setDirty(true);
    }
  };

  const handleDragCancel = () => {
    setActiveStepId(null);
    setOverId(null);
    setIsDragInvalid(false);
  };
  const [filterName, setFilterName] = useState('');
  const [tableMessage, setTableMessage] = useState<string | undefined>(undefined);
  const [tableError, setTableError] = useState<string | undefined>(undefined);
  const [loadedVersion, setLoadedVersion] = useState(1);

  useEffect(() => {
    let mounted = true;
    setLoading(true);
    Promise.all([listSequences(), listCommands()])
      .then(([seqs, cmds]: [SequenceDto[], CommandDto[]]) => {
        if (!mounted) return;
        setSequences(seqs);
        setCommandOptions(cmds.map((c) => ({ value: c.id, label: c.name })));
        setTableError(undefined);
      })
      .catch((err: any) => {
        if (!mounted) return;
        setSequences([]);
        setCommandOptions([]);
        setTableError(err?.message ?? 'Failed to load sequences');
      })
      .finally(() => {
        if (mounted) setLoading(false);
      });
    return () => {
      mounted = false;
    };
  }, []);

  const { confirmNavigate } = useUnsavedChangesPrompt(dirty);

  const commandLookup = useMemo(() => new Map(commandOptions.map((o) => [o.value, o.label])), [commandOptions]);
  const editorCommandOptions = useMemo(() => mergeCommandOptionsWithUnresolved(commandOptions, form.steps), [commandOptions, form.steps]);
  const sequenceRows = useMemo(() => sequences.map((s) => ({ id: s.id, name: s.name, stepCount: s.steps?.length ?? 0 })), [sequences]);

  const displayedSequences = useMemo(() => {
    const query = filterName.trim().toLowerCase();
    return sequenceRows
      .filter((s) => !query || s.name.toLowerCase().includes(query))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [sequenceRows, filterName]);

  const reloadSequences = async () => {
    setLoading(true);
    try {
      const data = await listSequences();
      setSequences(data);
      setTableError(undefined);
    } catch (err: any) {
      setSequences([]);
      setTableError(err?.message ?? 'Failed to load sequences');
    } finally {
      setLoading(false);
    }
  };

  const loadSequenceIntoForm = async (id: string) => {
    const s = await getSequence(id);
    const commandIds = toCommandStepIds(s.steps);
    const linearSteps = toLinearSteps(s.steps);
    const hasPerStep = isLinearStepArray(s.steps) && linearSteps.length > 0;
    const delayRange = toInterStepDelayRange(s.interStepDelayRangeMs);
    setEditingId(id);
    setCreating(false);
    setPendingStepId(undefined);
    setForm({
      name: s.name,
      steps: hasPerStep ? toStepEntriesFromLinear(linearSteps) : toStepEntries(commandIds),
      useCustomDelayRange: delayRange !== null,
      delayMin: delayRange ? String(delayRange.min) : '',
      delayMax: delayRange ? String(delayRange.max) : ''
    });
    setLoadedVersion(s.version ?? 1);
    setDirty(false);
  };

  const resetForm = () => {
    setForm(emptyForm);
    setPendingStepId(undefined);
    setPendingWaitReferenceImageId('');
    setPendingWaitConfidence('');
    setPendingWaitTimeoutMs('1000');
    setLoadedVersion(1);
    setErrors(undefined);
    setDirty(false);
  };

  const renderStepConditionEditor = useCallback((step: SequenceStep, index: number): React.ReactNode => (
    <div className="sequence-step-condition-editor">
      {step.stepType === 'Action' && step.actionType === 'WaitForImage' && (
        <>
          <div className="sequence-step-condition-field sequence-step-condition-field--image-id">
            <ImageSelectorDropdown
              id={`step-wait-image-id-${step.id}`}
              label="Wait image ID"
              value={step.waitReferenceImageId}
              onChange={(id) => {
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((candidate) => candidate.id === step.id
                    ? { ...candidate, waitReferenceImageId: id, waitConfidence: id ? candidate.waitConfidence : '' }
                    : candidate)
                }));
                setDirty(true);
              }}
              disabled={submitting || loading}
            />
          </div>

          <div className="sequence-step-condition-field sequence-step-condition-field--min-similarity">
            <label htmlFor={`step-wait-confidence-${step.id}`}>Wait confidence (0-1)</label>
            <input
              id={`step-wait-confidence-${step.id}`}
              inputMode="decimal"
              value={step.waitConfidence}
              onChange={(event) => {
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, waitConfidence: event.target.value } : candidate)
                }));
                setDirty(true);
              }}
              disabled={submitting || loading || !step.waitReferenceImageId.trim()}
            />
          </div>

          <div className="sequence-step-condition-field sequence-step-condition-field--expected-state">
            <label htmlFor={`step-wait-timeout-${step.id}`}>Wait timeout (ms)</label>
            <input
              id={`step-wait-timeout-${step.id}`}
              inputMode="numeric"
              value={step.waitTimeoutMs}
              onChange={(event) => {
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, waitTimeoutMs: event.target.value } : candidate)
                }));
                setDirty(true);
              }}
              disabled={submitting || loading}
            />
          </div>
        </>
      )}

      {step.stepType === 'Action' && step.actionType === 'reschedule-self' && (
        <>
          <div className="sequence-step-condition-field sequence-step-condition-field--reschedule-option">
            <label htmlFor={`step-reschedule-option-${step.id}`}>Reschedule option</label>
            <select
              id={`step-reschedule-option-${step.id}`}
              value={step.rescheduleOption ?? 'OncePerRun'}
              onChange={(event) => {
                const value = event.target.value as RescheduleOption;
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, rescheduleOption: value } : candidate)
                }));
                setDirty(true);
              }}
              disabled={submitting || loading}
            >
              {RESCHEDULE_OPTIONS.map((opt) => (
                <option key={opt.value} value={opt.value}>{opt.label}</option>
              ))}
            </select>
          </div>

          {(step.rescheduleOption ?? 'OncePerRun') === 'Timer' && (
            <>
              <div className="sequence-step-condition-field sequence-step-condition-field--reschedule-timer-mode">
                <label htmlFor={`step-reschedule-timer-mode-${step.id}`}>Timer mode</label>
                <select
                  id={`step-reschedule-timer-mode-${step.id}`}
                  value={step.rescheduleTimerMode ?? 'relative'}
                  onChange={(event) => {
                    const value = event.target.value as 'relative' | 'timeOfDay';
                    setForm((prev) => ({
                      ...prev,
                      steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, rescheduleTimerMode: value } : candidate)
                    }));
                    setDirty(true);
                  }}
                  disabled={submitting || loading}
                >
                  <option value="relative">Relative offset</option>
                  <option value="timeOfDay">Time of day</option>
                </select>
              </div>

              {(step.rescheduleTimerMode ?? 'relative') === 'relative' ? (
                <div className="sequence-step-condition-field sequence-step-condition-field--reschedule-offset">
                  <label htmlFor={`step-reschedule-offset-${step.id}`}>Relative offset (HH:mm:ss)</label>
                  <input
                    id={`step-reschedule-offset-${step.id}`}
                    value={step.rescheduleTimerRelativeOffset ?? ''}
                    placeholder="00:10:00"
                    onChange={(event) => {
                      setForm((prev) => ({
                        ...prev,
                        steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, rescheduleTimerRelativeOffset: event.target.value } : candidate)
                      }));
                      setDirty(true);
                    }}
                    disabled={submitting || loading}
                  />
                </div>
              ) : (
                <div className="sequence-step-condition-field sequence-step-condition-field--reschedule-time-of-day">
                  <label htmlFor={`step-reschedule-time-${step.id}`}>Time of day</label>
                  <input
                    id={`step-reschedule-time-${step.id}`}
                    type="time"
                    value={step.rescheduleTimerTimeOfDay ?? ''}
                    onChange={(event) => {
                      setForm((prev) => ({
                        ...prev,
                        steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, rescheduleTimerTimeOfDay: event.target.value } : candidate)
                      }));
                      setDirty(true);
                    }}
                    disabled={submitting || loading}
                  />
                </div>
              )}
            </>
          )}
        </>
      )}

      <div className="sequence-step-condition-field sequence-step-condition-field--type">
        <label htmlFor={`step-condition-type-${step.id}`}>Condition Type</label>
        <select
          id={`step-condition-type-${step.id}`}
          value={step.conditionType}
          onChange={(event) => {
            const value = event.target.value as SequenceStep['conditionType'];
            setForm((prev) => ({
              ...prev,
              steps: prev.steps.map((candidate) => candidate.id === step.id
                ? {
                  ...candidate,
                  conditionType: value,
                  imageId: value === 'imageVisible' ? candidate.imageId : '',
                  minSimilarity: value === 'imageVisible' ? candidate.minSimilarity : '',
                  outcomeStepRef: value === 'commandOutcome' ? candidate.outcomeStepRef : '',
                  expectedState: value === 'commandOutcome' ? candidate.expectedState : 'success'
                }
                : candidate)
            }));
            setDirty(true);
          }}
          disabled={submitting || loading}
        >
          <option value="none">None</option>
          <option value="imageVisible">imageVisible</option>
          <option value="commandOutcome">commandOutcome</option>
        </select>
      </div>

      {step.conditionType !== 'none' && (
        <div className="sequence-step-condition-field sequence-step-condition-field--negate">
          <label>
            <input
              type="checkbox"
              data-testid={`step-condition-negate-${step.id}`}
              checked={step.conditionNegate}
              onChange={(e) => {
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, conditionNegate: e.target.checked } : candidate)
                }));
                setDirty(true);
              }}
              disabled={submitting || loading}
            />
            NOT
          </label>
        </div>
      )}

      {step.conditionType === 'imageVisible' && (
        <>
          <div className="sequence-step-condition-field sequence-step-condition-field--image-id">
            <ImageSelectorDropdown
              id={`step-image-id-${step.id}`}
              label="Image Id"
              value={step.imageId}
              onChange={(id) => {
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, imageId: id } : candidate)
                }));
                setDirty(true);
              }}
              disabled={submitting || loading}
            />
          </div>

          <div className="sequence-step-condition-field sequence-step-condition-field--min-similarity">
            <label htmlFor={`step-min-similarity-${step.id}`}>Min Similarity</label>
            <input
              id={`step-min-similarity-${step.id}`}
              inputMode="decimal"
              placeholder="0–1 (default: 0.85)"
              value={step.minSimilarity}
              onChange={(event) => {
                const raw = event.target.value;
                if (raw === '') {
                  setForm((prev) => ({
                    ...prev,
                    steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, minSimilarity: '' } : candidate)
                  }));
                  setDirty(true);
                  return;
                }
                // Allow any partial decimal that could become a valid 0-1 number
                if (/^[01]?\.\d*$|^\.\d*$/.test(raw)) {
                  const parsed = Number(raw);
                  if (isNaN(parsed) || parsed <= 1) {
                    setForm((prev) => ({
                      ...prev,
                      steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, minSimilarity: raw } : candidate)
                    }));
                    setDirty(true);
                  }
                  return;
                }
                const num = Number(raw);
                if (!isNaN(num) && num >= 0 && num <= 1) {
                  setForm((prev) => ({
                    ...prev,
                    steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, minSimilarity: raw } : candidate)
                  }));
                  setDirty(true);
                }
              }}
              disabled={submitting || loading}
            />
          </div>
        </>
      )}

      {step.conditionType === 'commandOutcome' && (
        <>
          <div className="sequence-step-condition-field sequence-step-condition-field--step-ref">
            <label htmlFor={`step-step-ref-${step.id}`}>Step Ref</label>
            <select
              id={`step-step-ref-${step.id}`}
              value={step.outcomeStepRef}
              onChange={(event) => {
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, outcomeStepRef: event.target.value } : candidate)
                }));
                setDirty(true);
              }}
              disabled={submitting || loading}
            >
              <option value="">Select prior step</option>
              {form.steps.slice(0, index).map((candidate, candidateIndex) => (
                <option key={candidate.id} value={candidate.stepId}>{`Step ${candidateIndex + 1}`}</option>
              ))}
            </select>
          </div>

          <div className="sequence-step-condition-field sequence-step-condition-field--expected-state">
            <label htmlFor={`step-expected-state-${step.id}`}>Expected State</label>
            <select
              id={`step-expected-state-${step.id}`}
              value={step.expectedState}
              onChange={(event) => {
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((candidate) => candidate.id === step.id
                    ? { ...candidate, expectedState: event.target.value as SequenceStep['expectedState'] }
                    : candidate)
                }));
                setDirty(true);
              }}
              disabled={submitting || loading}
            >
              <option value="success">success</option>
              <option value="failed">failed</option>
              <option value="skipped">skipped</option>
            </select>
          </div>
        </>
      )}
    </div>
  ), [form.steps, submitting, loading]);

  const createLoopStep = (loopType: 'count' | 'while' | 'repeatUntil'): SequenceStep => {
    const id = makeId();
    const stepId = nextGeneratedStepId(form.steps);
    const loopEntry: LoopStepEntry = {
      type: 'Loop',
      id,
      stepId,
      loopType,
      count: loopType === 'count' ? 3 : undefined,
      condition: loopType !== 'count' ? { type: 'imageVisible', imageId: '', minSimilarity: null } : undefined,
      body: [],
    };
    return {
      id,
      stepId,
      stepType: 'Loop',
      actionType: 'command',
      commandId: '',
      waitReferenceImageId: '',
      waitConfidence: '',
      waitTimeoutMs: '1000',
      conditionType: 'none',
      conditionNegate: false,
      imageId: '',
      minSimilarity: '',
      outcomeStepRef: '',
      expectedState: 'success',
      loopEntry,
    };
  };

  const stepItems = useMemo<ReorderableListItem[]>(() => {
    return form.steps.map((s, idx) => {
      if (s.stepType === 'Loop' && s.loopEntry) {
        return {
          id: s.id,
          label: `Loop (${s.loopEntry.loopType})`,
          details: (
            <LoopBlock
              loop={s.loopEntry}
              onChange={(updated) => {
                setForm((prev) => ({
                  ...prev,
                  steps: prev.steps.map((step) =>
                    step.id === s.id ? { ...step, loopEntry: updated } : step
                  ),
                }));
                setDirty(true);
              }}
              onRemove={() => {
                setForm((prev) => ({ ...prev, steps: prev.steps.filter((step) => step.id !== s.id) }));
                setDirty(true);
              }}
              commandOptions={editorCommandOptions}
              disabled={submitting || loading}
              isDropInvalid={isDragInvalid}
              activeBodyStepId={s.loopEntry.body.some(b => b.id === activeStepId) ? activeStepId : null}
              overBodyStepId={s.loopEntry.body.some(b => b.id === overId) ? overId : null}
            />
          ),
        };
      }
      return {
        id: s.id,
        label: s.actionType === 'WaitForImage'
          ? `Wait for image: ${s.waitReferenceImageId.trim() || 'any image'}`
          : s.actionType === 'reschedule-self'
            ? `Reschedule this sequence (${RESCHEDULE_OPTIONS.find((o) => o.value === (s.rescheduleOption ?? 'OncePerRun'))?.label})`
            : getDisplayCommandLabel(s.commandId, s.commandReference, commandLookup),
        details: renderStepConditionEditor(s, idx),
      };
    });
  }, [form.steps, editorCommandOptions, commandLookup, submitting, loading, renderStepConditionEditor, isDragInvalid, activeStepId, overId]);

  const validate = (v: SequenceFormValue): Record<string, string> | undefined => {
    const next: Record<string, string> = {};
    if (!v.name.trim()) next.name = 'Name is required';

    for (const step of v.steps) {
      if (step.stepType !== 'Action') {
        continue;
      }

      if (step.actionType === 'command' && !step.commandId.trim()) {
        next.steps = 'Command steps require a command selection';
        break;
      }

      if (step.actionType === 'WaitForImage') {
        const timeout = step.waitTimeoutMs.trim();
        if (!timeout) {
          next.steps = 'Wait for image steps require a timeout in milliseconds';
          break;
        }

        const parsedTimeout = Number(timeout);
        if (!Number.isInteger(parsedTimeout) || parsedTimeout < 0) {
          next.steps = 'Wait for image timeout must be a non-negative integer';
          break;
        }
      }

      if (step.actionType === 'reschedule-self' && (step.rescheduleOption ?? 'OncePerRun') === 'Timer') {
        if ((step.rescheduleTimerMode ?? 'relative') === 'timeOfDay') {
          if (!(step.rescheduleTimerTimeOfDay ?? '').trim()) {
            next.steps = 'Timer reschedule requires a time of day';
            break;
          }
        } else if (!/^\d{1,2}:\d{2}:\d{2}$/.test((step.rescheduleTimerRelativeOffset ?? '').trim())) {
          next.steps = 'Timer reschedule requires a relative offset as HH:mm:ss';
          break;
        }
      }
    }

    if (v.useCustomDelayRange) {
      if (!v.delayMin.trim()) {
        next.delayMin = 'Minimum delay is required when custom range is enabled';
      }
      if (!v.delayMax.trim()) {
        next.delayMax = 'Maximum delay is required when custom range is enabled';
      }

      const isInteger = (value: string) => /^-?\d+$/.test(value.trim());
      if (v.delayMin.trim() && !isInteger(v.delayMin)) {
        next.delayMin = 'Minimum delay must be an integer (milliseconds)';
      }
      if (v.delayMax.trim() && !isInteger(v.delayMax)) {
        next.delayMax = 'Maximum delay must be an integer (milliseconds)';
      }

      if (!next.delayMin && !next.delayMax) {
        const min = Number(v.delayMin);
        const max = Number(v.delayMax);
        if (min < 0) {
          next.delayMin = 'Minimum delay must be greater than or equal to 0';
        }
        if (max < 0) {
          next.delayMax = 'Maximum delay must be greater than or equal to 0';
        }
        if (min > max) {
          next.delayMin = 'Minimum delay must be less than or equal to maximum delay';
          next.delayMax = 'Maximum delay must be greater than or equal to minimum delay';
        }
      }
    }

    return Object.keys(next).length ? next : undefined;
  };

  useEffect(() => {
    if (!initialEditId) return;
    const load = async () => {
      try {
        setErrors(undefined);
        await loadSequenceIntoForm(initialEditId);
      } catch (err: any) {
        setErrors({ form: err?.message ?? 'Failed to load sequence' });
      }
    };
    void load();
  }, [initialEditId]);

  return (
    <section>
      <h2>Sequences</h2>
      {tableMessage && <div className="form-hint" role="status">{tableMessage}</div>}
      {tableError && <div className="form-error" role="alert">{tableError}</div>}
      <div className="actions-header">
        <button
          onClick={() => {
            if (!confirmNavigate()) return;
            setCreating(true);
            setEditingId(undefined);
            setErrors(undefined);
            resetForm();
          }}
        >
          Create Sequence
        </button>
      </div>
      {!loading && sequences.length === 0 && (
        <div className="form-hint" role="status">No sequences yet. Create your first sequence to start authoring automation flow.</div>
      )}
      <table className="sequences-table" aria-label="Sequences table">
        <thead>
          <tr>
            <th>
              <div>Name</div>
              <input
                aria-label="Filter by name"
                value={filterName}
                onChange={(e) => setFilterName(e.target.value)}
                placeholder="Filter by name"
                disabled={loading}
              />
            </th>
            <th>
              <div>Steps</div>
            </th>
          </tr>
        </thead>
        <tbody>
          {loading && (
            <tr><td colSpan={2}>Loading...</td></tr>
          )}
          {!loading && displayedSequences.length === 0 && (
            <tr><td colSpan={2}>No sequences found.</td></tr>
          )}
          {!loading && displayedSequences.length > 0 && displayedSequences.map((s) => (
            <tr key={s.id} className="sequences-row">
              <td>
                <button
                  type="button"
                  className="link-button"
                  onClick={async () => {
                    if (!confirmNavigate()) return;
                    try {
                      await loadSequenceIntoForm(s.id);
                    } catch (err: any) {
                      setErrors({ form: err?.message ?? 'Failed to load sequence' });
                    }
                  }}
                >
                  {s.name}
                </button>
              </td>
              <td>{s.stepCount}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {creating && (
        <form
          className="edit-form"
          onSubmit={async (e) => {
            e.preventDefault();
            const validation = validate(form);
            if (validation) {
              setErrors(validation);
              return;
            }

            const linearPayload = {
              name: form.name.trim(),
              version: 1,
              steps: toLinearPayloadSteps(form.steps),
              interStepDelayRangeMs: form.useCustomDelayRange
                ? { min: Number(form.delayMin), max: Number(form.delayMax) }
                : null
            };

            if (linearPayload) {
              const linearErrors = validatePerStepConditions(linearPayload.steps);
              if (linearErrors.length > 0) {
                setErrors({ form: linearErrors[0] });
                return;
              }
            }

            setSubmitting(true);
            try {
              await createSequence(
                {
                  name: linearPayload.name,
                  version: linearPayload.version,
                  steps: linearPayload.steps,
                  interStepDelayRangeMs: linearPayload.interStepDelayRangeMs
                }
              );
              setCreating(false);
              setForm(emptyForm);
              setPendingStepId(undefined);
              setDirty(false);
              setTableMessage('Sequence created successfully.');
              await reloadSequences();
            } catch (err: any) {
              setErrors({ form: err?.message ?? 'Failed to create sequence' });
            } finally {
              setSubmitting(false);
            }
          }}
        >
          <FormSection title="Basics" description="Primary details for the sequence." id="sequence-basics">
            <div className="field">
              <label htmlFor="sequence-name">Name *</label>
              <input
                id="sequence-name"
                value={form.name}
                onChange={(e) => { setErrors(undefined); setForm({ ...form, name: e.target.value }); setDirty(true); }}
                aria-invalid={Boolean(errors?.name)}
                aria-describedby={errors?.name ? 'sequence-name-error' : undefined}
                disabled={submitting || loading}
              />
              {errors?.name && <div id="sequence-name-error" className="field-error" role="alert">{errors.name}</div>}
            </div>
            <div className="field">
              <label>
                <input
                  type="checkbox"
                  checked={form.useCustomDelayRange}
                  onChange={(event) => {
                    const enabled = event.target.checked;
                    setForm((prev) => ({
                      ...prev,
                      useCustomDelayRange: enabled,
                      delayMin: enabled ? prev.delayMin : '',
                      delayMax: enabled ? prev.delayMax : ''
                    }));
                    setErrors(undefined);
                    setDirty(true);
                  }}
                  disabled={submitting || loading}
                />
                Use custom inter-step delay range
              </label>
              <div className="form-hint">Default runtime range is 100-300 ms when custom range is disabled.</div>
            </div>
            {form.useCustomDelayRange && (
              <>
                <div className="field">
                  <label htmlFor="sequence-delay-min">Minimum Delay (ms) *</label>
                  <input
                    id="sequence-delay-min"
                    inputMode="numeric"
                    value={form.delayMin}
                    onChange={(event) => {
                      setErrors(undefined);
                      setForm((prev) => ({ ...prev, delayMin: event.target.value }));
                      setDirty(true);
                    }}
                    aria-invalid={Boolean(errors?.delayMin)}
                    aria-describedby={errors?.delayMin ? 'sequence-delay-min-error' : undefined}
                    disabled={submitting || loading}
                  />
                  {errors?.delayMin && <div id="sequence-delay-min-error" className="field-error" role="alert">{errors.delayMin}</div>}
                </div>
                <div className="field">
                  <label htmlFor="sequence-delay-max">Maximum Delay (ms) *</label>
                  <input
                    id="sequence-delay-max"
                    inputMode="numeric"
                    value={form.delayMax}
                    onChange={(event) => {
                      setErrors(undefined);
                      setForm((prev) => ({ ...prev, delayMax: event.target.value }));
                      setDirty(true);
                    }}
                    aria-invalid={Boolean(errors?.delayMax)}
                    aria-describedby={errors?.delayMax ? 'sequence-delay-max-error' : undefined}
                    disabled={submitting || loading}
                  />
                  {errors?.delayMax && <div id="sequence-delay-max-error" className="field-error" role="alert">{errors.delayMax}</div>}
                </div>
              </>
            )}
          </FormSection>

          <FormSection title="Steps" description="Add commands in the order they should run and configure conditions inline." id="sequence-steps">
            <div className="field grid-3">
              <div>
                <ImageSelectorDropdown
                  id="sequence-wait-reference"
                  label="Wait image ID"
                  value={pendingWaitReferenceImageId}
                  onChange={(id) => {
                    setPendingWaitReferenceImageId(id);
                    if (!id) setPendingWaitConfidence('');
                  }}
                />
              </div>
              <div>
                <label htmlFor="sequence-wait-confidence">Wait confidence (0-1)</label>
                <input
                  id="sequence-wait-confidence"
                  inputMode="decimal"
                  value={pendingWaitConfidence}
                  onChange={(event) => setPendingWaitConfidence(event.target.value)}
                  disabled={submitting || loading || !pendingWaitReferenceImageId.trim()}
                />
              </div>
              <div>
                <label htmlFor="sequence-wait-timeout">Wait timeout (ms)</label>
                <input
                  id="sequence-wait-timeout"
                  inputMode="numeric"
                  value={pendingWaitTimeoutMs}
                  onChange={(event) => setPendingWaitTimeoutMs(event.target.value)}
                  disabled={submitting || loading}
                />
              </div>
            </div>

            <hr className="sequence-steps-separator" />

            <DndContext
              sensors={sensors}
              collisionDetection={closestCenterToCursor}
              onDragStart={handleDragStart}
              onDragOver={handleDragOver}
              onDragEnd={handleDragEnd}
              onDragCancel={handleDragCancel}
            >
              <SortableSequenceStepList
                items={stepItems}
                activeId={activeStepId}
                overId={overId}
                onDelete={(item) => {
                  setForm((prev) => ({ ...prev, steps: prev.steps.filter((s) => s.id !== item.id) }));
                  setDirty(true);
                }}
                disabled={submitting || loading}
                emptyMessage="No steps added yet."
              />
            </DndContext>

            <div className="field sequence-inline-row">
              <SearchableDropdown
                id="sequence-step-dropdown"
                label="Add command"
                options={commandOptions}
                value={pendingStepId}
                onChange={(val) => { setPendingStepId(val); setErrors(undefined); }}
                disabled={submitting || loading}
                placeholder="Select a command"
              />
              <button type="button" onClick={() => {
                if (!pendingStepId) return;
                const next = [...form.steps, createDefaultStep(pendingStepId, nextGeneratedStepId(form.steps))];
                setForm({ ...form, steps: next });
                setPendingStepId(undefined);
                setDirty(true);
              }} disabled={submitting || loading || !pendingStepId}>Add to steps</button>
            </div>
            <div className="field sequence-inline-row">
              <button
                type="button"
                onClick={() => {
                  const nextStep = createDefaultWaitStep(nextGeneratedStepId(form.steps));
                  nextStep.waitReferenceImageId = pendingWaitReferenceImageId.trim();
                  nextStep.waitConfidence = pendingWaitReferenceImageId.trim() ? pendingWaitConfidence : '';
                  nextStep.waitTimeoutMs = pendingWaitTimeoutMs || '1000';
                  setForm((prev) => ({ ...prev, steps: [...prev.steps, nextStep] }));
                  setPendingWaitReferenceImageId('');
                  setPendingWaitConfidence('');
                  setPendingWaitTimeoutMs('1000');
                  setDirty(true);
                }}
                disabled={submitting || loading || !pendingWaitTimeoutMs.trim()}
              >
                Add wait step
              </button>
              <button
                type="button"
                data-testid="add-reschedule-button"
                onClick={() => {
                  setForm((prev) => ({ ...prev, steps: [...prev.steps, createDefaultRescheduleStep(nextGeneratedStepId(prev.steps))] }));
                  setDirty(true);
                }}
                disabled={submitting || loading}
              >
                Add reschedule step
              </button>
            </div>
            <div className="field" data-testid="add-loop-buttons">
              <span>Add loop:</span>{' '}
              <button type="button" onClick={() => { setForm((prev) => ({ ...prev, steps: [...prev.steps, createLoopStep('count')] })); setDirty(true); }} disabled={submitting || loading}>Count</button>{' '}
              <button type="button" onClick={() => { setForm((prev) => ({ ...prev, steps: [...prev.steps, createLoopStep('while')] })); setDirty(true); }} disabled={submitting || loading}>While</button>{' '}
              <button type="button" onClick={() => { setForm((prev) => ({ ...prev, steps: [...prev.steps, createLoopStep('repeatUntil')] })); setDirty(true); }} disabled={submitting || loading}>Repeat‑Until</button>
            </div>
            <div className="form-hint">Steps execute in listed order; drag to reorder within the same level.</div>
          </FormSection>

          <hr className="sequence-steps-separator" />

          <FormActions submitting={submitting} onCancel={() => { if (!confirmNavigate()) return; setCreating(false); resetForm(); }}>
            {loading && <span className="form-hint">Loading…</span>}
          </FormActions>
          <FormError message={errors?.form} />
        </form>
      )}
      {editingId && (
        <section>
          <h3>Edit Sequence</h3>
          <form
            className="edit-form"
            onSubmit={async (e) => {
              e.preventDefault();
              if (!editingId) return;
              const validation = validate(form);
              if (validation) {
                setErrors(validation);
                return;
              }

              const linearPayload = {
                name: form.name.trim(),
                version: loadedVersion,
                steps: toLinearPayloadSteps(form.steps),
                interStepDelayRangeMs: form.useCustomDelayRange
                  ? { min: Number(form.delayMin), max: Number(form.delayMax) }
                  : null
              };

              if (linearPayload) {
                const linearErrors = validatePerStepConditions(linearPayload.steps);
                if (linearErrors.length > 0) {
                  setErrors({ form: linearErrors[0] });
                  return;
                }
              }

              setSubmitting(true);
              try {
                await updateSequence(
                  editingId,
                  {
                    name: linearPayload.name,
                    version: linearPayload.version,
                    steps: linearPayload.steps,
                    interStepDelayRangeMs: linearPayload.interStepDelayRangeMs
                  }
                );
                await reloadSequences();
                setEditingId(undefined);
                resetForm();
                setDirty(false);
                setTableMessage('Sequence updated successfully.');
              } catch (err: any) {
                if (isSequenceConflictError(err)) {
                  setErrors({ form: `Sequence has changed on the server (version ${err.payload.currentVersion}). Reloaded latest version.` });
                  await loadSequenceIntoForm(editingId);
                } else {
                  setErrors({ form: err?.message ?? 'Failed to update sequence' });
                }
              } finally {
                setSubmitting(false);
              }
            }}
          >
            <FormSection title="Basics" description="Primary details for the sequence." id="sequence-edit-basics">
              <div className="field">
                <label htmlFor="sequence-edit-name">Name *</label>
                <input
                  id="sequence-edit-name"
                  value={form.name}
                  onChange={(e) => { setErrors(undefined); setForm({ ...form, name: e.target.value }); setDirty(true); }}
                  aria-invalid={Boolean(errors?.name)}
                  aria-describedby={errors?.name ? 'sequence-edit-name-error' : undefined}
                  disabled={submitting || loading}
                />
                {errors?.name && <div id="sequence-edit-name-error" className="field-error" role="alert">{errors.name}</div>}
              </div>
              <div className="field">
                <label>
                  <input
                    type="checkbox"
                    checked={form.useCustomDelayRange}
                    onChange={(event) => {
                      const enabled = event.target.checked;
                      setForm((prev) => ({
                        ...prev,
                        useCustomDelayRange: enabled,
                        delayMin: enabled ? prev.delayMin : '',
                        delayMax: enabled ? prev.delayMax : ''
                      }));
                      setErrors(undefined);
                      setDirty(true);
                    }}
                    disabled={submitting || loading}
                  />
                  Use custom inter-step delay range
                </label>
                <div className="form-hint">Default runtime range is 100-300 ms when custom range is disabled.</div>
              </div>
              {form.useCustomDelayRange && (
                <>
                  <div className="field">
                    <label htmlFor="sequence-edit-delay-min">Minimum Delay (ms) *</label>
                    <input
                      id="sequence-edit-delay-min"
                      inputMode="numeric"
                      value={form.delayMin}
                      onChange={(event) => {
                        setErrors(undefined);
                        setForm((prev) => ({ ...prev, delayMin: event.target.value }));
                        setDirty(true);
                      }}
                      aria-invalid={Boolean(errors?.delayMin)}
                      aria-describedby={errors?.delayMin ? 'sequence-edit-delay-min-error' : undefined}
                      disabled={submitting || loading}
                    />
                    {errors?.delayMin && <div id="sequence-edit-delay-min-error" className="field-error" role="alert">{errors.delayMin}</div>}
                  </div>
                  <div className="field">
                    <label htmlFor="sequence-edit-delay-max">Maximum Delay (ms) *</label>
                    <input
                      id="sequence-edit-delay-max"
                      inputMode="numeric"
                      value={form.delayMax}
                      onChange={(event) => {
                        setErrors(undefined);
                        setForm((prev) => ({ ...prev, delayMax: event.target.value }));
                        setDirty(true);
                      }}
                      aria-invalid={Boolean(errors?.delayMax)}
                      aria-describedby={errors?.delayMax ? 'sequence-edit-delay-max-error' : undefined}
                      disabled={submitting || loading}
                    />
                    {errors?.delayMax && <div id="sequence-edit-delay-max-error" className="field-error" role="alert">{errors.delayMax}</div>}
                  </div>
                </>
              )}
            </FormSection>

            <FormSection title="Steps" description="Add commands in the order they should run and configure conditions inline." id="sequence-edit-steps">
              <div className="field grid-3">
                <div>
                  <ImageSelectorDropdown
                    id="sequence-edit-wait-reference"
                    label="Wait image ID"
                    value={pendingWaitReferenceImageId}
                    onChange={(id) => {
                      setPendingWaitReferenceImageId(id);
                      if (!id) setPendingWaitConfidence('');
                    }}
                  />
                </div>
                <div>
                  <label htmlFor="sequence-edit-wait-confidence">Wait confidence (0-1)</label>
                  <input
                    id="sequence-edit-wait-confidence"
                    inputMode="decimal"
                    value={pendingWaitConfidence}
                    onChange={(event) => setPendingWaitConfidence(event.target.value)}
                    disabled={submitting || loading || !pendingWaitReferenceImageId.trim()}
                  />
                </div>
                <div>
                  <label htmlFor="sequence-edit-wait-timeout">Wait timeout (ms)</label>
                  <input
                    id="sequence-edit-wait-timeout"
                    inputMode="numeric"
                    value={pendingWaitTimeoutMs}
                    onChange={(event) => setPendingWaitTimeoutMs(event.target.value)}
                    disabled={submitting || loading}
                  />
                </div>
              </div>

              <hr className="sequence-steps-separator" />

              <DndContext
                sensors={sensors}
                collisionDetection={closestCenterToCursor}
                onDragStart={handleDragStart}
                onDragOver={handleDragOver}
                onDragEnd={handleDragEnd}
                onDragCancel={handleDragCancel}
              >
                <SortableSequenceStepList
                  items={stepItems}
                  activeId={activeStepId}
                  overId={overId}
                  onDelete={(item) => {
                    setForm((prev) => ({ ...prev, steps: prev.steps.filter((s) => s.id !== item.id) }));
                    setDirty(true);
                  }}
                  disabled={submitting || loading}
                  emptyMessage="No steps added yet."
                />
              </DndContext>

              <div className="field sequence-inline-row">
                <SearchableDropdown
                  id="sequence-edit-step-dropdown"
                  label="Add command"
                  options={commandOptions}
                  value={pendingStepId}
                  onChange={(val) => { setPendingStepId(val); setErrors(undefined); }}
                  disabled={submitting || loading}
                  placeholder="Select a command"
                />
                <button type="button" onClick={() => {
                  if (!pendingStepId) return;
                  const next = [...form.steps, createDefaultStep(pendingStepId, nextGeneratedStepId(form.steps))];
                  setForm({ ...form, steps: next });
                  setPendingStepId(undefined);
                  setDirty(true);
                }} disabled={submitting || loading || !pendingStepId}>Add to steps</button>
              </div>
              <div className="field sequence-inline-row">
                <button
                  type="button"
                  onClick={() => {
                    const nextStep = createDefaultWaitStep(nextGeneratedStepId(form.steps));
                    nextStep.waitReferenceImageId = pendingWaitReferenceImageId.trim();
                    nextStep.waitConfidence = pendingWaitReferenceImageId.trim() ? pendingWaitConfidence : '';
                    nextStep.waitTimeoutMs = pendingWaitTimeoutMs || '1000';
                    setForm((prev) => ({ ...prev, steps: [...prev.steps, nextStep] }));
                    setPendingWaitReferenceImageId('');
                    setPendingWaitConfidence('');
                    setPendingWaitTimeoutMs('1000');
                    setDirty(true);
                  }}
                  disabled={submitting || loading || !pendingWaitTimeoutMs.trim()}
                >
                  Add wait step
                </button>
                <button
                  type="button"
                  data-testid="edit-add-reschedule-button"
                  onClick={() => {
                    setForm((prev) => ({ ...prev, steps: [...prev.steps, createDefaultRescheduleStep(nextGeneratedStepId(prev.steps))] }));
                    setDirty(true);
                  }}
                  disabled={submitting || loading}
                >
                  Add reschedule step
                </button>
              </div>
              <div className="field" data-testid="edit-add-loop-buttons">
                <span>Add loop:</span>{' '}
                <button type="button" onClick={() => { setForm((prev) => ({ ...prev, steps: [...prev.steps, createLoopStep('count')] })); setDirty(true); }} disabled={submitting || loading}>Count</button>{' '}
                <button type="button" onClick={() => { setForm((prev) => ({ ...prev, steps: [...prev.steps, createLoopStep('while')] })); setDirty(true); }} disabled={submitting || loading}>While</button>{' '}
                <button type="button" onClick={() => { setForm((prev) => ({ ...prev, steps: [...prev.steps, createLoopStep('repeatUntil')] })); setDirty(true); }} disabled={submitting || loading}>Repeat‑Until</button>
              </div>
              <div className="form-hint">Steps execute in listed order; drag to reorder within the same level.</div>
            </FormSection>

            <hr className="sequence-steps-separator" />

            <FormActions
              submitting={submitting}
              onCancel={() => {
                if (!confirmNavigate()) return;
                setEditingId(undefined);
                resetForm();
              }}
            >
              {loading && <span className="form-hint">Loading…</span>}
              <button type="button" className="btn btn-danger" onClick={() => setDeleteOpen(true)} disabled={submitting}>Delete</button>
            </FormActions>
            <FormError message={errors?.form} />
          </form>
        </section>
      )}
      <ConfirmDeleteModal
        open={deleteOpen}
        itemName={form.name}
        message={deleteMessage}
        references={deleteReferences}
        onCancel={() => setDeleteOpen(false)}
        onConfirm={async () => {
          if (!editingId) return;
          try {
            await deleteSequence(editingId);
            setDeleteMessage(undefined);
            setDeleteReferences(undefined);
            setDeleteOpen(false);
            setEditingId(undefined);
            setDirty(false);
            resetForm();
            await reloadSequences();
            setTableMessage('Sequence deleted successfully.');
          } catch (err: any) {
            if (err instanceof ApiError && err.status === 409) {
              setDeleteMessage(err.message || 'Cannot delete: sequence is referenced. Unlink or migrate before deleting.');
              setDeleteReferences(err.references || undefined);
              setDeleteOpen(true);
            } else {
              setDeleteOpen(false);
              setErrors({ form: err?.message ?? 'Failed to delete sequence' });
            }
          }
        }}
      />
    </section>
  );
};

