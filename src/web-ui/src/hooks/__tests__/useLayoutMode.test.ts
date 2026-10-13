import { act, renderHook } from '@testing-library/react';
import { PHONE_MEDIA_QUERY, useLayoutMode } from '../useLayoutMode';
import { mockViewport, ViewportMock } from '../../testUtils/viewport';

describe('useLayoutMode', () => {
  let mock: ViewportMock;
  afterEach(() => mock?.restore());

  it('uses the documented media query', () => {
    expect(PHONE_MEDIA_QUERY).toBe('(max-width: 768px), (max-height: 500px)');
  });

  it('gives phone at width 768', () => {
    mock = mockViewport(768, 900);
    expect(renderHook(() => useLayoutMode()).result.current).toBe('phone');
  });

  it('gives desktop at width 769', () => {
    mock = mockViewport(769, 900);
    expect(renderHook(() => useLayoutMode()).result.current).toBe('desktop');
  });

  it('gives phone at height 500', () => {
    mock = mockViewport(900, 500);
    expect(renderHook(() => useLayoutMode()).result.current).toBe('phone');
  });

  it('updates on a change event', () => {
    mock = mockViewport(1280, 800);
    const { result } = renderHook(() => useLayoutMode());
    expect(result.current).toBe('desktop');
    act(() => mock.setViewport(360, 640));
    expect(result.current).toBe('phone');
    act(() => mock.setViewport(1280, 800));
    expect(result.current).toBe('desktop');
  });
});
