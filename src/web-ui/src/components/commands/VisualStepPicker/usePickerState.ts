import { useCallback, useReducer, Dispatch } from 'react';
import { ApiError } from '../../../lib/api';
import { fetchEmulatorScreenshot, detectAll } from '../../../services/images';
import type { PickerState, PickerStatus, RecordedStep, ImageMatchResult } from '../../../types/picker';

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
};

type Action =
  | { type: 'LOAD_START' }
  | { type: 'LOAD_SUCCESS'; captureId: string; screenshotUrl: string; naturalWidth: number; naturalHeight: number; matches: ImageMatchResult[] }
  | { type: 'LOAD_ERROR'; message: string; keepScreenshot: boolean }
  | { type: 'ADD_STEP'; step: RecordedStep }
  | { type: 'REMOVE_STEP'; id: string }
  | { type: 'REORDER_STEPS'; steps: RecordedStep[] };

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

  return { state, openPicker, recapture, recordTap, recordKey, recordSwipe, removeStep, reorderSteps };
}
