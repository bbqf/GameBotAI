import { formatCaptureRate } from '../pages/Execution';

describe('formatCaptureRate', () => {
  it('shows FPS when rate >= 1', () => {
    expect(formatCaptureRate(2.0)).toBe('2.0 FPS');
    expect(formatCaptureRate(10.5)).toBe('10.5 FPS');
    expect(formatCaptureRate(1.0)).toBe('1.0 FPS');
  });

  it('shows s/frame when rate > 0 and < 1', () => {
    expect(formatCaptureRate(0.5)).toBe('2.0 s/frame');
    expect(formatCaptureRate(0.25)).toBe('4.0 s/frame');
  });

  it('shows dash when null, undefined, or zero', () => {
    expect(formatCaptureRate(null)).toBe('—');
    expect(formatCaptureRate(undefined)).toBe('—');
    expect(formatCaptureRate(0)).toBe('—');
  });

  it('shows dash for negative values', () => {
    expect(formatCaptureRate(-1)).toBe('—');
  });
});
