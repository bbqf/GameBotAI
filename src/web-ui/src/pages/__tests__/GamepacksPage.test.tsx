import React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { GamepacksPage } from '../GamepacksPage';
import {
  CatalogApiError,
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
    expect(mockInstall).toHaveBeenCalledWith('alpha', '1.2.0', false);
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
    expect(mockInstall).toHaveBeenCalledWith('alpha', '1.2.0', true);
    expect(mockList).toHaveBeenCalledTimes(1);
  });

  it('calls the client with dryRun false when the check box is off', async () => {
    mockInstall.mockResolvedValue(installOk);
    render(<GamepacksPage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Install beta 0.1.0' }));
    await screen.findByText(/Installed beta 0\.1\.0/);
    expect(mockInstall).toHaveBeenCalledWith('beta', '0.1.0', false);
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
});
