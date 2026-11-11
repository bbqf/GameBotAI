// Passkey helpers (feature 187, research R2). They call navigator.credentials directly and add no npm package.
// The service sends binary values as base64url text. The browser needs ArrayBuffer values.

export const toBase64Url = (buffer: ArrayBuffer): string => {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.length; i += 1) binary += String.fromCharCode(bytes[i]);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
};

export const fromBase64Url = (text: string): ArrayBuffer => {
  const padded = text.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (text.length % 4)) % 4);
  const binary = atob(padded);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
};

/** True when the browser can make and use passkeys. */
export const isPasskeySupported = (): boolean =>
  typeof window !== 'undefined'
  && typeof window.PublicKeyCredential !== 'undefined'
  && typeof navigator !== 'undefined'
  && !!navigator.credentials;

type Descriptor = { id: string; type: string; transports?: string[] };

const toDescriptors = (list: Descriptor[] | undefined) =>
  (list ?? []).map((d) => ({ ...d, id: fromBase64Url(d.id) }) as unknown as PublicKeyCredentialDescriptor);

/** The WebAuthn options as the service sends them (JSON). */
export type PublicKeyOptionsJson = Record<string, unknown> & { challenge: string };

export type RegistrationCredentialJson = {
  id: string;
  rawId: string;
  type: string;
  response: { attestationObject: string; clientDataJSON: string };
};

export type AssertionCredentialJson = {
  id: string;
  rawId: string;
  type: string;
  response: { authenticatorData: string; clientDataJSON: string; signature: string; userHandle: string | null };
};

const isCancel = (error: unknown): boolean =>
  typeof error === 'object' && error !== null && (error as { name?: string }).name === 'NotAllowedError';

/** Makes a passkey. Gives null when the owner cancels the system dialog. */
export const createPasskey = async (options: PublicKeyOptionsJson): Promise<RegistrationCredentialJson | null> => {
  const source = options as Record<string, any>;
  const publicKey = {
    ...source,
    challenge: fromBase64Url(options.challenge),
    user: { ...source.user, id: fromBase64Url(source.user.id) },
    excludeCredentials: toDescriptors(source.excludeCredentials)
  } as unknown as PublicKeyCredentialCreationOptions;
  try {
    const credential = (await navigator.credentials.create({ publicKey })) as PublicKeyCredential | null;
    if (!credential) return null;
    const response = credential.response as AuthenticatorAttestationResponse;
    return {
      id: credential.id,
      rawId: toBase64Url(credential.rawId),
      type: credential.type,
      response: {
        attestationObject: toBase64Url(response.attestationObject),
        clientDataJSON: toBase64Url(response.clientDataJSON)
      }
    };
  } catch (error) {
    if (isCancel(error)) return null;
    throw error;
  }
};

/** Uses a passkey to sign in. Gives null when the owner cancels the system dialog. */
export const getPasskey = async (options: PublicKeyOptionsJson): Promise<AssertionCredentialJson | null> => {
  const source = options as Record<string, any>;
  const publicKey = {
    ...source,
    challenge: fromBase64Url(options.challenge),
    allowCredentials: toDescriptors(source.allowCredentials)
  } as unknown as PublicKeyCredentialRequestOptions;
  try {
    const credential = (await navigator.credentials.get({ publicKey })) as PublicKeyCredential | null;
    if (!credential) return null;
    const response = credential.response as AuthenticatorAssertionResponse;
    return {
      id: credential.id,
      rawId: toBase64Url(credential.rawId),
      type: credential.type,
      response: {
        authenticatorData: toBase64Url(response.authenticatorData),
        clientDataJSON: toBase64Url(response.clientDataJSON),
        signature: toBase64Url(response.signature),
        userHandle: response.userHandle ? toBase64Url(response.userHandle) : null
      }
    };
  } catch (error) {
    if (isCancel(error)) return null;
    throw error;
  }
};
