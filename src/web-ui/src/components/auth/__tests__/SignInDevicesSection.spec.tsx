import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { REMOVE_LAST_DEVICE_WARNING, REMOVE_THIS_DEVICE_WARNING, SignInDevicesSection } from '../SignInDevicesSection';
import * as authApi from '../../../services/authApi';

jest.mock('../../../services/authApi');
jest.mock('../../../lib/authStatus', () => ({ refreshAuthStatus: jest.fn().mockResolvedValue({}) }));

const mocked = authApi as jest.Mocked<typeof authApi>;

const item = (over: Partial<authApi.PasskeyItem>): authApi.PasskeyItem => ({
  id: 'a1', name: 'My phone', createdUtc: '2026-11-10T18:00:00Z', lastUsedUtc: null, current: false, ...over
});

afterEach(() => jest.resetAllMocks());

describe('SignInDevicesSection (contract U-4)', () => {
  it('lists the devices and marks this device', async () => {
    mocked.listPasskeys.mockResolvedValue([item({ id: 'a1', current: true }), item({ id: 'b2', name: 'Laptop' })]);
    render(<SignInDevicesSection />);
    expect(await screen.findByText(/My phone \(This device\)/)).toBeInTheDocument();
    expect(screen.getByText('Laptop')).toBeInTheDocument();
    expect(screen.getAllByText('This device')).toHaveLength(1);
    expect(screen.getAllByText(/Last used: Never/)).toHaveLength(2);
    expect(screen.getByText(/auth new-code/)).toBeInTheDocument();
  });

  it('renames a device', async () => {
    mocked.listPasskeys.mockResolvedValue([item({})]);
    mocked.renamePasskey.mockResolvedValue(item({ name: 'Work' }));
    render(<SignInDevicesSection />);
    fireEvent.click(await screen.findByRole('button', { name: 'Rename' }));
    fireEvent.change(screen.getByLabelText('Device name'), { target: { value: 'Work' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save name' }));
    await waitFor(() => expect(mocked.renamePasskey).toHaveBeenCalledWith('a1', 'Work'));
  });

  it('asks for confirmation before it removes a device', async () => {
    mocked.listPasskeys.mockResolvedValue([item({ id: 'a1' }), item({ id: 'b2', name: 'Laptop' })]);
    mocked.removePasskey.mockResolvedValue();
    render(<SignInDevicesSection />);
    fireEvent.click((await screen.findAllByRole('button', { name: 'Remove' }))[1]);
    expect(mocked.removePasskey).not.toHaveBeenCalled();
    expect(screen.queryByText(REMOVE_THIS_DEVICE_WARNING)).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Confirm remove' }));
    await waitFor(() => expect(mocked.removePasskey).toHaveBeenCalledWith('b2'));
  });

  it('warns for this device and for the last passkey', async () => {
    mocked.listPasskeys.mockResolvedValue([item({ id: 'a1', current: true })]);
    const first = render(<SignInDevicesSection />);
    fireEvent.click(await screen.findByRole('button', { name: 'Remove' }));
    expect(screen.getByText(REMOVE_THIS_DEVICE_WARNING)).toBeInTheDocument();
    first.unmount();

    mocked.listPasskeys.mockResolvedValue([item({ id: 'b2' })]);
    render(<SignInDevicesSection />);
    fireEvent.click(await screen.findByRole('button', { name: 'Remove' }));
    expect(screen.getByText(REMOVE_LAST_DEVICE_WARNING)).toBeInTheDocument();
  });

  it('signs out this device and all devices, the second after confirmation', async () => {
    mocked.listPasskeys.mockResolvedValue([item({})]);
    mocked.signOut.mockResolvedValue();
    mocked.endAllSessions.mockResolvedValue();
    render(<SignInDevicesSection />);
    fireEvent.click(await screen.findByRole('button', { name: 'Sign out' }));
    await waitFor(() => expect(mocked.signOut).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('button', { name: 'Sign out all devices' }));
    expect(mocked.endAllSessions).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Confirm sign out all' }));
    await waitFor(() => expect(mocked.endAllSessions).toHaveBeenCalled());
  });

  it('shows an error when the list does not load', async () => {
    mocked.listPasskeys.mockRejectedValue(new Error('The list of devices did not load.'));
    render(<SignInDevicesSection />);
    expect(await screen.findByRole('alert')).toHaveTextContent('The list of devices did not load.');
  });
});
