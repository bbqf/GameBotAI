import { setTemplateEntryParameters } from '../queueEntryParameters';
import { toggleTemplateEntry } from '../queueEntryToggle';
import type { QueueTemplateDetail } from '../../services/queueTemplates';

const template: QueueTemplateDetail = {
  id: 't1',
  name: 'Daily',
  entryCount: 3,
  createdAt: null,
  updatedAt: null,
  entries: [
    { entryId: 'e1', sequenceId: 's1', sequenceName: 'One', stale: false, scheduleType: 'OncePerRun', timerTimeOfDay: null, timerRelativeOffset: null, enabled: true, parameterValues: [{ name: 'a', value: '1' }] },
    { entryId: 'e2', sequenceId: 's2', sequenceName: 'Two', stale: false, scheduleType: 'Timer', timerTimeOfDay: '03:30:00', timerRelativeOffset: null, enabled: false },
    { entryId: 'e3', sequenceId: 's3', sequenceName: 'Three', stale: false, scheduleType: 'Timer', timerTimeOfDay: null, timerRelativeOffset: '00:10:00', enabled: true }
  ]
};

describe('setTemplateEntryParameters', () => {
  it('sets the values of the chosen entry only', () => {
    const save = setTemplateEntryParameters(template, 1, [{ name: 'n', value: '5' }]);

    expect(save.entries[1].parameterValues).toEqual([{ name: 'n', value: '5' }]);
    expect(save.entries[0].parameterValues).toEqual([{ name: 'a', value: '1' }]);
    expect(save.entries[2].parameterValues).toBeUndefined();
  });

  it('keeps the other fields and the order of all entries', () => {
    const save = setTemplateEntryParameters(template, 0, [{ name: 'a', value: '2' }]);

    expect(save.entries.map((e) => e.sequenceId)).toEqual(['s1', 's2', 's3']);
    expect(save.entries.map((e) => e.enabled)).toEqual([true, false, true]);
    expect(save.entries[1]).toMatchObject({ scheduleType: 'Timer', timerTimeOfDay: '03:30:00' });
    expect(save.entries[2]).toMatchObject({ scheduleType: 'Timer', timerRelativeOffset: '00:10:00' });
  });

  it('does not send a binding with a null value', () => {
    const save = setTemplateEntryParameters(template, 0, [{ name: 'a', value: null }, { name: 'b', value: '7' }]);

    expect(save.entries[0].parameterValues).toEqual([{ name: 'b', value: '7' }]);
  });

  it('sends no parameter list when every binding is null', () => {
    const save = setTemplateEntryParameters(template, 0, [{ name: 'a', value: null }]);

    expect(save.entries[0].parameterValues).toBeUndefined();
  });

  it('overwrites the template and keeps each entry ID', () => {
    const save = setTemplateEntryParameters(template, 2, []);

    expect(save.name).toBe('Daily');
    expect(save.overwrite).toBe(true);
    expect(save.entries.map((e) => e.entryId)).toEqual(['e1', 'e2', 'e3']);
  });

  it('does not change the input', () => {
    const before = JSON.stringify(template);
    setTemplateEntryParameters(template, 0, [{ name: 'x', value: '1' }]);
    expect(JSON.stringify(template)).toBe(before);
  });

  it('round-trips: the saved values equal the values that the editor mapper sends (SC-004)', () => {
    const values = [{ name: 'n', value: '5' }, { name: 'img', value: 'a.png' }];
    const save = setTemplateEntryParameters(template, 0, values);
    // The saved template, read back, goes through the same mapper as the editor and the enable switch.
    const readBack: QueueTemplateDetail = {
      ...template,
      entries: save.entries.map((e, i) => ({ ...template.entries[i], ...e, timerTimeOfDay: e.timerTimeOfDay ?? null, timerRelativeOffset: e.timerRelativeOffset ?? null }))
    };
    const again = toggleTemplateEntry(readBack, 1);

    expect(again.entries[0].parameterValues).toEqual(values);
  });
});
