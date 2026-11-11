import { deriveGateView, getAuthState, refreshAuthStatus, resetAuthStateForTests, type AuthState } from '../authStatus';
import * as authApi from '../../services/authApi';
import { clearUnauthorized, noteResponseStatus } from '../unauthorized';

jest.mock('../../services/authApi');
const mocked = authApi as jest.Mocked<typeof authApi>;

const status = (over: Partial<authApi.AuthStatus> = {}): authApi.AuthStatus => ({
  signInEnabled: true,
  signedIn: false,
  passkeysSupportedHere: true,
  source: 'internet',
  publicOrigin: null,
  tokenAccessHere: false,
  ...over
});

const state = (over: Partial<AuthState>): AuthState => ({ loaded: true, status: status(), signInRequired: false, refused: false, ...over });

beforeEach(() => {
  resetAuthStateForTests();
  clearUnauthorized();
});

describe('deriveGateView (contract U-1)', () => {
  it('waits for the first answer', () => {
    expect(deriveGateView(state({ loaded: false }))).toBe('loading');
  });

  it('shows the content when the sign-in function is off or the status is not available', () => {
    expect(deriveGateView(state({ status: status({ signInEnabled: false }) }))).toBe('content');
    expect(deriveGateView(state({ status: null }))).toBe('content');
  });

  it('shows the content when the request has access', () => {
    expect(deriveGateView(state({ status: status({ signedIn: true }) }))).toBe('content');
  });

  it('shows the sign-in page when the request has no access and passkeys work here', () => {
    expect(deriveGateView(state({}))).toBe('signIn');
    expect(deriveGateView(state({ status: status({ signedIn: true }), signInRequired: true }))).toBe('signIn');
  });

  it('shows the public address text on an address with no passkey support', () => {
    expect(deriveGateView(state({ status: status({ passkeysSupportedHere: false, source: 'homeNetwork' }) }))).toBe('otherAddress');
  });

  it('shows the refusal text for a refused internet request', () => {
    expect(deriveGateView(state({ refused: true }))).toBe('refused');
  });
});

describe('refreshAuthStatus', () => {
  it('stores the status', async () => {
    mocked.getAuthStatus.mockResolvedValue({ status: status(), httpStatus: 200 });
    await refreshAuthStatus();
    expect(getAuthState().loaded).toBe(true);
    expect(getAuthState().status?.signInEnabled).toBe(true);
  });

  it('records a 403 remote_access_refused', async () => {
    mocked.getAuthStatus.mockResolvedValue({ status: null, httpStatus: 403, code: 'remote_access_refused' });
    await refreshAuthStatus();
    expect(getAuthState().refused).toBe(true);
  });

  it('works as before when the status call fails', async () => {
    mocked.getAuthStatus.mockRejectedValue(new Error('network'));
    await refreshAuthStatus();
    expect(deriveGateView(getAuthState())).toBe('content');
  });

  it('asks for the sign-in again after a 401 when the sign-in function is on', async () => {
    mocked.getAuthStatus.mockResolvedValue({ status: status({ signedIn: true }), httpStatus: 200 });
    await refreshAuthStatus();
    noteResponseStatus(401);
    expect(getAuthState().signInRequired).toBe(true);
    expect(deriveGateView(getAuthState())).toBe('signIn');
  });

  it('ignores a 401 when the sign-in function is off', async () => {
    mocked.getAuthStatus.mockResolvedValue({ status: status({ signInEnabled: false }), httpStatus: 200 });
    await refreshAuthStatus();
    noteResponseStatus(401);
    expect(getAuthState().signInRequired).toBe(false);
  });
});
