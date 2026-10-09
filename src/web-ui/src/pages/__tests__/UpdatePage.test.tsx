import React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { UpdatePage, UPDATE_POLL_INTERVAL_MS } from '../UpdatePage';
import { checkForUpdate, getUpdateStatus, installUpdate, UpdateApiError } from '../../services/update';
import type { UpdateAttempt, UpdateCheckResult, UpdateStatus } from '../../services/update';

jest.mock('../../services/update', () => ({
  ...jest.requireActual('../../services/update'),
  getUpdateStatus: jest.fn(),
  checkForUpdate: jest.fn(),
  installUpdate: jest.fn()
}));

const mockStatus = getUpdateStatus as jest.MockedFunction<typeof getUpdateStatus>;
const mockCheck = checkForUpdate as jest.MockedFunction<typeof checkForUpdate>;
const mockInstall = installUpdate as jest.MockedFunction<typeof installUpdate>;

const baseStatus: UpdateStatus = {
  installedVersion: '1.7.0.412',
  lastCheck: null,
  attempt: null,
  lastResult: null,
  canInstallHere: true,
  installBlockedReason: null
};

const available = (notes: string | null = 'Fixes and news'): UpdateCheckResult => ({
  status: 'updateAvailable',
  installedVersion: '1.7.0.412',
  latestVersion: '1.7.0.430',
  notes,
  checkedAtUtc: '2026-10-09T10:00:00Z',
  error: null
});

const attempt = (state: UpdateAttempt['state'], extra: Partial<UpdateAttempt> = {}): UpdateAttempt => ({
  attemptId: 'attempt-1',
  state,
  targetVersion: '1.7.0.430',
  fromVersion: '1.7.0.412',
  startedAtUtc: '2026-10-09T10:01:00Z',
  ...extra
});

const renderPage = async () => {
  render(<UpdatePage />);
  await screen.findByText('1.7.0.412');
};

const clickCheck = async () => {
  fireEvent.click(screen.getByRole('button', { name: 'Check for Update' }));
  await waitFor(() => expect(mockCheck).toHaveBeenCalled());
};

describe('UpdatePage', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    mockStatus.mockResolvedValue(baseStatus);
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('shows the installed version and the check button', async () => {
    await renderPage();

    expect(screen.getByRole('button', { name: 'Check for Update' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Install update' })).not.toBeInTheDocument();
  });

  it('shows an error when the state cannot be loaded', async () => {
    mockStatus.mockRejectedValue(new Error('Network down'));

    render(<UpdatePage />);

    expect(await screen.findByText('Network down')).toBeInTheDocument();
  });

  it('shows the new version, the notes, and the install button after a check', async () => {
    mockCheck.mockResolvedValue(available());
    await renderPage();

    await clickCheck();

    expect(await screen.findByText('1.7.0.430')).toBeInTheDocument();
    expect(screen.getByText('Fixes and news')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Install update' })).toBeInTheDocument();
  });

  it('shows release notes as plain text and runs no markup', async () => {
    const hostile = '<img src=x onerror="alert(1)"><script>alert(2)</script> **bold**';
    mockCheck.mockResolvedValue(available(hostile));
    const { container } = render(<UpdatePage />);
    await screen.findByText('1.7.0.412');

    await clickCheck();

    expect(await screen.findByText(hostile)).toBeInTheDocument();
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('script')).toBeNull();
    expect(container.querySelector('strong + pre, pre strong')).toBeNull();
  });

  it.each([null, '', '   '])('hides the notes area when the notes are %p', async (notes) => {
    mockCheck.mockResolvedValue(available(notes));
    await renderPage();

    await clickCheck();

    await screen.findByText('1.7.0.430');
    expect(screen.queryByText('Release notes')).not.toBeInTheDocument();
  });

  it('shows a confirm dialog that warns about the queues, and the cancel button closes it', async () => {
    mockCheck.mockResolvedValue(available());
    await renderPage();
    await clickCheck();

    fireEvent.click(await screen.findByRole('button', { name: 'Install update' }));

    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('All active queues stop at once');
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(mockInstall).not.toHaveBeenCalled();
  });

  it('sends the install request only after the user accepts', async () => {
    mockCheck.mockResolvedValue(available());
    mockInstall.mockResolvedValue({ attemptId: 'attempt-1', state: 'downloading' });
    await renderPage();
    await clickCheck();
    fireEvent.click(await screen.findByRole('button', { name: 'Install update' }));
    expect(mockInstall).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Stop queues and install' }));

    await waitFor(() => expect(mockInstall).toHaveBeenCalledWith('1.7.0.430'));
    expect(await screen.findByRole('status')).toBeInTheDocument();
  });

  it('shows progress, waits through the restart, and then shows the success state', async () => {
    jest.useFakeTimers();
    mockCheck.mockResolvedValue(available());
    mockInstall.mockResolvedValue({ attemptId: 'attempt-1', state: 'downloading' });
    mockStatus.mockResolvedValue(baseStatus);
    render(<UpdatePage />);
    await act(async () => { await Promise.resolve(); });
    fireEvent.click(screen.getByRole('button', { name: 'Check for Update' }));
    await act(async () => { await Promise.resolve(); });
    fireEvent.click(screen.getByRole('button', { name: 'Install update' }));
    fireEvent.click(screen.getByRole('button', { name: 'Stop queues and install' }));
    await act(async () => { await Promise.resolve(); });

    mockStatus.mockResolvedValueOnce({ ...baseStatus, attempt: attempt('verifying') });
    await act(async () => { jest.advanceTimersByTime(UPDATE_POLL_INTERVAL_MS); });
    expect(screen.getByText('Checking the download...')).toBeInTheDocument();

    mockStatus.mockRejectedValueOnce(new Error('connection refused'));
    await act(async () => { jest.advanceTimersByTime(UPDATE_POLL_INTERVAL_MS); });
    expect(screen.getByText(/GameBot restarts/)).toBeInTheDocument();

    mockStatus.mockResolvedValueOnce({ ...baseStatus, installedVersion: '1.7.0.430', lastResult: attempt('succeeded') });
    await act(async () => { jest.advanceTimersByTime(UPDATE_POLL_INTERVAL_MS); });
    expect(screen.getByText('The update to 1.7.0.430 is done.')).toBeInTheDocument();
    expect(screen.queryByText(/GameBot restarts/)).not.toBeInTheDocument();
  });

  it('ignores an old result of another attempt while it waits', async () => {
    jest.useFakeTimers();
    mockCheck.mockResolvedValue(available());
    mockInstall.mockResolvedValue({ attemptId: 'attempt-1', state: 'downloading' });
    render(<UpdatePage />);
    await act(async () => { await Promise.resolve(); });
    fireEvent.click(screen.getByRole('button', { name: 'Check for Update' }));
    await act(async () => { await Promise.resolve(); });
    fireEvent.click(screen.getByRole('button', { name: 'Install update' }));
    fireEvent.click(screen.getByRole('button', { name: 'Stop queues and install' }));
    await act(async () => { await Promise.resolve(); });

    mockStatus.mockResolvedValueOnce({ ...baseStatus, lastResult: attempt('succeeded', { attemptId: 'older-attempt' }) });
    await act(async () => { jest.advanceTimersByTime(UPDATE_POLL_INTERVAL_MS); });

    expect(screen.queryByText(/is done/)).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('shows the failure when the attempt fails during the install', async () => {
    jest.useFakeTimers();
    mockCheck.mockResolvedValue(available());
    mockInstall.mockResolvedValue({ attemptId: 'attempt-1', state: 'downloading' });
    render(<UpdatePage />);
    await act(async () => { await Promise.resolve(); });
    fireEvent.click(screen.getByRole('button', { name: 'Check for Update' }));
    await act(async () => { await Promise.resolve(); });
    fireEvent.click(screen.getByRole('button', { name: 'Install update' }));
    fireEvent.click(screen.getByRole('button', { name: 'Stop queues and install' }));
    await act(async () => { await Promise.resolve(); });

    mockStatus.mockResolvedValueOnce({
      ...baseStatus,
      attempt: attempt('failed', { errorCode: 'update_checksum_mismatch', errorMessage: 'The checksum is wrong.', errorHint: 'Try again.' })
    });
    await act(async () => { jest.advanceTimersByTime(UPDATE_POLL_INTERVAL_MS); });

    expect(screen.getByText(/The update to 1.7.0.430 failed/)).toBeInTheDocument();
    expect(screen.getByText('Error code: update_checksum_mismatch')).toBeInTheDocument();
  });

  it('shows a start error with its hint', async () => {
    mockCheck.mockResolvedValue(available());
    mockInstall.mockRejectedValue(new UpdateApiError(422, 'update_disk_space', 'There is not enough free disk space for the update.', 'Free disk space on the data drive, then try again.'));
    await renderPage();
    await clickCheck();
    fireEvent.click(await screen.findByRole('button', { name: 'Install update' }));

    fireEvent.click(screen.getByRole('button', { name: 'Stop queues and install' }));

    expect(await screen.findByText('There is not enough free disk space for the update.')).toBeInTheDocument();
    expect(screen.getByText('Free disk space on the data drive, then try again.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Check for Update' })).toBeEnabled();
  });

  it('shows the up-to-date state', async () => {
    mockCheck.mockResolvedValue({ status: 'upToDate', installedVersion: '1.7.0.412', latestVersion: '1.7.0.412', checkedAtUtc: '2026-10-09T10:00:00Z' });
    await renderPage();

    await clickCheck();

    expect(await screen.findByText('GameBot is up to date.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Install update' })).not.toBeInTheDocument();
  });

  it('shows the failed-check message and its hint', async () => {
    mockCheck.mockResolvedValue({
      status: 'checkFailed',
      installedVersion: '1.7.0.412',
      checkedAtUtc: '2026-10-09T10:00:00Z',
      error: { code: 'update_rate_limited', message: 'GitHub refused the request because of the request limit.', hint: 'Try again later.' }
    });
    await renderPage();

    await clickCheck();

    expect(await screen.findByText('GitHub refused the request because of the request limit.')).toBeInTheDocument();
    expect(screen.getByText('Try again later.')).toBeInTheDocument();
  });

  it('disables the check button while a check runs', async () => {
    let finish: (value: UpdateCheckResult) => void = () => undefined;
    mockCheck.mockImplementation(() => new Promise<UpdateCheckResult>((resolve) => { finish = resolve; }));
    await renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'Check for Update' }));

    expect(await screen.findByRole('button', { name: 'Checking...' })).toBeDisabled();
    await act(async () => { finish(available()); });
    expect(await screen.findByRole('button', { name: 'Check for Update' })).toBeEnabled();
  });

  it('shows an error when the check request itself fails', async () => {
    mockCheck.mockRejectedValue(new UpdateApiError(409, 'update_in_progress', 'An update is in progress.', 'Wait until the update ends.'));
    await renderPage();

    await clickCheck();

    expect(await screen.findByText('An update is in progress.')).toBeInTheDocument();
    expect(screen.getByText('Wait until the update ends.')).toBeInTheDocument();
  });

  it('shows no install button on a remote PC and says why', async () => {
    mockStatus.mockResolvedValue({ ...baseStatus, canInstallHere: false, installBlockedReason: 'remote' });
    mockCheck.mockResolvedValue(available());
    await renderPage();

    await clickCheck();

    expect(await screen.findByText('Install from the bot PC.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Install update' })).not.toBeInTheDocument();
  });

  it('shows no install button for a bot that is not installed and says why', async () => {
    mockStatus.mockResolvedValue({ ...baseStatus, canInstallHere: false, installBlockedReason: 'notInstalled' });
    mockCheck.mockResolvedValue(available());
    await renderPage();

    await clickCheck();

    expect(await screen.findByText('Update works only for an installed bot.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Install update' })).not.toBeInTheDocument();
  });

  it('shows the success result of the last attempt after a restart, and the close button hides it', async () => {
    mockStatus.mockResolvedValue({ ...baseStatus, lastResult: attempt('succeeded') });

    render(<UpdatePage />);

    expect(await screen.findByText('The update to 1.7.0.430 is done.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(screen.queryByText('The update to 1.7.0.430 is done.')).not.toBeInTheDocument();
  });

  it('shows the error code, message, hint, and log path of a failed result', async () => {
    mockStatus.mockResolvedValue({
      ...baseStatus,
      lastResult: attempt('failed', {
        errorCode: 'update_install_failed',
        errorMessage: 'The installer stopped with a fatal error (code 1603).',
        errorHint: 'Close other programs that use the GameBot files, then try again.',
        msiexecExitCode: 1603,
        logPath: 'C:\\data\\updates\\msiexec-1.log'
      })
    });

    render(<UpdatePage />);

    expect(await screen.findByText(/The update to 1.7.0.430 failed/)).toBeInTheDocument();
    expect(screen.getByText('Error code: update_install_failed')).toBeInTheDocument();
    expect(screen.getByText('The installer stopped with a fatal error (code 1603).')).toBeInTheDocument();
    expect(screen.getByText('Close other programs that use the GameBot files, then try again.')).toBeInTheDocument();
    expect(screen.getByText('Installer exit code: 1603')).toBeInTheDocument();
    expect(screen.getByText('Installer log: C:\\data\\updates\\msiexec-1.log')).toBeInTheDocument();
  });

  it('shows the last check from the state without a new click', async () => {
    mockStatus.mockResolvedValue({ ...baseStatus, lastCheck: available() });

    render(<UpdatePage />);

    expect(await screen.findByText('1.7.0.430')).toBeInTheDocument();
  });
});
