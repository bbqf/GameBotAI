import { setLeaveGuard, shouldBlockLeave } from '../phoneLeaveGuard';

describe('phoneLeaveGuard (feature 185)', () => {
  afterEach(() => setLeaveGuard(null));

  it('allows a leave when no guard is set', () => {
    expect(shouldBlockLeave()).toBe(false);
  });

  it('blocks a leave when the guard returns true', () => {
    setLeaveGuard(() => true);

    expect(shouldBlockLeave()).toBe(true);
  });

  it('allows a leave when the guard returns false', () => {
    setLeaveGuard(() => false);

    expect(shouldBlockLeave()).toBe(false);
  });

  it('allows a leave again after the guard is removed', () => {
    setLeaveGuard(() => true);
    setLeaveGuard(null);

    expect(shouldBlockLeave()).toBe(false);
  });

  it('replaces the first guard with the second one', () => {
    const first = jest.fn(() => true);
    const second = jest.fn(() => false);
    setLeaveGuard(first);
    setLeaveGuard(second);

    expect(shouldBlockLeave()).toBe(false);
    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledTimes(1);
  });
});
