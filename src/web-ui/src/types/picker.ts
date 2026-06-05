export type StepExecutionStatus = 'idle' | 'running' | 'success' | 'error';

export type StepRunResult = {
  status: 'executed' | 'timeout' | 'error' | string;
  reason?: string;
};

export type RecordedPrimitiveTapStep = {
  id: string;
  type: 'PrimitiveTap';
  imageId: string;
  offsetX: number;
  offsetY: number;
  label: string;
  executionStatus: StepExecutionStatus;
  errorMessage?: string;
};

export type RecordedKeyInputStep = {
  id: string;
  type: 'KeyInput';
  key: string;
  label: string;
  executionStatus: StepExecutionStatus;
  errorMessage?: string;
};

export type RecordedSwipeStep = {
  id: string;
  type: 'Swipe';
  startX: number;
  startY: number;
  endX: number;
  endY: number;
  durationMs: number;
  label: string;
  executionStatus: StepExecutionStatus;
  errorMessage?: string;
};

export type RecordedStep = RecordedPrimitiveTapStep | RecordedKeyInputStep | RecordedSwipeStep;

export type ImageMatchResult = {
  imageId: string;
  imageName: string;
  x: number;
  y: number;
  width: number;
  height: number;
  confidence: number;
};

export type PickerStatus = 'loading' | 'ready' | 'error';

export type PickerState = {
  status: PickerStatus;
  captureId: string | null;
  screenshotUrl: string | null;
  naturalWidth: number;
  naturalHeight: number;
  matches: ImageMatchResult[];
  steps: RecordedStep[];
  errorMessage: string | null;
  isExecuting: boolean;
};
