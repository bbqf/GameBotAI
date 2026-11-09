import { scheduleSummary } from '../scheduleSummary';

describe('scheduleSummary', () => {
  it('gives the fixed text of each schedule type', () => {
    expect(scheduleSummary({ scheduleType: 'OncePerRun' })).toBe('Once each run');
    expect(scheduleSummary({ scheduleType: 'EveryStep' })).toBe('Every step');
    expect(scheduleSummary({ scheduleType: 'AtQueueStart' })).toBe('At queue start');
    expect(scheduleSummary({ scheduleType: 'BeforeEachRun' })).toBe('Before each run');
  });

  it('gives not scheduled for an unknown type', () => {
    expect(scheduleSummary({ scheduleType: undefined })).toBe('not scheduled');
  });

  it('shows the time of day of a Timer entry', () => {
    expect(scheduleSummary({ scheduleType: 'Timer', timerTimeOfDay: '14:30:00' })).toBe('Daily at 14:30');
    expect(scheduleSummary({ scheduleType: 'Timer', timerTimeOfDay: '14:30' })).toBe('Daily at 14:30');
  });

  it('shows the relative offset of a Timer entry', () => {
    expect(scheduleSummary({ scheduleType: 'Timer', timerRelativeOffset: '01:00:00' })).toBe('Every 01:00:00');
  });

  it('gives not scheduled for a Timer entry with no value', () => {
    expect(scheduleSummary({ scheduleType: 'Timer' })).toBe('not scheduled');
    expect(scheduleSummary({ scheduleType: 'Timer', timerTimeOfDay: '', timerRelativeOffset: '' })).toBe('not scheduled');
  });

  it('lets the offset win in relative mode, even when an old time of day stays', () => {
    expect(scheduleSummary({
      scheduleType: 'Timer', timerMode: 'relative', timerTimeOfDay: '08:00', timerRelativeOffset: '00:10:00'
    })).toBe('Every 00:10:00');
  });

  it('lets the time win in time-of-day mode, even when an old offset stays', () => {
    expect(scheduleSummary({
      scheduleType: 'Timer', timerMode: 'timeOfDay', timerTimeOfDay: '08:00', timerRelativeOffset: '00:10:00'
    })).toBe('Daily at 08:00');
  });

  it('gives not scheduled when the field of the mode is empty', () => {
    expect(scheduleSummary({
      scheduleType: 'Timer', timerMode: 'relative', timerTimeOfDay: '08:00', timerRelativeOffset: ''
    })).toBe('not scheduled');
    expect(scheduleSummary({
      scheduleType: 'Timer', timerMode: 'timeOfDay', timerTimeOfDay: '', timerRelativeOffset: '00:10:00'
    })).toBe('not scheduled');
  });
});
