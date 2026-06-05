import { useCallback, useReducer, Dispatch } from 'react';
import { ApiError } from '../../../lib/api';
import { fetchEmulatorScreenshot, detectAll } from '../../../services/images';
import { executeStep } from '../../../services/commands';
import type { PickerState, PickerStatus, RecordedStep, ImageMatchResult, StepRunResult } from '../../../types/picker';
import { toCommandStepDto } from './stepUtils';

const makeId = () =>
  typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2);

const initialState: PickerState = {
  status: 'loading',
  captureId: null,
  screenshotUrl: null,
  naturalWidth: 0,
  naturalHeight: 0,
  matches: [],
  steps: [],
  errorMessage: null,
  isExecuting: false,
};

type Action =
  | { type: 'LOAD_START' }
  | { type: 'LOAD_SUCCESS'; captureId: string; screenshotUrl: string; naturalWidth: number; naturalHeight: number; matches: ImageMatchResult[] }
  | { type: 'LOAD_ERROR'; message: string; keepScreenshot: boolean }
  | { type: 'ADD_STEP'; step: RecordedStep }
  | { type: 'REMOVE_STEP'; id: string }
  | { type: 'REORDER_STEPS'; steps: RecordedStep[] }
  | { type: 'RUN_STEP_START'; id: string }
  | { type: 'RUN_STEP_COMPLETE'; id: string; result: StepRunResult }
  | { type: 'RUN_ALL_DONE' };

function reducer(state: PickerState, action: Action): PickerState {
  switch (action.type) {
    case 'LOAD_START':
      return { ...state, status: 'loading' as PickerStatus, errorMessage: null };
    case 'LOAD_SUCCESS': {
      if (state.screenshotUrl && state.screenshotUrl !== action.screenshotUrl) {
        URL.revokeObjectURL(state.screenshotUrl);
      }
      return {
        ...state,
        status: 'ready',
        captureId: action.captureId,
        screenshotUrl: action.screenshotUrl,
        naturalWidth: action.naturalWidth,
        naturalHeight: action.naturalHeight,
        matches: action.matches,
        errorMessage: null,
      };
    }
    case 'LOAD_ERROR':
      return {
        ...state,
        status: 'error',
        errorMessage: action.message,
        // On re-capture failure keep previous screenshot; on open failure clear it
        screenshotUrl: action.keepScreenshot ? state.screenshotUrl : null,
        matches: action.keepScreenshot ? state.matches : [],
      };
    case 'ADD_STEP':
      return { ...state, steps: [...state.steps, action.step] };
    case 'REMOVE_STEP':
      return { ...state, steps: state.steps.filter((s) => s.id !== action.id) };
    case 'REORDER_STEPS':
      return { ...state, steps: action.steps };
    case 'RUN_STEP_START':
      return {
        ...state,
        isExecuting: true,
        steps: state.steps.map((s) =>
          s.id === action.id ? { ...s, executionStatus: 'running' as const } : s
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
            // eslint-disable-next-line @typescript-eslint/no-unused-vars
            const { errorMessage: _e, ...rest } = s as RecordedStep & { errorMessage?: string };
            return { ...rest, executionStatus: 'success' as const };
          }
          return {
            ...s,
            executionStatus: 'error' as const,
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

async function loadCapture(dispatch: Dispatch<Action>, isRecapture: boolean) {
  dispatch({ type: 'LOAD_START' });
  try {
    const screenshot = await fetchEmulatorScreenshot();
    const url = URL.createObjectURL(screenshot.blob);
    const img = new Image();
    await new Promise<void>((resolve) => {
      img.onload = () => resolve();
      img.onerror = () => resolve();
      img.src = url;
    });
    const naturalWidth = img.naturalWidth;
    const naturalHeight = img.naturalHeight;

    let matches: ImageMatchResult[] = [];
    try {
      const raw = await detectAll(screenshot.captureId);
      matches = raw.map((m) => ({
        imageId: m.imageId,
        imageName: m.imageName,
        x: m.x,
        y: m.y,
        width: m.width,
        height: m.height,
        confidence: m.confidence,
      }));
    } catch {
      // matching failure is non-fatal — show screenshot with no overlays
    }

    dispatch({
      type: 'LOAD_SUCCESS',
      captureId: screenshot.captureId,
      screenshotUrl: url,
      naturalWidth,
      naturalHeight,
      matches,
    });
  } catch (e: unknown) {
    let message = 'Failed to capture screenshot';
    if (e instanceof ApiError) {
      if (e.status === 503 || e.payload?.error === 'emulator_unavailable') {
        message = 'Emulator not connected. Make sure the emulator is running and try again.';
      } else {
        message = e.message;
      }
    } else if (e instanceof Error) {
      message = e.message;
    }
    dispatch({ type: 'LOAD_ERROR', message, keepScreenshot: isRecapture });
  }
}

export function calcGestureDisplacement(
  start: { x: number; y: number },
  end: { x: number; y: number }
): number {
  return Math.sqrt((end.x - start.x) ** 2 + (end.y - start.y) ** 2);
}

export type UsePickerState = {
  state: PickerState;
  openPicker: () => void;
  recapture: () => void;
  recordTap: (naturalX: number, naturalY: number) => void;
  recordKey: (adbKey: string) => void;
  recordSwipe: (startX: number, startY: number, endX: number, endY: number, durationMs: number) => void;
  removeStep: (id: string) => void;
  reorderSteps: (steps: RecordedStep[]) => void;
  runStep: (id: string) => Promise<void>;
  runAll: () => Promise<void>;
};

export function usePickerState(): UsePickerState {
  const [state, dispatch] = useReducer(reducer, initialState);

  const openPicker = useCallback(() => {
    void loadCapture(dispatch, false);
  }, []);

  const recapture = useCallback(() => {
    void loadCapture(dispatch, true);
  }, []);

  const recordTap = useCallback(
    (naturalX: number, naturalY: number) => {
      if (state.status !== 'ready') return;
      const hits = state.matches.filter(
        (m) => naturalX >= m.x && naturalX <= m.x + m.width && naturalY >= m.y && naturalY <= m.y + m.height
      );
      if (hits.length === 0) return;
      const best = hits.reduce((a, b) => (b.confidence > a.confidence ? b : a));
      const centerX = best.x + best.width / 2;
      const centerY = best.y + best.height / 2;
      const offsetX = Math.round(naturalX - centerX);
      const offsetY = Math.round(naturalY - centerY);
      const sign = (n: number) => (n >= 0 ? '+' : '');
      const label = `${best.imageName} (${sign(offsetX)}${offsetX}, ${sign(offsetY)}${offsetY})`;
      const step: import('../../../types/picker').RecordedPrimitiveTapStep = {
        id: makeId(),
        type: 'PrimitiveTap',
        imageId: best.imageId,
        offsetX,
        offsetY,
        label,
        executionStatus: 'idle',
      };
      dispatch({ type: 'ADD_STEP', step });
    },
    [state.status, state.matches]
  );

  const recordKey = useCallback(
    (adbKey: string) => {
      if (state.status !== 'ready') return;
      const step: import('../../../types/picker').RecordedKeyInputStep = {
        id: makeId(),
        type: 'KeyInput',
        key: adbKey,
        label: `Key: ${adbKey}`,
        executionStatus: 'idle',
      };
      dispatch({ type: 'ADD_STEP', step });
    },
    [state.status]
  );

  const recordSwipe = useCallback(
    (startX: number, startY: number, endX: number, endY: number, durationMs: number) => {
      if (state.status !== 'ready') return;
      const step: import('../../../types/picker').RecordedSwipeStep = {
        id: makeId(),
        type: 'Swipe',
        startX,
        startY,
        endX,
        endY,
        durationMs,
        label: `Swipe (${startX},${startY})→(${endX},${endY}) ${durationMs}ms`,
        executionStatus: 'idle',
      };
      dispatch({ type: 'ADD_STEP', step });
    },
    [state.status]
  );

  const removeStep = useCallback((id: string) => {
    dispatch({ type: 'REMOVE_STEP', id });
  }, []);

  const reorderSteps = useCallback((steps: RecordedStep[]) => {
    dispatch({ type: 'REORDER_STEPS', steps });
  }, []);

  const runStep = useCallback(
    async (id: string): Promise<void> => {
      if (state.isExecuting) return;
      const step = state.steps.find((s) => s.id === id);
      if (!step) return;
      dispatch({ type: 'RUN_STEP_START', id });
      try {
        const dto = toCommandStepDto(step);
        const response = await executeStep(dto);
        const outcome = response.stepOutcomes?.[0];
        const result: StepRunResult = {
          status: outcome?.status ?? 'error',
          reason: outcome?.reason,
        };
        dispatch({ type: 'RUN_STEP_COMPLETE', id, result });
      } catch (e: unknown) {
        const reason = e instanceof Error ? e.message : 'Execution failed';
        dispatch({ type: 'RUN_STEP_COMPLETE', id, result: { status: 'error', reason } });
      }
    },
    [state.isExecuting, state.steps]
  );

  const runAll = useCallback(async (): Promise<void> => {
    if (state.isExecuting) return;
    for (const step of state.steps) {
      dispatch({ type: 'RUN_STEP_START', id: step.id });
      try {
        const dto = toCommandStepDto(step);
        const response = await executeStep(dto);
        const outcome = response.stepOutcomes?.[0];
        const result: StepRunResult = {
          status: outcome?.status ?? 'error',
          reason: outcome?.reason,
        };
        dispatch({ type: 'RUN_STEP_COMPLETE', id: step.id, result });
        if (result.status !== 'executed') break;
      } catch (e: unknown) {
        const reason = e instanceof Error ? e.message : 'Execution failed';
        dispatch({ type: 'RUN_STEP_COMPLETE', id: step.id, result: { status: 'error', reason } });
        break;
      }
    }
    dispatch({ type: 'RUN_ALL_DONE' });
  }, [state.isExecuting, state.steps]);

  return { state, openPicker, recapture, recordTap, recordKey, recordSwipe, removeStep, reorderSteps, runStep, runAll };
}
