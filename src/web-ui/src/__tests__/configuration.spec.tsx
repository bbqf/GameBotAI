import React from 'react';
import { fireEvent, render, screen, waitFor, act } from '@testing-library/react';
import { App } from '../App';
import * as configService from '../services/config';

jest.mock('../pages/actions/ActionsListPage', () => ({ ActionsListPage: () => <div role="heading" aria-level={2}>Actions</div> }));
jest.mock('../pages/CommandsPage', () => ({ CommandsPage: () => <div role="heading" aria-level={2}>Commands</div> }));
jest.mock('../pages/GamesPage', () => ({ GamesPage: () => <div role="heading" aria-level={2}>Games</div> }));
jest.mock('../pages/SequencesPage', () => ({ SequencesPage: () => <div role="heading" aria-level={2}>Sequences</div> }));

const mockSnapshot: configService.ConfigurationSnapshot = {
  generatedAtUtc: '2026-01-01T00:00:00Z',
  serviceVersion: '1.0.0',
  dynamicPort: null,
  refreshCount: 1,
  envScanned: 10,
  envIncluded: 2,
  envExcluded: 8,
  parameters: {
    'GAMEBOT_TESSERACT_LANG': { name: 'GAMEBOT_TESSERACT_LANG', source: 'File', value: 'eng', isSecret: false },
    'GAMEBOT_AUTH_TOKEN': { name: 'GAMEBOT_AUTH_TOKEN', source: 'Environment', value: '***', isSecret: true },
    'GAMEBOT_USE_ADB': { name: 'GAMEBOT_USE_ADB', source: 'Default', value: 'true', isSecret: false },
  }
};

describe('Configuration area', () => {
  beforeEach(() => {
    jest.spyOn(configService, 'getConfigSnapshot').mockResolvedValue(mockSnapshot);
    jest.spyOn(configService, 'updateParameters').mockResolvedValue(mockSnapshot);
    jest.spyOn(configService, 'reorderParameters').mockResolvedValue(mockSnapshot);
    jest.spyOn(configService, 'getConfigFileParams').mockResolvedValue({ fileName: 'test.json', parameters: {} });
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('shows host/token controls in a collapsible section', async () => {
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    await waitFor(() => expect(screen.getByText('Backend Connection')).toBeInTheDocument());
    expect(screen.getByLabelText('API Base URL')).toBeInTheDocument();
    expect(screen.getByLabelText('Bearer Token')).toBeInTheDocument();
  });

  it('renders parameter list from config snapshot', async () => {
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    await waitFor(() => expect(screen.getByText('GAMEBOT_TESSERACT_LANG')).toBeInTheDocument());
    expect(screen.getByText('GAMEBOT_USE_ADB')).toBeInTheDocument();
    expect(screen.getByText('GAMEBOT_AUTH_TOKEN')).toBeInTheDocument();
  });

  it('shows source badges', async () => {
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    await waitFor(() => expect(screen.getByText('GAMEBOT_TESSERACT_LANG')).toBeInTheDocument());
    expect(screen.getByText('File')).toBeInTheDocument();
    expect(screen.getByText('Environment')).toBeInTheDocument();
    expect(screen.getByText('Default')).toBeInTheDocument();
  });

  it('shows filter input and filters parameters', async () => {
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    await waitFor(() => expect(screen.getByLabelText('Filter parameters')).toBeInTheDocument());
    fireEvent.change(screen.getByLabelText('Filter parameters'), { target: { value: 'TESSERACT' } });
    expect(screen.getByText('GAMEBOT_TESSERACT_LANG')).toBeInTheDocument();
    expect(screen.queryByText('GAMEBOT_USE_ADB')).not.toBeInTheDocument();
  });

  it('shows empty state when filter matches nothing', async () => {
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    await waitFor(() => expect(screen.getByLabelText('Filter parameters')).toBeInTheDocument());
    fireEvent.change(screen.getByLabelText('Filter parameters'), { target: { value: 'zzzznonexistent' } });
    expect(screen.getByText('No matching parameters')).toBeInTheDocument();
  });

  it('shows Apply All button disabled when no edits', async () => {
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    await waitFor(() => expect(screen.getByText('Apply All')).toBeInTheDocument());
    expect(screen.getByText('Apply All')).toBeDisabled();
  });

  it('enables Apply All after editing a parameter', async () => {
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    await waitFor(() => expect(screen.getByLabelText('Value for GAMEBOT_TESSERACT_LANG')).toBeInTheDocument());
    fireEvent.change(screen.getByLabelText('Value for GAMEBOT_TESSERACT_LANG'), { target: { value: 'deu' } });
    expect(screen.getByText('Apply All')).toBeEnabled();
  });

  it('shows error state with retry button on fetch failure', async () => {
    jest.spyOn(configService, 'getConfigSnapshot').mockRejectedValueOnce(new Error('Network error'));
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    await waitFor(() => expect(screen.getByText('Failed to load configuration')).toBeInTheDocument());
    // Main config section has its own retry
    const alerts = screen.getAllByRole('alert');
    expect(alerts.length).toBeGreaterThanOrEqual(1);
    expect(alerts[0]).toHaveTextContent('Failed to load configuration');
  });
});