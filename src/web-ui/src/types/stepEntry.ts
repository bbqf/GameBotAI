import type { SequenceCommandReference, SequencePrimitiveActionPayload, SequenceStepCondition } from './sequenceFlow';

/** Discriminated union for all step types in the sequence form editor. */
export type StepEntry =
  | ActionStepEntry
  | LoopStepEntry
  | BreakStepEntry
  | IfStepEntry;

export type ActionStepEntry = {
  type: 'Action';
  id: string;
  stepId: string;
  /** Authored display name; preserved on save so opening a sequence here does not strip labels. */
  label?: string;
  commandId: string;
  commandReference?: SequenceCommandReference;
  /**
   * Set when the step dispatches an action inline (a tap, a self-reschedule) instead of invoking a
   * command. The payload is carried through untouched so a body step the editor has no rich form for
   * still round-trips, and its scalar slots stay parametrizable.
   */
  primitiveAction?: SequencePrimitiveActionPayload;
  conditionType: 'none' | 'imageVisible' | 'commandOutcome';
  conditionNegate?: boolean;
  imageId: string;
  minSimilarity: string;
  outcomeStepRef: string;
  expectedState: 'success' | 'failed' | 'skipped';
};

export type LoopStepEntry = {
  type: 'Loop';
  id: string;
  stepId: string;
  label?: string;
  loopType: 'count' | 'while' | 'repeatUntil';
  count?: number;
  condition?: SequenceStepCondition;
  maxIterations?: number;
  body: StepEntry[];
};

export type BreakStepEntry = {
  type: 'Break';
  id: string;
  stepId: string;
  label?: string;
  breakCondition?: SequenceStepCondition;
};

export type IfStepEntry = {
  type: 'If';
  id: string;
  stepId: string;
  label?: string;
  /** Same condition shape as while/repeat-until loop conditions. */
  condition?: SequenceStepCondition;
  /** Then branch; behaves like a loop body (no nested loops or ifs). */
  body: StepEntry[];
  /** Else branch; undefined = no else, [] = else present but empty. */
  elseBody?: StepEntry[];
};

/** Metadata attached to each draggable step via useSortable data prop. */
export type StepDragData = {
  scopeId: string;
  type: 'step';
};
