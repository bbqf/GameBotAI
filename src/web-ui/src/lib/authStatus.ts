// The sign-in state of the web UI (feature 187, contracts/ui-contract.md U-1).
// It holds the answer of GET /api/auth/status and reacts to a 401 and to a 403 remote_access_refused.
import { useEffect, useState } from 'react';
import { getAuthStatus, type AuthStatus } from '../services/authApi';
import { noteRemoteRefused, subscribeRemoteRefused, subscribeUnauthorized } from './unauthorized';

export type AuthState = {
  /** True after the first status call ended. */
  loaded: boolean;
  status: AuthStatus | null;
  /** True after an API call got a 401 while the sign-in function is on. */
  signInRequired: boolean;
  /** True when the service refuses the internet (403 remote_access_refused). */
  refused: boolean;
};

export type GateView = 'loading' | 'refused' | 'content' | 'signIn' | 'otherAddress';

type Listener = (state: AuthState) => void;

let state: AuthState = { loaded: false, status: null, signInRequired: false, refused: false };
const listeners = new Set<Listener>();

const publish = (next: Partial<AuthState>) => {
  state = { ...state, ...next };
  listeners.forEach((l) => l(state));
};

export const getAuthState = (): AuthState => state;

/** Asks the service for the sign-in state and updates the signal. */
export const refreshAuthStatus = async (): Promise<AuthState> => {
  try {
    const result = await getAuthStatus();
    if (result.status) {
      publish({ loaded: true, status: result.status, signInRequired: false, refused: false });
    } else if (result.httpStatus === 403 && result.code === 'remote_access_refused') {
      noteRemoteRefused();
      publish({ loaded: true, status: null, refused: true });
    } else {
      // The status is not available. The UI works as before. The service still refuses what it must refuse.
      publish({ loaded: true, status: null, signInRequired: false });
    }
  } catch {
    publish({ loaded: true, status: null });
  }
  return state;
};

/** Gives the part that the access gate shows. */
export const deriveGateView = (s: AuthState): GateView => {
  if (!s.loaded) return 'loading';
  if (s.refused) return 'refused';
  const status = s.status;
  if (!status || !status.signInEnabled) return 'content';
  if (status.signedIn && !s.signInRequired) return 'content';
  return status.passkeysSupportedHere ? 'signIn' : 'otherAddress';
};

/** React hook for the sign-in state. */
export const useAuthStatus = (): AuthState => {
  const [value, setValue] = useState<AuthState>(state);
  useEffect(() => {
    setValue(state);
    listeners.add(setValue);
    return () => { listeners.delete(setValue); };
  }, []);
  return value;
};

// A 401 with the sign-in function on shows the sign-in page again. A 403 remote_access_refused shows the refusal text.
subscribeUnauthorized((unauthorized) => {
  if (unauthorized && state.status?.signInEnabled && !state.signInRequired) publish({ signInRequired: true });
});
subscribeRemoteRefused((refused) => {
  if (refused && !state.refused) publish({ refused: true });
});

/** For tests: puts the module in its first state. */
export const resetAuthStateForTests = (): void => {
  state = { loaded: false, status: null, signInRequired: false, refused: false };
  listeners.forEach((l) => l(state));
};
