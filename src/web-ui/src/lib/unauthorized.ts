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
