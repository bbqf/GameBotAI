import React, { useEffect } from 'react';
import { act, render, waitFor } from '@testing-library/react';
import { useActionTypes } from '../useActionTypes';
import { setBaseUrl } from '../../lib/config';

describe('useActionTypes', () => {
  const originalFetch = global.fetch;

  afterEach(() => {
    global.fetch = originalFetch as any;
    act(() => setBaseUrl(''));
  });

  it('loads action types and exposes data', async () => {
    const catalog = {
      version: 'v1',
      items: [
        { key: 'tap', displayName: 'Tap', attributeDefinitions: [] }
      ]
    };

    const fetchMock = jest.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => catalog,
      headers: { get: () => '"etag-1"' }
    } as unknown as Response);
    global.fetch = fetchMock as any;

    const states: any[] = [];
    const Harness: React.FC = () => {
      const state = useActionTypes();
      useEffect(() => { states.push(state); }, [state]);
      return null;
    };

    render(<Harness />);

    await waitFor(() => {
      expect(states.at(-1)?.loading).toBe(false);
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(states.at(-1)?.data?.items[0].key).toBe('tap');
  });

  it('surfaces errors when fetch fails', async () => {
    const fetchMock = jest.fn().mockRejectedValue(new Error('boom'));
    global.fetch = fetchMock as any;

    const states: any[] = [];
    const Harness: React.FC = () => {
      const state = useActionTypes();
      useEffect(() => { states.push(state); }, [state]);
      return null;
    };

    // Use a unique base URL to avoid cache from previous test
    act(() => setBaseUrl('http://example'));
    render(<Harness />);

    await waitFor(() => {
      expect(states.at(-1)?.loading).toBe(false);
    });

    expect(states.at(-1)?.error).toBeDefined();
  });
});
