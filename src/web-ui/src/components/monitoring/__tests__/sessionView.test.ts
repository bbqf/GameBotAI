import { deviceKind, sessionChip, startedText } from '../sessionView';

describe('sessionView', () => {
  it.each([
    ['live', 'done', 'Live'],
    ['no_answer', 'failed', 'No answer'],
    ['restarting', 'paused', 'Restarting'],
    ['stopping', 'stopped', 'Stopping']
  ])('maps the state %s to a chip', (state, chip, text) => {
    expect(sessionChip(state)).toEqual({ state: chip, text });
  });

  it('shows an unknown state with its own value', () => {
    expect(sessionChip('weird')).toEqual({ state: 'unknown', text: 'weird' });
  });

  it('reads the device kind', () => {
    expect(deviceKind('live')).toBe('live');
    expect(deviceKind('not_live')).toBe('not_live');
    expect(deviceKind(null)).toBe('none');
    expect(deviceKind(undefined)).toBe('none');
  });

  it('formats a valid date and shows a dash for an invalid one', () => {
    expect(startedText('2026-11-27T10:30:00Z', 'en-GB')).toMatch(/^\d{2}\/\d{2}\/\d{4}, \d{2}:\d{2}$/);
    expect(startedText('not a date', 'en-GB')).toBe('—');
    expect(startedText(null)).toBe('—');
  });
});
