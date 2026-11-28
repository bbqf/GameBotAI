import React from 'react';
import { act, render, renderHook, screen } from '@testing-library/react';
import { ApiError } from '../../../lib/api';
import { useDeleteFlow } from '../useDeleteFlow';
import { DeleteReferences } from '../DeleteReferences';

const setup = (remove: (id: string) => Promise<void>) => {
  const onDeleted = jest.fn();
  const onError = jest.fn();
  const hook = renderHook(() => useDeleteFlow({ kind: 'game', remove, onDeleted, onError }));
  return { ...hook, onDeleted, onError };
};

describe('useDeleteFlow', () => {
  it('open sets the target', () => {
    const { result } = setup(jest.fn());
    act(() => result.current.open({ id: 'g1', name: 'Alpha' }));
    expect(result.current.target).toEqual({ id: 'g1', name: 'Alpha' });
  });

  it('confirm calls remove once and onDeleted, then closes', async () => {
    const remove = jest.fn().mockResolvedValue(undefined);
    const { result, onDeleted } = setup(remove);
    act(() => result.current.open({ id: 'g1', name: 'Alpha' }));
    await act(async () => { await result.current.confirm(); });
    expect(remove).toHaveBeenCalledTimes(1);
    expect(remove).toHaveBeenCalledWith('g1');
    expect(onDeleted).toHaveBeenCalledWith({ id: 'g1', name: 'Alpha' });
    expect(result.current.target).toBeUndefined();
  });

  it('ignores a second confirm while busy', async () => {
    let release: () => void = () => undefined;
    const remove = jest.fn().mockImplementation(() => new Promise<void>((r) => { release = r; }));
    const { result } = setup(remove);
    act(() => result.current.open({ id: 'g1', name: 'Alpha' }));
    let first: Promise<void> = Promise.resolve();
    act(() => { first = result.current.confirm(); });
    expect(result.current.busy).toBe(true);
    await act(async () => { await result.current.confirm(); });
    expect(remove).toHaveBeenCalledTimes(1);
    await act(async () => { release(); await first; });
    expect(result.current.busy).toBe(false);
  });

  it('keeps the target and sets the conflict for a 409', async () => {
    const references = { sequences: [{ id: 's1', name: 'Seq' }] };
    const remove = jest.fn().mockRejectedValue(new ApiError(409, 'In use', undefined, undefined, references));
    const { result, onDeleted, onError } = setup(remove);
    act(() => result.current.open({ id: 'g1', name: 'Alpha' }));
    await act(async () => { await result.current.confirm(); });
    expect(result.current.target).toBeDefined();
    expect(result.current.conflict).toEqual({ message: 'In use', references });
    expect(onDeleted).not.toHaveBeenCalled();
    expect(onError).not.toHaveBeenCalled();
  });

  it('uses a default message for a 409 with an empty message', async () => {
    const remove = jest.fn().mockRejectedValue(new ApiError(409, ''));
    const { result } = setup(remove);
    act(() => result.current.open({ id: 'g1', name: 'Alpha' }));
    await act(async () => { await result.current.confirm(); });
    expect(result.current.conflict?.message).toBe('Cannot delete: the game is in use. Remove the links first.');
  });

  it('uses the default 409 text for a sequence', async () => {
    const remove = jest.fn().mockRejectedValue(new ApiError(409, ''));
    const hook = renderHook(() => useDeleteFlow({ kind: 'sequence', remove, onDeleted: jest.fn(), onError: jest.fn() }));
    act(() => hook.result.current.open({ id: 'q1', name: 'Seq' }));
    await act(async () => { await hook.result.current.confirm(); });
    expect(hook.result.current.conflict?.message).toBe('Cannot delete: the sequence is in use. Remove the links first.');
  });

  it('uses the default error text for a sequence', async () => {
    const remove = jest.fn().mockRejectedValue({});
    const onError = jest.fn();
    const hook = renderHook(() => useDeleteFlow({ kind: 'sequence', remove, onDeleted: jest.fn(), onError }));
    act(() => hook.result.current.open({ id: 'q1', name: 'Seq' }));
    await act(async () => { await hook.result.current.confirm(); });
    expect(onError).toHaveBeenCalledWith('Failed to delete sequence');
  });

  it('closes and calls onError for another error', async () => {
    const remove = jest.fn().mockRejectedValue(new Error('boom'));
    const { result, onError } = setup(remove);
    act(() => result.current.open({ id: 'g1', name: 'Alpha' }));
    await act(async () => { await result.current.confirm(); });
    expect(result.current.target).toBeUndefined();
    expect(onError).toHaveBeenCalledWith('boom');
  });

  it('cancel clears the target and the conflict', async () => {
    const remove = jest.fn().mockRejectedValue(new ApiError(409, 'In use'));
    const { result } = setup(remove);
    act(() => result.current.open({ id: 'g1', name: 'Alpha' }));
    await act(async () => { await result.current.confirm(); });
    act(() => result.current.cancel());
    expect(result.current.target).toBeUndefined();
    expect(result.current.conflict).toBeUndefined();
  });
});

describe('DeleteReferences', () => {
  it('shows the message, the groups, and the names', () => {
    render(<DeleteReferences conflict={{ message: 'In use', references: { sequences: [{ id: 's1', name: 'Seq One' }, { id: 's2', name: 'Seq Two' }] } }} />);
    expect(screen.getByRole('alert')).toHaveTextContent('In use');
    expect(screen.getByText('Referenced by:')).toBeInTheDocument();
    expect(screen.getByText('sequences (2)')).toBeInTheDocument();
    expect(screen.getByText(/Seq One/)).toBeInTheDocument();
    expect(screen.getByText(/Seq Two/)).toBeInTheDocument();
  });

  it('shows only the message when there are no references', () => {
    render(<DeleteReferences conflict={{ message: 'In use' }} />);
    expect(screen.queryByText('Referenced by:')).toBeNull();
  });
});
