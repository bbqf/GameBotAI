import type { SequenceCommandReference, SequenceStepCondition } from './sequenceFlow';

/** Discriminated union for all step types in the sequence form editor. */
export type StepEntry =
  | ActionStepEntry
  | LoopStepEntry
  | BreakStepEntry;

export type ActionStepEntry = {
  type: 'Action';
  id: string;
  stepId: string;
  commandId: string;
  commandReference?: SequenceCommandReference;
  conditionType: 'none' | 'imageVisible' | 'commandOutcome';
  imageId: string;
  minSimilarity: string;
  outcomeStepRef: string;
  expectedState: 'success' | 'failed' | 'skipped';
};

export type LoopStepEntry = {
  type: 'Loop';
  id: string;
  stepId: string;
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
  breakCondition?: SequenceStepCondition;
};

/** Metadata attached to each draggable step via useSortable data prop. */
export type StepDragData = {
  scopeId: string;
  type: 'step';
};
