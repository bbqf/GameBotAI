// Calls of the sign-in API (feature 187, contracts/auth-api.md C-1 to C-10).
import { apiDelete, apiGet, apiPatch, apiPost } from '../lib/api';
import {
  createPasskey,
  getPasskey,
  type PublicKeyOptionsJson
} from '../lib/passkey';

export type AuthSource = 'localMachine' | 'homeNetwork' | 'internet';

export type AuthStatus = {
  signInEnabled: boolean;
  signedIn: boolean;
  passkeysSupportedHere: boolean;
  source: AuthSource;
  publicOrigin: string | null;
  tokenAccessHere: boolean;
};

export type PasskeyItem = {
  id: string;
  name: string;
  createdUtc: string;
  lastUsedUtc: string | null;
  current: boolean;
};

type CeremonyResponse = { ceremonyId: string; publicKey: PublicKeyOptionsJson };

/** C-1. Gives null when the status is not available. */
export const getAuthStatus = async (): Promise<{ status: AuthStatus | null; httpStatus: number; code?: string }> => {
  const res = await apiGet('/api/auth/status');
  if (!res.ok) {
    let code: string | undefined;
    try {
      const body = await res.json();
      code = body?.error?.code;
    } catch { /* no body */ }
    return { status: null, httpStatus: res.status, code };
  }
  return { status: (await res.json()) as AuthStatus, httpStatus: res.status };
};

/** C-2 and C-3. Gives true after a correct sign-in, false after a refusal or a cancel. Never throws for a refusal. */
export const signInWithPasskey = async (): Promise<'signedIn' | 'failed' | 'cancelled'> => {
  const optionsRes = await apiPost('/api/auth/sign-in/options', undefined);
  if (!optionsRes.ok) return 'failed';
  const { ceremonyId, publicKey } = (await optionsRes.json()) as CeremonyResponse;
  const credential = await getPasskey(publicKey);
  if (!credential) return 'cancelled';
  const completeRes = await apiPost('/api/auth/sign-in/complete', { ceremonyId, credential });
  return completeRes.status === 204 ? 'signedIn' : 'failed';
};

/** C-4 and C-5. Registers a passkey with a registration code. */
export const registerPasskey = async (code: string, name: string): Promise<'registered' | 'failed' | 'cancelled'> => {
  const optionsRes = await apiPost('/api/auth/register/options', { code });
  if (!optionsRes.ok) return 'failed';
  const { ceremonyId, publicKey } = (await optionsRes.json()) as CeremonyResponse;
  const credential = await createPasskey(publicKey);
  if (!credential) return 'cancelled';
  const completeRes = await apiPost('/api/auth/register/complete', {
    ceremonyId,
    name: name.trim().length > 0 ? name.trim() : undefined,
    credential
  });
  return completeRes.status === 204 ? 'registered' : 'failed';
};

/** C-6. */
export const signOut = async (): Promise<void> => {
  await apiPost('/api/auth/sign-out', undefined);
};

/** C-7. */
export const endAllSessions = async (): Promise<void> => {
  await apiPost('/api/auth/sessions/end-all', undefined);
};

/** C-8. */
export const listPasskeys = async (): Promise<PasskeyItem[]> => {
  const res = await apiGet('/api/auth/passkeys');
  if (!res.ok) throw new Error('The list of devices did not load.');
  return ((await res.json()) as { passkeys: PasskeyItem[] }).passkeys;
};

/** C-9. */
export const renamePasskey = async (id: string, name: string): Promise<PasskeyItem> => {
  const res = await apiPatch(`/api/auth/passkeys/${encodeURIComponent(id)}`, { name });
  if (!res.ok) throw new Error('The name did not change.');
  return (await res.json()) as PasskeyItem;
};

/** C-10. */
export const removePasskey = async (id: string): Promise<void> => {
  const res = await apiDelete(`/api/auth/passkeys/${encodeURIComponent(id)}`);
  if (!res.ok && res.status !== 204) throw new Error('The device did not go.');
};
