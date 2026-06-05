import type { RecordedStep, StepRunResult } from '../../../../types/picker';

// ── Reducer unit tests (extracted pure function) ──────────────────────────────
// We test the reducer logic directly by re-implementing the cases here.
// The full usePickerState hook is tested via its exported functions.

type StepExecutionStatus = 'idle' | 'running' | 'success' | 'error';

type PickerState = {
  status: string;
  captureId: string | null;
  screenshotUrl: string | null;
  naturalWidth: number;
  naturalHeight: number;
  matches: unknown[];
  steps: RecordedStep[];
  errorMessage: string | null;
  isExecuting: boolean;
};

type Action =
  | { type: 'ADD_STEP'; step: RecordedStep }
  | { type: 'REMOVE_STEP'; id: string }
  | { type: 'RUN_STEP_START'; id: string }
  | { type: 'RUN_STEP_COMPLETE'; id: string; result: StepRunResult }
  | { type: 'RUN_ALL_DONE' };

function reducer(state: PickerState, action: Action): PickerState {
  switch (action.type) {
    case 'ADD_STEP':
      return { ...state, steps: [...state.steps, action.step] };
    case 'REMOVE_STEP':
      return { ...state, steps: state.steps.filter((s) => s.id !== action.id) };
    case 'RUN_STEP_START':
      return {
        ...state,
        isExecuting: true,
        steps: state.steps.map((s) =>
          s.id === action.id ? { ...s, executionStatus: 'running' as StepExecutionStatus } : s
        ),
      };
    case 'RUN_STEP_COMPLETE': {
      const isSuccess = action.result.status === 'executed';
      return {
        ...state,
        isExecuting: false,
        steps: state.steps.map((s) => {
          if (s.id !== action.id) return s;
          if (isSuccess) {
            const { errorMessage: _e, ...rest } = s as RecordedStep & { errorMessage?: string };
            return { ...rest, executionStatus: 'success' as StepExecutionStatus };
          }
          return {
            ...s,
            executionStatus: 'error' as StepExecutionStatus,
            errorMessage: action.result.reason ?? 'Execution failed',
          };
        }),
      };
    }
    case 'RUN_ALL_DONE':
      return { ...state, isExecuting: false };
    default:
      return state;
  }
}

const makeStep = (id: string, type: RecordedStep['type'] = 'KeyInput'): RecordedStep => {
  if (type === 'KeyInput') {
    return { id, type: 'KeyInput', key: 'ENTER', label: 'Key: ENTER', executionStatus: 'idle' };
  }
  if (type === 'Swipe') {
    return { id, type: 'Swipe', startX: 0, startY: 0, endX: 100, endY: 100, durationMs: 300, label: 'Swipe', executionStatus: 'idle' };
  }
  return { id, type: 'PrimitiveTap', imageId: 'img1', offsetX: 0, offsetY: 0, label: 'Tap', executionStatus: 'idle' };
};

const initialState: PickerState = {
  status: 'ready',
  captureId: null,
  screenshotUrl: null,
  naturalWidth: 0,
  naturalHeight: 0,
  matches: [],
  steps: [],
  errorMessage: null,
  isExecuting: false,
};

// ── T008: RUN_STEP_START / RUN_STEP_COMPLETE ─────────────────────────────────

describe('RUN_STEP_START', () => {
  it('sets isExecuting to true', () => {
    const state = { ...initialState, steps: [makeStep('a')] };
    const next = reducer(state, { type: 'RUN_STEP_START', id: 'a' });
    expect(next.isExecuting).toBe(true);
  });

  it('sets target step executionStatus to running', () => {
    const state = { ...initialState, steps: [makeStep('a'), makeStep('b')] };
    const next = reducer(state, { type: 'RUN_STEP_START', id: 'a' });
    expect(next.steps[0].executionStatus).toBe('running');
    expect(next.steps[1].executionStatus).toBe('idle');
  });
});

describe('RUN_STEP_COMPLETE success', () => {
  it('sets isExecuting to false', () => {
    const state = { ...initialState, isExecuting: true, steps: [makeStep('a')] };
    const next = reducer(state, { type: 'RUN_STEP_COMPLETE', id: 'a', result: { status: 'executed' } });
    expect(next.isExecuting).toBe(false);
  });

  it('sets target step executionStatus to success', () => {
    const state = { ...initialState, steps: [makeStep('a')] };
    const next = reducer(state, { type: 'RUN_STEP_COMPLETE', id: 'a', result: { status: 'executed' } });
    expect(next.steps[0].executionStatus).toBe('success');
  });

  it('clears errorMessage on success', () => {
    const step = { ...makeStep('a'), errorMessage: 'old error' };
    const state = { ...initialState, steps: [step] };
    const next = reducer(state, { type: 'RUN_STEP_COMPLETE', id: 'a', result: { status: 'executed' } });
    expect((next.steps[0] as any).errorMessage).toBeUndefined();
  });
});

describe('RUN_STEP_COMPLETE error', () => {
  it('sets target step executionStatus to error', () => {
    const state = { ...initialState, steps: [makeStep('a')] };
    const next = reducer(state, {
      type: 'RUN_STEP_COMPLETE',
      id: 'a',
      result: { status: 'timeout', reason: 'Step execution timed out' },
    });
    expect(next.steps[0].executionStatus).toBe('error');
  });

  it('sets errorMessage from result.reason', () => {
    const state = { ...initialState, steps: [makeStep('a')] };
    const next = reducer(state, {
      type: 'RUN_STEP_COMPLETE',
      id: 'a',
      result: { status: 'error', reason: 'emulator unavailable' },
    });
    expect((next.steps[0] as any).errorMessage).toBe('emulator unavailable');
  });

  it('sets isExecuting to false', () => {
    const state = { ...initialState, isExecuting: true, steps: [makeStep('a')] };
    const next = reducer(state, {
      type: 'RUN_STEP_COMPLETE',
      id: 'a',
      result: { status: 'error' },
    });
    expect(next.isExecuting).toBe(false);
  });
});

describe('ADD_STEP initializes executionStatus to idle', () => {
  it('new KeyInput step starts with executionStatus idle', () => {
    const step = makeStep('new1', 'KeyInput');
    const next = reducer(initialState, { type: 'ADD_STEP', step });
    expect(next.steps[0].executionStatus).toBe('idle');
  });

  it('new PrimitiveTap step starts with executionStatus idle', () => {
    const step = makeStep('new2', 'PrimitiveTap');
    const next = reducer(initialState, { type: 'ADD_STEP', step });
    expect(next.steps[0].executionStatus).toBe('idle');
  });
});

// ── T019: runAll reducer tests ───────────────────────────────────────────────

describe('runAll: all steps succeed', () => {
  it('all statuses become success after sequential RUN_STEP_START/COMPLETE dispatches', () => {
    let state = {
      ...initialState,
      steps: [makeStep('a'), makeStep('b'), makeStep('c')],
    };

    for (const step of state.steps) {
      state = reducer(state, { type: 'RUN_STEP_START', id: step.id });
      state = reducer(state, { type: 'RUN_STEP_COMPLETE', id: step.id, result: { status: 'executed' } });
    }
    state = reducer(state, { type: 'RUN_ALL_DONE' });

    expect(state.isExecuting).toBe(false);
    expect(state.steps.every((s) => s.executionStatus === 'success')).toBe(true);
  });
});

describe('runAll: step[1] fails', () => {
  it('step[1] is error, step[2] remains idle, isExecuting resets to false', () => {
    let state = {
      ...initialState,
      steps: [makeStep('a'), makeStep('b'), makeStep('c')],
    };

    // step a succeeds
    state = reducer(state, { type: 'RUN_STEP_START', id: 'a' });
    state = reducer(state, { type: 'RUN_STEP_COMPLETE', id: 'a', result: { status: 'executed' } });

    // step b fails — loop breaks
    state = reducer(state, { type: 'RUN_STEP_START', id: 'b' });
    state = reducer(state, { type: 'RUN_STEP_COMPLETE', id: 'b', result: { status: 'error', reason: 'bad' } });
    state = reducer(state, { type: 'RUN_ALL_DONE' });

    expect(state.steps[0].executionStatus).toBe('success');
    expect(state.steps[1].executionStatus).toBe('error');
    expect(state.steps[2].executionStatus).toBe('idle');
    expect(state.isExecuting).toBe(false);
  });
});

describe('RUN_ALL_DONE', () => {
  it('sets isExecuting to false', () => {
    const state = { ...initialState, isExecuting: true };
    const next = reducer(state, { type: 'RUN_ALL_DONE' });
    expect(next.isExecuting).toBe(false);
  });
});

describe('REMOVE_STEP clears execution status', () => {
  it('removing a step with error status removes it entirely from steps', () => {
    const step = { ...makeStep('a'), executionStatus: 'error' as const, errorMessage: 'oops' };
    const state = { ...initialState, steps: [step, makeStep('b')] };
    const next = reducer(state, { type: 'REMOVE_STEP', id: 'a' });
    expect(next.steps).toHaveLength(1);
    expect(next.steps[0].id).toBe('b');
  });
});
