import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { useAdbDevices } from '../useAdbDevices';
import { listAdbDevices } from '../adbApi';

jest.mock('../adbApi', () => ({
  listAdbDevices: jest.fn()
}));

const mockListAdbDevices = listAdbDevices as jest.MockedFunction<typeof listAdbDevices>;

const TestHarness: React.FC<{ enabled: boolean }> = ({ enabled }) => {
  const { loading, devices, error, refresh } = useAdbDevices(enabled);

  return (
    <div>
      <div data-testid="loading">{loading ? 'true' : 'false'}</div>
      <div data-testid="count">{devices.length}</div>
      <div data-testid="error">{error ?? ''}</div>
      <button type="button" onClick={refresh}>Refresh</button>
    </div>
  );
};

describe('useAdbDevices', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('skips loading when disabled', () => {
    render(<TestHarness enabled={false} />);

    expect(screen.getByTestId('loading')).toHaveTextContent('false');
    expect(screen.getByTestId('count')).toHaveTextContent('0');
    expect(screen.getByTestId('error')).toHaveTextContent('');
  });

  it('loads devices when enabled', async () => {
    mockListAdbDevices.mockResolvedValue([{ serial: 'emu-1' }] as any);

    render(<TestHarness enabled={true} />);

    await waitFor(() => expect(screen.getByTestId('count')).toHaveTextContent('1'));
    expect(screen.getByTestId('loading')).toHaveTextContent('false');
  });

  it('reports errors when device load fails', async () => {
    mockListAdbDevices.mockRejectedValue(new Error('no devices'));

    render(<TestHarness enabled={true} />);

    await waitFor(() => expect(screen.getByTestId('error')).toHaveTextContent('no devices'));
    expect(screen.getByTestId('count')).toHaveTextContent('0');
  });
});
