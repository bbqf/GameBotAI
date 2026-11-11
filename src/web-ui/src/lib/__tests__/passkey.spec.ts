import { createPasskey, fromBase64Url, getPasskey, isPasskeySupported, toBase64Url } from '../passkey';

const bytes = (...values: number[]) => new Uint8Array(values).buffer;

describe('base64url helper', () => {
  it('round-trips binary values without padding', () => {
    const buffer = bytes(251, 255, 190, 0, 1, 2);
    const text = toBase64Url(buffer);
    expect(text).not.toMatch(/[+/=]/);
    expect(Array.from(new Uint8Array(fromBase64Url(text)))).toEqual([251, 255, 190, 0, 1, 2]);
  });

  it('reads text with and without padding', () => {
    expect(Array.from(new Uint8Array(fromBase64Url('AQID')))).toEqual([1, 2, 3]);
    expect(Array.from(new Uint8Array(fromBase64Url('AQI')))).toEqual([1, 2]);
  });
});

describe('passkey calls', () => {
  const original = Object.getOwnPropertyDescriptor(window.navigator, 'credentials');
  afterEach(() => {
    if (original) Object.defineProperty(window.navigator, 'credentials', original);
    else delete (window.navigator as any).credentials;
    delete (window as any).PublicKeyCredential;
  });

  const setCredentials = (api: { create?: jest.Mock; get?: jest.Mock }) => {
    Object.defineProperty(window.navigator, 'credentials', { value: api, configurable: true });
  };

  it('reports support only with the browser API', () => {
    expect(isPasskeySupported()).toBe(false);
    (window as any).PublicKeyCredential = function PublicKeyCredential() { /* stub */ };
    setCredentials({});
    expect(isPasskeySupported()).toBe(true);
  });

  it('makes a passkey and converts the answer to base64url text', async () => {
    const create = jest.fn().mockResolvedValue({
      id: 'AQID',
      rawId: bytes(1, 2, 3),
      type: 'public-key',
      response: { attestationObject: bytes(4, 5), clientDataJSON: bytes(6) }
    });
    setCredentials({ create });
    const result = await createPasskey({
      challenge: 'AQID',
      user: { id: 'BAUG', name: 'owner', displayName: 'owner' },
      excludeCredentials: [{ id: 'AQID', type: 'public-key' }]
    });
    const sent = create.mock.calls[0][0].publicKey;
    expect(new Uint8Array(sent.challenge)).toEqual(new Uint8Array([1, 2, 3]));
    expect(new Uint8Array(sent.user.id)).toEqual(new Uint8Array([4, 5, 6]));
    expect(new Uint8Array(sent.excludeCredentials[0].id)).toEqual(new Uint8Array([1, 2, 3]));
    expect(result).toEqual({
      id: 'AQID',
      rawId: 'AQID',
      type: 'public-key',
      response: { attestationObject: 'BAU', clientDataJSON: 'Bg' }
    });
  });

  it('uses a passkey to sign in and sends no credential list when the list is empty', async () => {
    const get = jest.fn().mockResolvedValue({
      id: 'AQID',
      rawId: bytes(1, 2, 3),
      type: 'public-key',
      response: { authenticatorData: bytes(1), clientDataJSON: bytes(2), signature: bytes(3), userHandle: bytes(4) }
    });
    setCredentials({ get });
    const result = await getPasskey({ challenge: 'AQID', allowCredentials: [] });
    expect(get.mock.calls[0][0].publicKey.allowCredentials).toEqual([]);
    expect(result?.response.userHandle).toBe('BA');
    expect(result?.response.signature).toBe('Aw');
  });

  it('gives null when the owner cancels and passes other errors on', async () => {
    const cancel = Object.assign(new Error('cancel'), { name: 'NotAllowedError' });
    setCredentials({ get: jest.fn().mockRejectedValue(cancel) });
    await expect(getPasskey({ challenge: 'AQID' })).resolves.toBeNull();
    setCredentials({ get: jest.fn().mockRejectedValue(new Error('other')) });
    await expect(getPasskey({ challenge: 'AQID' })).rejects.toThrow('other');
    setCredentials({ get: jest.fn().mockResolvedValue(null) });
    await expect(getPasskey({ challenge: 'AQID' })).resolves.toBeNull();
  });
});
