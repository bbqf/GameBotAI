import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BackupRestorePage } from '../BackupRestorePage';
import { downloadBackup, validateRestore, applyRestore } from '../../services/backup';
import { listCommands } from '../../services/commands';
import { getJson } from '../../lib/api';

jest.mock('../../services/backup');
jest.mock('../../services/commands');
jest.mock('../../lib/api');

const mockDownloadBackup = downloadBackup as jest.MockedFunction<typeof downloadBackup>;
const mockValidateRestore = validateRestore as jest.MockedFunction<typeof validateRestore>;
const mockApplyRestore = applyRestore as jest.MockedFunction<typeof applyRestore>;
const mockListCommands = listCommands as jest.MockedFunction<typeof listCommands>;
const mockGetJson = getJson as jest.MockedFunction<typeof getJson>;

const mockCommands = [
  { id: 'c1', name: 'Attack', steps: [] },
  { id: 'c2', name: 'Defend', steps: [] },
];
const mockSequences = [
  { id: 's1', name: 'Combo A' },
];

const noConflictReport = {
  hasConflicts: false,
  conflictingCommandNames: [],
  conflictingSequenceNames: [],
  conflictingImageIds: [],
  totalCommands: 1,
  totalSequences: 0,
  totalImages: 0,
};

const conflictReport = {
  hasConflicts: true,
  conflictingCommandNames: ['Attack'],
  conflictingSequenceNames: [],
  conflictingImageIds: [],
  totalCommands: 1,
  totalSequences: 0,
  totalImages: 0,
};

describe('BackupRestorePage', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    mockListCommands.mockResolvedValue(mockCommands);
    mockGetJson.mockResolvedValue(mockSequences);
  });

  it('shows loading state initially', () => {
    render(<BackupRestorePage />);
    expect(screen.getByText('Loading...')).toBeInTheDocument();
  });

  it('renders commands and sequences after load', async () => {
    render(<BackupRestorePage />);
    expect(await screen.findByText('Attack')).toBeInTheDocument();
    expect(screen.getByText('Defend')).toBeInTheDocument();
    expect(screen.getByText('Combo A')).toBeInTheDocument();
  });

  it('shows empty state when no commands or sequences', async () => {
    mockListCommands.mockResolvedValue([]);
    mockGetJson.mockResolvedValue([]);
    render(<BackupRestorePage />);
    expect(await screen.findByText(/No commands or sequences exist yet/)).toBeInTheDocument();
  });

  it('Download Backup button is disabled when nothing selected', async () => {
    render(<BackupRestorePage />);
    await screen.findByText('Attack');
    const btn = screen.getByRole('button', { name: 'Download Backup' });
    expect(btn).toBeDisabled();
  });

  it('Download Backup button enables when a command is selected', async () => {
    render(<BackupRestorePage />);
    await screen.findByText('Attack');
    fireEvent.click(screen.getByLabelText(/Attack/));
    const btn = screen.getByRole('button', { name: 'Download Backup' });
    expect(btn).not.toBeDisabled();
  });

  it('Select All selects all commands', async () => {
    render(<BackupRestorePage />);
    await screen.findByText('Attack');
    // First "Select All" button belongs to the commands group
    fireEvent.click(screen.getAllByRole('button', { name: 'Select All' })[0]);
    const attackBox = screen.getByLabelText(/Attack/i) as HTMLInputElement;
    const defendBox = screen.getByLabelText(/Defend/i) as HTMLInputElement;
    expect(attackBox.checked).toBe(true);
    expect(defendBox.checked).toBe(true);
  });

  it('calls downloadBackup with selected ids on Download Backup click', async () => {
    mockDownloadBackup.mockResolvedValue(undefined);
    render(<BackupRestorePage />);
    await screen.findByText('Attack');
    fireEvent.click(screen.getByLabelText(/Attack/i));
    fireEvent.click(screen.getByRole('button', { name: 'Download Backup' }));
    await waitFor(() => expect(mockDownloadBackup).toHaveBeenCalledWith(
      expect.objectContaining({ commandIds: ['c1'], sequenceIds: [] })
    ));
  });

  it('calls validateRestore on file upload and shows conflict dialog', async () => {
    mockValidateRestore.mockResolvedValue(noConflictReport);
    render(<BackupRestorePage />);
    await screen.findByText(/Upload & Check Archive/);

    const fileInput = screen.getByTestId('restore-file-input');
    const file = new File(['dummy'], 'backup.zip', { type: 'application/zip' });
    fireEvent.change(fileInput, { target: { files: [file] } });

    expect(await screen.findByText('Restore Confirmation')).toBeInTheDocument();
    expect(screen.getByText('No conflicts detected.')).toBeInTheDocument();
  });

  it('shows conflict names in conflict dialog', async () => {
    mockValidateRestore.mockResolvedValue(conflictReport);
    render(<BackupRestorePage />);
    await screen.findByText(/Upload & Check Archive/);

    const fileInput = screen.getByTestId('restore-file-input');
    const file = new File(['dummy'], 'backup.zip', { type: 'application/zip' });
    fireEvent.change(fileInput, { target: { files: [file] } });

    expect(await screen.findByText('Attack')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Confirm Overwrite' })).toBeInTheDocument();
  });

  it('Cancel in conflict dialog returns to idle', async () => {
    mockValidateRestore.mockResolvedValue(noConflictReport);
    render(<BackupRestorePage />);
    await screen.findByText(/Upload & Check Archive/);

    const fileInput = screen.getByTestId('restore-file-input');
    fireEvent.change(fileInput, { target: { files: [new File(['x'], 'b.zip')] } });
    await screen.findByText('Restore Confirmation');

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByText('Restore Confirmation')).not.toBeInTheDocument();
    expect(screen.getByText(/Upload & Check Archive/)).toBeInTheDocument();
  });

  it('Confirm Restore calls applyRestore and shows success state', async () => {
    mockValidateRestore.mockResolvedValue(noConflictReport);
    mockApplyRestore.mockResolvedValue({ restoredCommands: 1, restoredSequences: 0, restoredImages: 0, rolledBack: false });
    render(<BackupRestorePage />);
    await screen.findByText(/Upload & Check Archive/);

    const fileInput = screen.getByTestId('restore-file-input');
    fireEvent.change(fileInput, { target: { files: [new File(['x'], 'b.zip')] } });
    await screen.findByText('Restore Confirmation');

    fireEvent.click(screen.getByRole('button', { name: 'Confirm Restore' }));
    expect(await screen.findByText(/Restore complete/)).toBeInTheDocument();
  });

  it('rolledBack result shows error state', async () => {
    mockValidateRestore.mockResolvedValue(noConflictReport);
    mockApplyRestore.mockResolvedValue({ restoredCommands: 0, restoredSequences: 0, restoredImages: 0, rolledBack: true, errorMessage: 'Storage failure' });
    render(<BackupRestorePage />);
    await screen.findByText(/Upload & Check Archive/);

    const fileInput = screen.getByTestId('restore-file-input');
    fireEvent.change(fileInput, { target: { files: [new File(['x'], 'b.zip')] } });
    await screen.findByText('Restore Confirmation');

    fireEvent.click(screen.getByRole('button', { name: 'Confirm Restore' }));
    expect(await screen.findByText(/Storage failure/)).toBeInTheDocument();
  });
});
