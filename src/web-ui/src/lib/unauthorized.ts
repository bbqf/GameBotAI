import { useEffect, useState } from 'react';

type Listener = (unauthorized: boolean) => void;

let unauthorized = false;
const listeners = new Set<Listener>();

const emit = () => listeners.forEach((l) => l(unauthorized));

/** Records the status of an API response. A 401 means the service needs a token (contract C-10). */
export const noteResponseStatus = (status: number | undefined): void => {
  if (status === 401 && !unauthorized) {
    unauthorized = true;
    emit();
  }
};

// A 403 with the code remote_access_refused means the service does not accept the internet (feature 187).
let remoteRefused = false;
const refusedListeners = new Set<(refused: boolean) => void>();

/** Records a 403 response. The service refuses the internet when its sign-in function is off. */
export const noteRemoteRefused = (): void => {
  if (remoteRefused) return;
  remoteRefused = true;
  refusedListeners.forEach((l) => l(true));
};

/** Reads the body of a 403 response and records it when the code is remote_access_refused. */
export const noteForbiddenResponse = async (response: { status?: number; clone?: () => { json: () => Promise<any> } } | undefined): Promise<void> => {
  if (!response || response.status !== 403 || typeof response.clone !== 'function') return;
  try {
    const body = await response.clone().json();
    if (body?.error?.code === 'remote_access_refused') noteRemoteRefused();
  } catch { /* no JSON body */ }
};

export const isRemoteRefused = (): boolean => remoteRefused;

export const subscribeRemoteRefused = (listener: (refused: boolean) => void): (() => void) => {
  refusedListeners.add(listener);
  return () => { refusedListeners.delete(listener); };
};

/** Clears the 401 flag, for example after the user sets a token. */
export const clearUnauthorized = (): void => {
  if (!unauthorized) return;
  unauthorized = false;
  emit();
};

export const isUnauthorized = (): boolean => unauthorized;

export const subscribeUnauthorized = (listener: Listener): (() => void) => {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
};

/** React hook that returns true after an API call got a 401. */
export const useUnauthorized = (): boolean => {
  const [value, setValue] = useState<boolean>(unauthorized);
  useEffect(() => {
    setValue(unauthorized);
    return subscribeUnauthorized(setValue);
  }, []);
  return value;
};
