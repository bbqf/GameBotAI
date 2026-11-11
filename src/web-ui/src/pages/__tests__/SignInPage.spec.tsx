import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { SignInPage, SIGN_IN_FAILURE_TEXT } from '../SignInPage';
import * as authApi from '../../services/authApi';
import * as passkey from '../../lib/passkey';

jest.mock('../../services/authApi');
jest.mock('../../lib/passkey');

const mockedApi = authApi as jest.Mocked<typeof authApi>;
const mockedPasskey = passkey as jest.Mocked<typeof passkey>;

beforeEach(() => {
  mockedPasskey.isPasskeySupported.mockReturnValue(true);
});
afterEach(() => jest.resetAllMocks());

describe('SignInPage (contract U-2)', () => {
  it('has one button and no password field, user name field, or registration link', () => {
    render(<SignInPage onSignedIn={jest.fn()} />);
    expect(screen.getByRole('heading', { name: 'GameBot' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign in with passkey' })).toBeEnabled();
    expect(screen.queryByLabelText(/password/i)).toBeNull();
    expect(screen.queryByLabelText(/user/i)).toBeNull();
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('calls onSignedIn after a correct sign-in', async () => {
    mockedApi.signInWithPasskey.mockResolvedValue('signedIn');
    const onSignedIn = jest.fn();
    render(<SignInPage onSignedIn={onSignedIn} />);
    fireEvent.click(screen.getByRole('button', { name: 'Sign in with passkey' }));
    await waitFor(() => expect(onSignedIn).toHaveBeenCalledTimes(1));
  });

  it('shows one failure text for a refusal and for an error', async () => {
    mockedApi.signInWithPasskey.mockResolvedValueOnce('failed');
    render(<SignInPage onSignedIn={jest.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Sign in with passkey' }));
    expect(await screen.findByText(SIGN_IN_FAILURE_TEXT)).toBeInTheDocument();

    mockedApi.signInWithPasskey.mockRejectedValueOnce(new Error('boom'));
    fireEvent.click(screen.getByRole('button', { name: 'Sign in with passkey' }));
    await waitFor(() => expect(screen.getByText(SIGN_IN_FAILURE_TEXT)).toBeInTheDocument());
  });

  it('shows no error after a cancel', async () => {
    mockedApi.signInWithPasskey.mockResolvedValue('cancelled');
    const onSignedIn = jest.fn();
    render(<SignInPage onSignedIn={onSignedIn} />);
    fireEvent.click(screen.getByRole('button', { name: 'Sign in with passkey' }));
    await waitFor(() => expect(mockedApi.signInWithPasskey).toHaveBeenCalled());
    expect(screen.queryByRole('alert')).toBeNull();
    expect(onSignedIn).not.toHaveBeenCalled();
  });

  it('turns the button off when the browser has no passkey support', () => {
    mockedPasskey.isPasskeySupported.mockReturnValue(false);
    render(<SignInPage onSignedIn={jest.fn()} />);
    expect(screen.getByText('This browser does not support passkeys.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign in with passkey' })).toBeDisabled();
  });
});
