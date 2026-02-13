import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { listGames } from '../games';
import { baseUrl$ } from '../../lib/config';
import { useGames } from '../useGames';

jest.mock('../games', () => ({
  listGames: jest.fn()
}));

jest.mock('../../lib/config', () => ({
  baseUrl$: {
    get: jest.fn(() => 'http://base'),
    subscribe: jest.fn(() => jest.fn())
  }
}));

const mockListGames = listGames as jest.MockedFunction<typeof listGames>;
const mockBaseUrlGet = baseUrl$.get as jest.MockedFunction<typeof baseUrl$.get>;

const TestHarness: React.FC = () => {
  const state = useGames();
  return (
    <div>
      <div data-testid="loading">{state.loading ? 'true' : 'false'}</div>
      <div data-testid="count">{state.data?.length ?? 0}</div>
      <div data-testid="error">{state.error ?? ''}</div>
    </div>
  );
};

describe('useGames', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('loads games when not cached', async () => {
    mockBaseUrlGet.mockReturnValue('base-1');
    mockListGames.mockResolvedValue([{ id: 'g1', name: 'Game 1' }] as any);

    render(<TestHarness />);

    await waitFor(() => expect(screen.getByTestId('loading')).toHaveTextContent('false'));
    expect(screen.getByTestId('count')).toHaveTextContent('1');
  });

  it('returns error when loading fails', async () => {
    mockBaseUrlGet.mockReturnValue('base-2');
    mockListGames.mockRejectedValue(new Error('no games'));

    render(<TestHarness />);

    await waitFor(() => expect(screen.getByTestId('error')).toHaveTextContent('no games'));
    expect(screen.getByTestId('loading')).toHaveTextContent('false');
  });

  it('uses cached data on subsequent renders', async () => {
    mockBaseUrlGet.mockReturnValue('cache');
    mockListGames.mockResolvedValue([{ id: 'g1', name: 'Game 1' }] as any);

    const { unmount } = render(<TestHarness />);

    await waitFor(() => expect(screen.getByTestId('loading')).toHaveTextContent('false'));
    expect(mockListGames).toHaveBeenCalledTimes(1);

    unmount();

    render(<TestHarness />);

    await waitFor(() => expect(screen.getByTestId('loading')).toHaveTextContent('false'));
    expect(mockListGames).toHaveBeenCalledTimes(1);
    expect(baseUrl$.get).toHaveBeenCalled();
  });
});
