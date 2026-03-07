export type FlowStepType = 'action' | 'command' | 'condition' | 'terminal';
export type BranchType = 'next' | 'true' | 'false';
export type ConditionNodeType = 'and' | 'or' | 'not' | 'operand';
export type OperandType = 'command-outcome' | 'image-detection';
export type DeepLinkResolutionStatus = 'resolved' | 'step_missing' | 'sequence_missing';

export type ConditionOperand = {
  operandType: OperandType;
  targetRef: string;
  expectedState: string;
  threshold?: number | null;
};

export type ConditionExpression = {
  nodeType: ConditionNodeType;
  children?: ConditionExpression[];
  operand?: ConditionOperand;
};

export type FlowStep = {
  stepId: string;
  label: string;
  stepType: FlowStepType;
  payloadRef?: string | null;
  iterationLimit?: number | null;
  condition?: ConditionExpression;
};

export type BranchLink = {
  linkId: string;
  sourceStepId: string;
  targetStepId: string;
  branchType: BranchType;
};

export type SequenceFlow = {
  sequenceId: string;
  name: string;
  version: number;
  entryStepId: string;
  steps: FlowStep[];
  links: BranchLink[];
};

export type SequenceFlowUpsertRequest = {
  name: string;
  version: number;
  entryStepId: string;
  steps: FlowStep[];
  links: BranchLink[];
};

export type SequenceSaveConflict = {
  sequenceId: string;
  currentVersion: number;
  message: string;
};

export type AuthoringDeepLink = {
  sequenceId: string;
  stepId: string;
  sequenceLabel: string;
  stepLabel: string;
  resolutionStatus: DeepLinkResolutionStatus;
  fallbackRoute?: string | null;
};

export type ConditionEvaluationTrace = {
  finalResult: boolean;
  selectedBranch: 'true' | 'false' | 'none';
  failureReason?: string | null;
  operandResults?: Array<Record<string, unknown>>;
  operatorSteps?: Array<Record<string, unknown>>;
};

export type PerStepConditionType = 'imageVisible' | 'commandOutcome';

export type ImageVisibleStepCondition = {
  type: 'imageVisible';
  imageId: string;
  minSimilarity?: number | null;
};

export type CommandOutcomeStepCondition = {
  type: 'commandOutcome';
  stepRef: string;
  expectedState: 'success' | 'failed' | 'skipped';
};

export type SequenceStepCondition = ImageVisibleStepCondition | CommandOutcomeStepCondition;

export type SequenceActionPayload = {
  type: string;
  parameters: Record<string, unknown>;
};

export type SequenceLinearStep = {
  stepId: string;
  label?: string;
  action: SequenceActionPayload;
  condition?: SequenceStepCondition | null;
};

export type SequenceLinearUpsertRequest = {
  name: string;
  version: number;
  steps: SequenceLinearStep[];
};
