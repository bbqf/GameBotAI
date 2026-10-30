import { toggleTemplateEntry } from '../queueEntryToggle';
import type { QueueTemplateDetail } from '../../services/queueTemplates';

const template: QueueTemplateDetail = {
  id: 't1',
  name: 'Daily',
  entryCount: 4,
  createdAt: null,
  updatedAt: null,
  entries: [
    { sequenceId: 's1', sequenceName: 'One', stale: false, scheduleType: 'OncePerRun', timerTimeOfDay: null, timerRelativeOffset: null, enabled: true },
    { sequenceId: 's2', sequenceName: 'Two', stale: false, scheduleType: 'Timer', timerTimeOfDay: '03:30:00', timerRelativeOffset: null, enabled: true, parameterValues: [{ name: 'a', value: '1' }, { name: 'b', value: null }] },
    { sequenceId: 's3', sequenceName: 'Three', stale: false, scheduleType: 'Timer', timerTimeOfDay: null, timerRelativeOffset: '00:10:00', enabled: false },
    { sequenceId: 's4', sequenceName: 'Four', stale: false, scheduleType: 'EveryStep', timerTimeOfDay: null, timerRelativeOffset: null, enabled: true }
  ]
};

describe('toggleTemplateEntry', () => {
  it('flips only the chosen entry and keeps the order', () => {
    const save = toggleTemplateEntry(template, 0);
    expect(save.entries.map((e) => e.sequenceId)).toEqual(['s1', 's2', 's3', 's4']);
    expect(save.entries.map((e) => e.enabled)).toEqual([false, true, false, true]);
  });

  it('can switch a disabled entry on', () => {
    const save = toggleTemplateEntry(template, 2);
    expect(save.entries.map((e) => e.enabled)).toEqual([true, true, true, true]);
  });

  it('keeps schedule type, timers, and parameter values', () => {
    const save = toggleTemplateEntry(template, 3);
    expect(save.entries[1]).toMatchObject({ scheduleType: 'Timer', timerTimeOfDay: '03:30:00' });
    expect(save.entries[1].timerRelativeOffset).toBeUndefined();
    expect(save.entries[1].parameterValues).toEqual([{ name: 'a', value: '1' }]);
    expect(save.entries[2]).toMatchObject({ scheduleType: 'Timer', timerRelativeOffset: '00:10:00' });
    expect(save.entries[2].timerTimeOfDay).toBeUndefined();
    expect(save.entries[3].scheduleType).toBe('EveryStep');
    expect(save.entries[0].parameterValues).toBeUndefined();
  });

  it('overwrites the template with the same name', () => {
    const save = toggleTemplateEntry(template, 0);
    expect(save.name).toBe('Daily');
    expect(save.overwrite).toBe(true);
  });

  it('does not change the input', () => {
    const before = JSON.stringify(template);
    toggleTemplateEntry(template, 1);
    expect(JSON.stringify(template)).toBe(before);
  });
});

describe('toggleTemplateEntry keeps the entry IDs (feature 171)', () => {
  it('sends back the entryId of each entry that has one, and leaves it out for an entry that has none', () => {
    const template = {
      id: 't1', name: 'Daily', entryCount: 2, createdAt: null, updatedAt: null,
      entries: [
        { entryId: 'e1', sequenceId: 'a', sequenceName: 'A', stale: false, scheduleType: 'OncePerRun', timerTimeOfDay: null, timerRelativeOffset: null, enabled: true },
        { sequenceId: 'b', sequenceName: 'B', stale: false, scheduleType: 'OncePerRun', timerTimeOfDay: null, timerRelativeOffset: null, enabled: true }
      ]
    } as any;

    const request = toggleTemplateEntry(template, 0);

    expect(request.entries[0].entryId).toBe('e1');
    expect(request.entries[0].enabled).toBe(false);
    expect('entryId' in request.entries[1]).toBe(false);
  });
});
