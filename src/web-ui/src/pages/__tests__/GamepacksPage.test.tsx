import React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { GamepacksPage } from '../GamepacksPage';
import {
  CatalogApiError,
  CatalogInstallRejectedError,
  getCatalogSettings,
  installFromCatalog,
  listCatalog,
  putCatalogSettings
} from '../../services/gamepackCatalog';

jest.mock('../../services/gamepackCatalog', () => {
  const actual = jest.requireActual('../../services/gamepackCatalog');
  return {
    ...actual,
    getCatalogSettings: jest.fn(),
    putCatalogSettings: jest.fn(),
    listCatalog: jest.fn(),
    installFromCatalog: jest.fn()
  };
});

const mockSettings = getCatalogSettings as jest.MockedFunction<typeof getCatalogSettings>;
const mockPut = putCatalogSettings as jest.MockedFunction<typeof putCatalogSettings>;
const mockList = listCatalog as jest.MockedFunction<typeof listCatalog>;
const mockInstall = installFromCatalog as jest.MockedFunction<typeof installFromCatalog>;

const list = {
  gamepacks: [
    {
      name: 'alpha',
      installedVersion: '1.0.0',
      versions: [
        { version: '1.2.0', changelog: 'Adds a new daily', installed: false, updateAvailable: true },
        { version: '1.0.0', changelog: 'First release', installed: true, updateAvailable: false }
      ]
    },
    {
      name: 'beta',
      installedVersion: null,
      versions: [{ version: '0.1.0', changelog: '', installed: false, updateAvailable: true }]
    }
  ]
};

const installOk = { dryRun: false, valid: true, created: 2, updated: 1, removed: 0 };

beforeEach(() => {
  jest.resetAllMocks();
  mockSettings.mockResolvedValue({ location: 'C:\\catalog', allowInsecure: false, tokenSet: true });
  mockList.mockResolvedValue(list);
});

describe('GamepacksPage', () => {
  it('shows versions, change text, and the installed marker', async () => {
    render(<GamepacksPage />);
    expect(await screen.findByText('Adds a new daily')).toBeInTheDocument();
    expect(screen.getByText('First release')).toBeInTheDocument();
    expect(screen.getAllByText('Installed').length).toBe(1);
  });

  it('shows the Update button only for a newer version and Install when none is installed', async () => {
    render(<GamepacksPage />);
    await screen.findByText('Adds a new daily');
    expect(screen.getByRole('button', { name: 'Update alpha 1.2.0' })).toHaveTextContent('Update');
    expect(screen.queryByRole('button', { name: 'Update alpha 1.0.0' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Install beta 0.1.0' })).toHaveTextContent('Install');
  });

  it('installs a version, shows the result, and reloads the list', async () => {
    mockInstall.mockResolvedValue(installOk);
    render(<GamepacksPage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Update alpha 1.2.0' }));
    expect(await screen.findByText(/Installed alpha 1\.2\.0/)).toBeInTheDocument();
    expect(mockInstall).toHaveBeenCalledWith('alpha', '1.2.0', false, false);
    await waitFor(() => expect(mockList).toHaveBeenCalledTimes(2));
  });

  it('shows a plain reason when an install fails', async () => {
    mockInstall.mockRejectedValue(new CatalogApiError(502, 'hash_mismatch', 'The file does not match its hash. The install stopped.'));
    render(<GamepacksPage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Update alpha 1.2.0' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('does not match its hash');
    expect(mockList).toHaveBeenCalledTimes(1);
  });

  it('shows the not-configured state', async () => {
    mockSettings.mockResolvedValue({ location: null, allowInsecure: false, tokenSet: false });
    mockList.mockRejectedValue(new CatalogApiError(409, 'not_configured', 'No catalog is set.'));
    render(<GamepacksPage />);
    expect(await screen.findByText(/No catalog is set/)).toBeInTheDocument();
  });

  it('never fills the token field and keeps the token when the field is empty', async () => {
    mockPut.mockResolvedValue({ location: 'C:\\catalog', allowInsecure: false, tokenSet: true });
    render(<GamepacksPage />);
    await screen.findByText('Adds a new daily');
    expect(screen.getByLabelText('Token')).toHaveValue('');
    expect(screen.getByText(/A token is set/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));
    await waitFor(() => expect(mockPut).toHaveBeenCalledWith({ location: 'C:\\catalog', allowInsecure: false, token: null }));
  });

  it('runs a dry run when the check box is on', async () => {
    mockInstall.mockResolvedValue({ ...installOk, dryRun: true });
    render(<GamepacksPage />);
    fireEvent.click(await screen.findByLabelText('Dry run'));
    fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));
    expect(await screen.findByText(/Dry run for alpha 1\.2\.0/)).toHaveTextContent('Nothing changed');
    expect(mockInstall).toHaveBeenCalledWith('alpha', '1.2.0', true, false);
    expect(mockList).toHaveBeenCalledTimes(1);
  });

  it('calls the client with dryRun false when the check box is off', async () => {
    mockInstall.mockResolvedValue(installOk);
    render(<GamepacksPage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Install beta 0.1.0' }));
    await screen.findByText(/Installed beta 0\.1\.0/);
    expect(mockInstall).toHaveBeenCalledWith('beta', '0.1.0', false, false);
  });

  it('reloads the list on Refresh', async () => {
    render(<GamepacksPage />);
    await screen.findByText('Adds a new daily');
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(mockList).toHaveBeenCalledTimes(2));
  });

  it('does not install or reload on a timer', async () => {
    jest.useFakeTimers();
    try {
      render(<GamepacksPage />);
      await act(async () => {
        await Promise.resolve();
      });
      expect(mockList).toHaveBeenCalledTimes(1);
      await act(async () => {
        jest.advanceTimersByTime(10 * 60 * 1000);
      });
      expect(mockList).toHaveBeenCalledTimes(1);
      expect(mockInstall).not.toHaveBeenCalled();
    } finally {
      jest.useRealTimers();
    }
  });

  describe('take-over', () => {
    const TAKE_OVER = 'Take over objects of the user with the same name';
    const dryOk = { ...installOk, dryRun: true, takenOver: 3 };
    const hint = 'Run the dry run with take-over first.';

    const toggle = (label: string) => fireEvent.click(screen.getByLabelText(label));
    const ready = async () => {
      render(<GamepacksPage />);
      await screen.findByText('Adds a new daily');
    };

    it('shows the check box and hint at load, with the box off', async () => {
      await ready();
      expect(screen.getByLabelText(TAKE_OVER)).not.toBeChecked();
      expect(screen.getByText(/The gamepack owns these objects and replaces them/)).toBeInTheDocument();
    });

    it('lists conflicts when take-over is off and keeps the box available', async () => {
      mockInstall.mockResolvedValue({
        ...dryOk,
        valid: false,
        takenOver: 0,
        problems: [{ code: 'conflict', message: 'Task "daily" is owned by the user.' }]
      });
      await ready();
      toggle('Dry run');
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));
      expect(await screen.findByRole('alert')).toHaveTextContent('Task "daily" is owned by the user.');
      expect(screen.getByRole('alert')).not.toHaveTextContent('Take over');
      expect(screen.getByLabelText(TAKE_OVER)).toBeEnabled();
    });

    it('sends takeOver in the dry run request and shows the count', async () => {
      mockInstall.mockResolvedValue(dryOk);
      await ready();
      toggle('Dry run');
      toggle(TAKE_OVER);
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));
      expect(await screen.findByText(/Take over 3\./)).toBeInTheDocument();
      expect(mockInstall).toHaveBeenCalledWith('alpha', '1.2.0', true, true);
    });

    it('shows the count for an invalid dry run with take-over on', async () => {
      mockInstall.mockResolvedValue({ ...dryOk, valid: false, takenOver: 2 });
      await ready();
      toggle('Dry run');
      toggle(TAKE_OVER);
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));
      expect(await screen.findByRole('alert')).toHaveTextContent('Take over 2.');
    });

    it('shows Take over 0 and enables Update when the count is 0', async () => {
      mockInstall.mockResolvedValue({ ...dryOk, takenOver: 0 });
      await ready();
      toggle('Dry run');
      toggle(TAKE_OVER);
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));
      expect(await screen.findByText(/Take over 0\./)).toBeInTheDocument();
      toggle('Dry run');
      expect(screen.getByRole('button', { name: 'Update alpha 1.2.0' })).toBeEnabled();
    });

    it('blocks Update without a valid matching dry run', async () => {
      await ready();
      toggle(TAKE_OVER);
      expect(screen.getByRole('button', { name: 'Update alpha 1.2.0' })).toBeDisabled();
      expect(screen.getAllByText(hint).length).toBe(2);
      expect(screen.getByRole('button', { name: 'Install beta 0.1.0' })).toBeDisabled();
    });

    it('blocks Update after an invalid dry run', async () => {
      mockInstall.mockResolvedValue({ ...dryOk, valid: false });
      await ready();
      toggle(TAKE_OVER);
      toggle('Dry run');
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));
      await screen.findByRole('alert');
      toggle('Dry run');
      expect(screen.getByRole('button', { name: 'Update alpha 1.2.0' })).toBeDisabled();
    });

    it('sends takeOver true for a real Update after a valid matching dry run', async () => {
      mockInstall.mockResolvedValueOnce(dryOk).mockResolvedValueOnce(installOk);
      await ready();
      toggle('Dry run');
      toggle(TAKE_OVER);
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));
      await screen.findByText(/Take over 3\./);
      toggle('Dry run');
      const button = screen.getByRole('button', { name: 'Update alpha 1.2.0' });
      expect(button).toBeEnabled();
      fireEvent.click(button);
      await screen.findByText(/Installed alpha 1\.2\.0/);
      expect(mockInstall).toHaveBeenLastCalledWith('alpha', '1.2.0', false, true);
    });

    it('sends takeOver false for a real Update with the box off', async () => {
      mockInstall.mockResolvedValue(installOk);
      await ready();
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));
      await screen.findByText(/Installed alpha 1\.2\.0/);
      expect(mockInstall).toHaveBeenCalledWith('alpha', '1.2.0', false, false);
    });

    it('clears the notice and blocks the buttons when the take-over box changes', async () => {
      mockInstall.mockResolvedValue(dryOk);
      await ready();
      toggle('Dry run');
      toggle(TAKE_OVER);
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));
      await screen.findByText(/Take over 3\./);
      toggle(TAKE_OVER);
      toggle(TAKE_OVER);
      expect(screen.queryByText(/Take over 3\./)).toBeNull();
      toggle('Dry run');
      expect(screen.getByRole('button', { name: 'Update alpha 1.2.0' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Install beta 0.1.0' })).toBeDisabled();
    });

    it('keeps the last dry run when the Dry run box changes', async () => {
      mockInstall.mockResolvedValue(dryOk);
      await ready();
      toggle('Dry run');
      toggle(TAKE_OVER);
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));
      await screen.findByText(/Take over 3\./);
      toggle('Dry run');
      toggle('Dry run');
      expect(screen.getByText(/Take over 3\./)).toBeInTheDocument();
      expect(mockInstall).toHaveBeenCalledTimes(1);
    });

    it('shows the error for a failed dry run and lets the operator try again', async () => {
      mockInstall
        .mockRejectedValueOnce(new CatalogApiError(502, 'timeout', 'The catalog did not answer in time.'))
        .mockResolvedValueOnce(dryOk);
      await ready();
      toggle('Dry run');
      toggle(TAKE_OVER);
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));
      expect(await screen.findByRole('alert')).toHaveTextContent('did not answer in time');
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));
      expect(await screen.findByText(/Take over 3\./)).toBeInTheDocument();
    });

    it('applies to the Install button of a not-installed gamepack', async () => {
      mockInstall.mockResolvedValueOnce(dryOk).mockResolvedValueOnce(installOk);
      await ready();
      toggle('Dry run');
      toggle(TAKE_OVER);
      fireEvent.click(screen.getByRole('button', { name: 'Install beta 0.1.0' }));
      await screen.findByText(/Take over 3\./);
      toggle('Dry run');
      fireEvent.click(screen.getByRole('button', { name: 'Install beta 0.1.0' }));
      await screen.findByText(/Installed beta 0\.1\.0/);
      expect(mockInstall).toHaveBeenLastCalledWith('beta', '0.1.0', false, true);
    });

    it('does not enable another version after a valid dry run', async () => {
      mockInstall.mockResolvedValue(dryOk);
      await ready();
      toggle('Dry run');
      toggle(TAKE_OVER);
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));
      await screen.findByText(/Take over 3\./);
      toggle('Dry run');
      expect(screen.getByRole('button', { name: 'Update alpha 1.2.0' })).toBeEnabled();
      expect(screen.getByRole('button', { name: 'Install beta 0.1.0' })).toBeDisabled();
      expect(screen.getAllByText(hint).length).toBe(1);
    });
  });

  describe('failure explanation', () => {
    const summary = {
      headline: '20 conflicts.',
      entries: [
        {
          category: 'conflict',
          count: 20,
          label: 'conflicts: objects owned by the user',
          remedy: 'Install with take-over to replace them, or rename or delete them.',
          resolvedByTakeOver: true
        }
      ]
    };
    const failed = (extra: object = {}) => ({
      dryRun: false,
      valid: false,
      created: 0,
      updated: 0,
      removed: 0,
      summary,
      wouldInstallWithTakeOver: true,
      problems: [{ message: 'A sequence exists.', takeOverResolves: true, remedy: 'x' }],
      ...extra
    });
    const toggle = (label: string) => fireEvent.click(screen.getByLabelText(label));
    const ready = async () => {
      render(<GamepacksPage />);
      await screen.findByText('Adds a new daily');
    };

    it('shows the summary and the take-over sentence for a failed dry run', async () => {
      mockInstall.mockResolvedValueOnce(failed({ dryRun: true }));
      await ready();
      toggle('Dry run');
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));

      const alert = await screen.findByRole('alert');
      expect(alert).toHaveTextContent('20 conflicts: objects owned by the user. Install with take-over');
      expect(alert).toHaveTextContent('Take-over would resolve all problems.');
      expect(alert).not.toHaveTextContent('A sequence exists.');
    });

    it('says that take-over would not resolve all problems when the field is false', async () => {
      mockInstall.mockResolvedValueOnce(failed({ dryRun: true, wouldInstallWithTakeOver: false }));
      await ready();
      toggle('Dry run');
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));

      expect(await screen.findByRole('alert')).toHaveTextContent('Take-over would not resolve all problems.');
    });

    it('shows the summary for a failed update (422)', async () => {
      mockInstall.mockRejectedValueOnce(new CatalogInstallRejectedError('install_rejected', 'A sequence exists.', failed()));
      await ready();
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));

      const alert = await screen.findByRole('alert');
      expect(alert).toHaveTextContent('Install of alpha 1.2.0 failed. Nothing changed.');
      expect(alert).toHaveTextContent('20 conflicts: objects owned by the user.');
    });

    it('shows the summary for a failed install of a new gamepack', async () => {
      mockInstall.mockRejectedValueOnce(new CatalogInstallRejectedError('install_rejected', 'A sequence exists.', failed()));
      await ready();
      fireEvent.click(screen.getByRole('button', { name: 'Install beta 0.1.0' }));

      expect(await screen.findByRole('alert')).toHaveTextContent('20 conflicts: objects owned by the user.');
    });

    it('shows No problems. for a valid dry run', async () => {
      mockInstall.mockResolvedValueOnce({
        dryRun: true,
        valid: true,
        created: 1,
        updated: 0,
        removed: 0,
        summary: { headline: 'No problems.', entries: [] },
        wouldInstallWithTakeOver: true
      });
      await ready();
      toggle('Dry run');
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));

      expect(await screen.findByRole('status')).toHaveTextContent('No problems.');
    });

    it('shows a reason when the error has an empty message', async () => {
      mockInstall.mockRejectedValueOnce(new Error(''));
      await ready();
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));

      expect(await screen.findByRole('alert')).toHaveTextContent('The cause is not known. See the service log.');
    });

    it('shows the problem messages when the answer has no summary (old server)', async () => {
      mockInstall.mockResolvedValueOnce({
        dryRun: true,
        valid: false,
        created: 0,
        updated: 0,
        removed: 0,
        problems: [{ message: 'Old server problem.' }]
      });
      await ready();
      toggle('Dry run');
      fireEvent.click(screen.getByRole('button', { name: 'Update alpha 1.2.0' }));

      expect(await screen.findByRole('alert')).toHaveTextContent('Old server problem.');
    });
  });
});
