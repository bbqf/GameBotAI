import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { RegisterDevicePage, REGISTER_FAILURE_TEXT } from '../RegisterDevicePage';
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

const fill = (code: string, name = '') => {
  fireEvent.change(screen.getByLabelText('Registration code'), { target: { value: code } });
  if (name) fireEvent.change(screen.getByLabelText('Device name'), { target: { value: name } });
};

describe('RegisterDevicePage (contract U-3)', () => {
  it('has the fields, the button, and the help text', () => {
    render(<RegisterDevicePage onRegistered={jest.fn()} />);
    expect(screen.getByLabelText('Registration code')).toBeInTheDocument();
    expect(screen.getByLabelText('Device name')).toHaveAttribute('maxLength', '40');
    expect(screen.getByRole('button', { name: 'Register this device' })).toBeDisabled();
    expect(screen.getByText(/GameBot.Service.exe auth new-code/)).toBeInTheDocument();
  });

  it('changes the code to upper case and registers', async () => {
    mockedApi.registerPasskey.mockResolvedValue('registered');
    const onRegistered = jest.fn();
    render(<RegisterDevicePage onRegistered={onRegistered} />);
    fill('abcd-efgh', 'My phone');
    expect(screen.getByLabelText('Registration code')).toHaveValue('ABCD-EFGH');
    fireEvent.click(screen.getByRole('button', { name: 'Register this device' }));
    await waitFor(() => expect(onRegistered).toHaveBeenCalledTimes(1));
    expect(mockedApi.registerPasskey).toHaveBeenCalledWith('ABCD-EFGH', 'My phone');
  });

  it('shows one failure text for each cause', async () => {
    mockedApi.registerPasskey.mockResolvedValueOnce('failed');
    render(<RegisterDevicePage onRegistered={jest.fn()} />);
    fill('AAAA-AAAA');
    fireEvent.click(screen.getByRole('button', { name: 'Register this device' }));
    expect(await screen.findByText(REGISTER_FAILURE_TEXT)).toBeInTheDocument();

    mockedApi.registerPasskey.mockRejectedValueOnce(new Error('boom'));
    fireEvent.click(screen.getByRole('button', { name: 'Register this device' }));
    await waitFor(() => expect(screen.getByText(REGISTER_FAILURE_TEXT)).toBeInTheDocument());
  });

  it('shows no error after a cancel', async () => {
    mockedApi.registerPasskey.mockResolvedValue('cancelled');
    render(<RegisterDevicePage onRegistered={jest.fn()} />);
    fill('AAAA-AAAA');
    fireEvent.click(screen.getByRole('button', { name: 'Register this device' }));
    await waitFor(() => expect(mockedApi.registerPasskey).toHaveBeenCalled());
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('turns the form off when the browser has no passkey support', () => {
    mockedPasskey.isPasskeySupported.mockReturnValue(false);
    render(<RegisterDevicePage onRegistered={jest.fn()} />);
    expect(screen.getByText('This browser does not support passkeys.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Register this device' })).toBeDisabled();
  });
});
