import React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AccessGate, OTHER_ADDRESS_TEXT, REFUSED_TEXT } from '../AccessGate';
import * as authApi from '../../../services/authApi';
import { resetAuthStateForTests } from '../../../lib/authStatus';
import { clearUnauthorized } from '../../../lib/unauthorized';
import { token$ } from '../../../lib/token';

jest.mock('../../../services/authApi');
jest.mock('../../../lib/passkey', () => ({ isPasskeySupported: () => true }));

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

const renderGate = () => render(<AccessGate><div>CONTENT</div></AccessGate>);

beforeEach(() => {
  resetAuthStateForTests();
  clearUnauthorized();
  window.history.replaceState(null, '', '/');
});
afterEach(() => jest.resetAllMocks());

describe('AccessGate (contract U-1)', () => {
  it('shows the content as before when the sign-in function is off', async () => {
    mocked.getAuthStatus.mockResolvedValue({ status: status({ signInEnabled: false, signedIn: true }), httpStatus: 200 });
    renderGate();
    expect(await screen.findByText('CONTENT')).toBeInTheDocument();
  });

  it('shows the sign-in page in place of the content and keeps the address', async () => {
    window.history.replaceState(null, '', '/?area=queues');
    mocked.getAuthStatus.mockResolvedValue({ status: status(), httpStatus: 200 });
    renderGate();
    expect(await screen.findByRole('button', { name: 'Sign in with passkey' })).toBeInTheDocument();
    expect(screen.queryByText('CONTENT')).toBeNull();
    expect(window.location.search).toBe('?area=queues');
  });

  it('shows the content and loads it again after the sign-in', async () => {
    mocked.getAuthStatus
      .mockResolvedValueOnce({ status: status(), httpStatus: 200 })
      .mockResolvedValue({ status: status({ signedIn: true }), httpStatus: 200 });
    mocked.signInWithPasskey.mockResolvedValue('signedIn');
    renderGate();
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in with passkey' }));
    expect(await screen.findByText('CONTENT')).toBeInTheDocument();
  });

  it('shows the public address link and no token prompt when the token does not work here', async () => {
    mocked.getAuthStatus.mockResolvedValue({
      status: status({ passkeysSupportedHere: false, source: 'homeNetwork', publicOrigin: 'https://bot.example.com', tokenAccessHere: false }),
      httpStatus: 200
    });
    renderGate();
    expect(await screen.findByText(OTHER_ADDRESS_TEXT)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'https://bot.example.com' })).toHaveAttribute('href', 'https://bot.example.com');
    expect(screen.queryByLabelText('Access token')).toBeNull();
  });

  it('shows the token prompt on the direct address when a token is configured', async () => {
    mocked.getAuthStatus
      .mockResolvedValueOnce({
        status: status({ passkeysSupportedHere: false, source: 'homeNetwork', publicOrigin: 'https://bot.example.com', tokenAccessHere: true }),
        httpStatus: 200
      })
      .mockResolvedValue({ status: status({ passkeysSupportedHere: false, signedIn: true, source: 'homeNetwork', tokenAccessHere: true }), httpStatus: 200 });
    renderGate();
    fireEvent.change(await screen.findByLabelText('Access token'), { target: { value: 'secret' } });
    fireEvent.click(screen.getByRole('button', { name: 'Use token' }));
    expect(await screen.findByText('CONTENT')).toBeInTheDocument();
    expect(token$.get()).toBe('secret');
    token$.set(null);
  });

  it('shows the refusal text and no sign-in control for a refused internet request', async () => {
    mocked.getAuthStatus.mockResolvedValue({ status: null, httpStatus: 403, code: 'remote_access_refused' });
    renderGate();
    expect(await screen.findByText(REFUSED_TEXT)).toBeInTheDocument();
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('shows the registration page at /register with no session', async () => {
    window.history.replaceState(null, '', '/register');
    mocked.getAuthStatus.mockResolvedValue({ status: status(), httpStatus: 200 });
    renderGate();
    expect(await screen.findByLabelText('Registration code')).toBeInTheDocument();
  });

  it('opens the start page after a registration', async () => {
    window.history.replaceState(null, '', '/register');
    mocked.getAuthStatus
      .mockResolvedValueOnce({ status: status(), httpStatus: 200 })
      .mockResolvedValue({ status: status({ signedIn: true }), httpStatus: 200 });
    mocked.registerPasskey.mockResolvedValue('registered');
    renderGate();
    fireEvent.change(await screen.findByLabelText('Registration code'), { target: { value: 'AAAA-AAAA' } });
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Register this device' }));
    });
    await waitFor(() => expect(screen.getByText('CONTENT')).toBeInTheDocument());
    expect(window.location.pathname).toBe('/');
  });
});
