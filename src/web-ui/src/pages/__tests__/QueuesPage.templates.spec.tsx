import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueuesPage } from '../QueuesPage';
import { listQueues, getQueue, updateQueue, replaceQueueEntries, addQueueEntry } from '../../services/queues';
import { listSequences } from '../../services/sequences';
import { saveQueueTemplate, getQueueTemplate, listQueueTemplates, deleteQueueTemplate } from '../../services/queueTemplates';
import { ApiError } from '../../lib/api';

jest.mock('../../services/queues');
jest.mock('../../services/sequences');
jest.mock('../../services/queueTemplates');
jest.mock('../../services/useAdbDevices', () => ({
  useAdbDevices: () => ({ devices: [{ serial: 'emu-1' }], loading: false, error: undefined, refresh: () => {} }),
}));

const listQueuesMock = listQueues as jest.MockedFunction<typeof listQueues>;
const getQueueMock = getQueue as jest.MockedFunction<typeof getQueue>;
const updateQueueMock = updateQueue as jest.MockedFunction<typeof updateQueue>;
const replaceEntriesMock = replaceQueueEntries as jest.MockedFunction<typeof replaceQueueEntries>;
const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;
const saveTemplateMock = saveQueueTemplate as jest.MockedFunction<typeof saveQueueTemplate>;
const getTemplateMock = getQueueTemplate as jest.MockedFunction<typeof getQueueTemplate>;
const listTemplatesMock = listQueueTemplates as jest.MockedFunction<typeof listQueueTemplates>;
const deleteTemplateMock = deleteQueueTemplate as jest.MockedFunction<typeof deleteQueueTemplate>;

const queue = (over: Partial<any> = {}) => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false, status: 'Stopped', entryCount: 2, ...over,
});

const detailWithEntries = () => ({
  ...queue(),
  entries: [
    { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'A', stale: false },
    { entryId: 'e2', sequenceId: 'seq-b', sequenceName: 'B', stale: false },
  ],
});

const templateDetail = () => ({
  id: 't1', name: 'Daily Farm', entryCount: 1, createdAt: null, updatedAt: null,
  entries: [{ sequenceId: 'seq-x', sequenceName: 'X', stale: false, scheduleType: 'OncePerRun', timerTimeOfDay: null }],
});

beforeEach(() => {
  jest.resetAllMocks();
  listQueuesMock.mockResolvedValue([queue()] as any);
  listSequencesMock.mockResolvedValue([] as any);
  getQueueMock.mockResolvedValue(detailWithEntries() as any);
  listTemplatesMock.mockResolvedValue([{ id: 't1', name: 'Daily Farm', entryCount: 1, createdAt: null, updatedAt: null }] as any);
  getTemplateMock.mockResolvedValue(templateDetail() as any);
  replaceEntriesMock.mockResolvedValue({} as any);
  updateQueueMock.mockResolvedValue({} as any);
});

const openEditQueue = async () => {
  render(<QueuesPage />);
  await screen.findByText('Daily');
  fireEvent.click(screen.getByText('Daily'));
  await screen.findByText('Edit Queue');
};

/** Opens the manage area (load list + editable template name) via the template name button. */
const openManageArea = async (label = '(no template)') => {
  fireEvent.click(screen.getByText(label));
  return screen.findByRole('region', { name: 'Load template' });
};

/** With the editor already open, sets the template name in the manage area and clicks Rename. */
const saveTemplateAs = async (name: string, label = '(no template)') => {
  const area = await openManageArea(label);
  fireEvent.change(within(area).getByLabelText('Template name'), { target: { value: name } });
  fireEvent.click(within(area).getByText('Rename'));
};

/** Opens the edit queue, then saves the template under a typed name via Rename. */
const openEditAndSaveAs = async (name: string, label = '(no template)') => {
  await openEditQueue();
  await saveTemplateAs(name, label);
};

describe('QueuesPage template save wiring', () => {
  it('builds sequenceIds from the current entries and saves (via Rename)', async () => {
    saveTemplateMock.mockResolvedValue({} as any);
    await openEditAndSaveAs('Daily Farm');

    await waitFor(() =>
      expect(saveTemplateMock).toHaveBeenCalledWith({
        name: 'Daily Farm',
        entries: [
          { sequenceId: 'seq-a', scheduleType: 'OncePerRun' },
          { sequenceId: 'seq-b', scheduleType: 'OncePerRun' },
        ],
        overwrite: false,
      })
    );
    // The confirmation appears at the template controls, not via the page-top status banner.
    const controls = screen.getByRole('region', { name: 'Queue templates' });
    expect(await within(controls).findByText('Template "Daily Farm" saved successfully.')).toBeInTheDocument();
  });

  it('saves under a brand-new name in one action via Rename (no overwrite prompt)', async () => {
    saveTemplateMock.mockResolvedValue({} as any);
    await openEditAndSaveAs('Fresh Name');

    await waitFor(() =>
      expect(saveTemplateMock).toHaveBeenCalledWith({
        name: 'Fresh Name',
        entries: [
          { sequenceId: 'seq-a', scheduleType: 'OncePerRun' },
          { sequenceId: 'seq-b', scheduleType: 'OncePerRun' },
        ],
        overwrite: false,
      })
    );
    expect(screen.queryByRole('region', { name: 'Confirm overwrite' })).not.toBeInTheDocument();
  });

  it('Save Template (bottom) re-saves the associated template in one click under the old name', async () => {
    // The queue is already linked to "Daily Farm"; clicking Save Template (bottom) without opening
    // the area overwrites the associated template directly.
    getQueueMock.mockResolvedValue({
      ...detailWithEntries(),
      linkedTemplateId: 't1',
      linkedTemplateName: 'Daily Farm',
    } as any);
    saveTemplateMock.mockResolvedValue({} as any);
    await openEditQueue();
    expect(screen.getByText('Daily Farm')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Save Template'));

    await waitFor(() =>
      expect(saveTemplateMock).toHaveBeenCalledWith({
        name: 'Daily Farm',
        entries: [
          { sequenceId: 'seq-a', scheduleType: 'OncePerRun' },
          { sequenceId: 'seq-b', scheduleType: 'OncePerRun' },
        ],
        overwrite: true,
      })
    );
    expect(screen.queryByRole('region', { name: 'Confirm overwrite' })).not.toBeInTheDocument();
  });

  it('Save Template (bottom) saves under the OLD name even after the field was edited without Rename', async () => {
    getQueueMock.mockResolvedValue({
      ...detailWithEntries(),
      linkedTemplateId: 't1',
      linkedTemplateName: 'Daily Farm',
    } as any);
    saveTemplateMock.mockResolvedValue({} as any);
    await openEditQueue();

    // Edit the name in the manage area but do NOT click Rename, then use the bottom Save Template.
    const area = await openManageArea('Daily Farm');
    fireEvent.change(within(area).getByLabelText('Template name'), { target: { value: 'Renamed Farm' } });
    fireEvent.click(screen.getByText('Save Template'));

    await waitFor(() =>
      expect(saveTemplateMock).toHaveBeenCalledWith({
        name: 'Daily Farm',
        entries: [
          { sequenceId: 'seq-a', scheduleType: 'OncePerRun' },
          { sequenceId: 'seq-b', scheduleType: 'OncePerRun' },
        ],
        overwrite: true,
      })
    );
  });

  it('disables Save Template (bottom) when the queue has no associated template', async () => {
    await openEditQueue();
    expect(screen.getByText('Save Template')).toBeDisabled();
  });

  it('on a differing name that collides (409) prompts to overwrite (near Rename) and re-saves with overwrite=true', async () => {
    saveTemplateMock
      .mockRejectedValueOnce(new ApiError(409, 'template_exists'))
      .mockResolvedValueOnce({} as any);
    // No linked template (associated name undefined): a typed name always takes the differing path.
    await openEditAndSaveAs('Daily Farm');

    const confirm = await screen.findByRole('region', { name: 'Confirm overwrite' });
    fireEvent.click(within(confirm).getByText('Overwrite'));

    await waitFor(() =>
      expect(saveTemplateMock).toHaveBeenLastCalledWith({
        name: 'Daily Farm',
        entries: [
          { sequenceId: 'seq-a', scheduleType: 'OncePerRun' },
          { sequenceId: 'seq-b', scheduleType: 'OncePerRun' },
        ],
        overwrite: true,
      })
    );
  });
});

describe('QueuesPage save confirmations are co-located (US2)', () => {
  const openEditForm = async () => {
    render(<QueuesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByText('Daily'));
    await screen.findByText('Edit Queue');
    return screen.getByRole('form', { name: 'Edit queue form' });
  };

  it('shows the queue-save success confirmation at the queue form, not the page-top banner', async () => {
    updateQueueMock.mockResolvedValue({} as any);
    const form = await openEditForm();
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Daily Renamed' } });
    fireEvent.click(within(form).getByText('Save'));

    await waitFor(() => expect(updateQueueMock).toHaveBeenCalled());
    expect(await within(form).findByText('Queue updated successfully.')).toBeInTheDocument();
    // No duplicate page-top banner for this save.
    expect(screen.getAllByText('Queue updated successfully.')).toHaveLength(1);
  });

  it('shows a queue-save error at the queue form when the save fails', async () => {
    updateQueueMock.mockRejectedValue(new Error('Update failed'));
    const form = await openEditForm();
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Daily Renamed' } });
    fireEvent.click(within(form).getByText('Save'));

    expect(await within(form).findByText('Update failed')).toBeInTheDocument();
  });

  it('shows a template-save success confirmation at the template controls', async () => {
    saveTemplateMock.mockResolvedValue({} as any);
    await openEditAndSaveAs('Fresh One');

    const controls = screen.getByRole('region', { name: 'Queue templates' });
    expect(await within(controls).findByText('Template "Fresh One" saved successfully.')).toBeInTheDocument();
    // Not surfaced via the page-top banner.
    expect(screen.getAllByText('Template "Fresh One" saved successfully.')).toHaveLength(1);
  });
});

const openLoadSection = async () => {
  render(<QueuesPage />);
  await screen.findByText('Daily');
  fireEvent.click(screen.getByText('Daily'));
  await screen.findByText('Edit Queue');
  fireEvent.click(screen.getByText('(no template)'));
  return screen.findByRole('region', { name: 'Load template' });
};

describe('QueuesPage template load wiring', () => {
  it('replaces entries after confirming the replacement for a non-empty queue', async () => {
    const picker = await openLoadSection();
    fireEvent.click(within(picker).getByText('Load'));

    const confirm = await screen.findByRole('region', { name: 'Replace queue entries' });
    fireEvent.click(within(confirm).getByRole('button', { name: 'Replace' }));

    await waitFor(() => expect(replaceEntriesMock).toHaveBeenCalledWith('q1', ['seq-x']));
  });

  it('does not replace entries when the replacement is canceled', async () => {
    const picker = await openLoadSection();
    fireEvent.click(within(picker).getByText('Load'));

    const confirm = await screen.findByRole('region', { name: 'Replace queue entries' });
    fireEvent.click(within(confirm).getByRole('button', { name: 'Cancel' }));

    expect(replaceEntriesMock).not.toHaveBeenCalled();
  });

  it('pre-fills the editable name with the loaded template name', async () => {
    getQueueMock.mockResolvedValue({ ...queue({ entryCount: 0 }), entries: [] } as any);
    const picker = await openLoadSection();
    fireEvent.click(within(picker).getByText('Load'));
    await waitFor(() => expect(replaceEntriesMock).toHaveBeenCalledWith('q1', ['seq-x']));

    // The name button now shows the loaded template; reopening the area shows it pre-filled.
    const area = await openManageArea('Daily Farm');
    expect(within(area).getByLabelText('Template name')).toHaveValue('Daily Farm');
  });

  it('disables the Load action while the queue is running', async () => {
    getQueueMock.mockResolvedValue({ ...detailWithEntries(), status: 'Running' } as any);
    render(<QueuesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByText('Daily'));
    await screen.findByText('Edit Queue');
    fireEvent.click(screen.getByText('(no template)'));
    const picker = await screen.findByRole('region', { name: 'Load template' });
    expect(within(picker).getByText('Load')).toBeDisabled();
  });

  it('loads the same template independently into two different queues (FR-016)', async () => {
    listQueuesMock.mockResolvedValue([queue({ id: 'q1', name: 'Daily' }), queue({ id: 'q2', name: 'Arena' })] as any);
    getQueueMock.mockImplementation((id: string) => Promise.resolve({ ...queue({ id, entryCount: 0 }), entries: [] } as any));

    render(<QueuesPage />);
    await screen.findByText('Daily');

    fireEvent.click(screen.getByText('Daily'));
    await screen.findByText('Edit Queue');
    fireEvent.click(screen.getByText('(no template)'));
    fireEvent.click(within(await screen.findByRole('region', { name: 'Load template' })).getByText('Load'));
    await waitFor(() => expect(replaceEntriesMock).toHaveBeenCalledWith('q1', ['seq-x']));

    fireEvent.click(screen.getByText('Arena'));
    await waitFor(() => expect(getQueueMock).toHaveBeenLastCalledWith('q2'));
    fireEvent.click(screen.getByText('(no template)'));
    fireEvent.click(within(await screen.findByRole('region', { name: 'Load template' })).getByText('Load'));
    await waitFor(() => expect(replaceEntriesMock).toHaveBeenCalledWith('q2', ['seq-x']));
  });
});

describe('QueuesPage AtQueueStart round-trip (feature 060, T013)', () => {
  it('saves an AtQueueStart entry and reflects it after reload', async () => {
    saveTemplateMock.mockResolvedValue({ id: 't1' } as any);
    // Reload returns the saved template with the entry typed AtQueueStart.
    getTemplateMock.mockResolvedValue({
      id: 't1', name: 'At Start Farm', entryCount: 1, createdAt: null, updatedAt: null,
      entries: [
        { sequenceId: 'seq-a', sequenceName: 'A', stale: false, scheduleType: 'AtQueueStart', timerTimeOfDay: null },
        { sequenceId: 'seq-b', sequenceName: 'B', stale: false, scheduleType: 'OncePerRun', timerTimeOfDay: null },
      ],
    } as any);

    render(<QueuesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByText('Daily'));
    await screen.findByText('Edit Queue');

    // Mark the first entry as "At Queue Start" via the schedule dropdown.
    fireEvent.change(screen.getByLabelText('Schedule type for A'), { target: { value: 'AtQueueStart' } });

    // Save the template and confirm the schedule type is carried on the wire (SC-004).
    await saveTemplateAs('At Start Farm');

    await waitFor(() =>
      expect(saveTemplateMock).toHaveBeenCalledWith({
        name: 'At Start Farm',
        entries: [
          { sequenceId: 'seq-a', scheduleType: 'AtQueueStart' },
          { sequenceId: 'seq-b', scheduleType: 'OncePerRun' },
        ],
        overwrite: false,
      })
    );

    // The dropdown still reflects the selected at-queue-start type for the first entry.
    expect((screen.getByLabelText('Schedule type for A') as HTMLSelectElement).value).toBe('AtQueueStart');
  });
});

describe('QueuesPage scheduling areas round-trip (feature 061)', () => {
  // getQueue returns whatever order the last replaceQueueEntries set, with fresh entryIds —
  // mirroring the runtime API so the order-aware save path can be exercised end-to-end.
  const wireDynamicEntries = (initial: Array<{ sequenceId: string; sequenceName: string }>) => {
    let current = initial.map((e, i) => ({ entryId: `e${i + 1}`, sequenceId: e.sequenceId, sequenceName: e.sequenceName, stale: false }));
    getQueueMock.mockImplementation(() => Promise.resolve({ ...queue({ entryCount: current.length }), entries: current } as any));
    replaceEntriesMock.mockImplementation((_id: string, seqIds: string[]) => {
      current = seqIds.map((sid, i) => ({ entryId: `r${i + 1}`, sequenceId: sid, sequenceName: initial.find((x) => x.sequenceId === sid)?.sequenceName ?? sid, stale: false }));
      return Promise.resolve({} as any);
    });
  };

  const openEditor = async () => {
    render(<QueuesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByText('Daily'));
    await screen.findByText('Edit Queue');
  };

  it('I1: reassigning a card then saving persists the destination type in canonical order', async () => {
    wireDynamicEntries([{ sequenceId: 'seq-a', sequenceName: 'A' }, { sequenceId: 'seq-b', sequenceName: 'B' }]);
    saveTemplateMock.mockResolvedValue({ id: 't1' } as any);
    await openEditor();

    // Move A to "After every step" via the non-drag schedule selector.
    fireEvent.change(screen.getByLabelText('Schedule type for A'), { target: { value: 'EveryStep' } });

    // Canonical order now puts OncePerRun (B) before EveryStep (A).
    expect(within(screen.getByRole('region', { name: 'After every step' })).getByText('A')).toBeInTheDocument();
    expect(screen.getByLabelText('After Every Step')).toBeInTheDocument();

    await saveTemplateAs('Farm');

    await waitFor(() => expect(replaceEntriesMock).toHaveBeenCalledWith('q1', ['seq-b', 'seq-a']));
    await waitFor(() =>
      expect(saveTemplateMock).toHaveBeenCalledWith({
        name: 'Farm',
        entries: [
          { sequenceId: 'seq-b', scheduleType: 'OncePerRun' },
          { sequenceId: 'seq-a', scheduleType: 'EveryStep' },
        ],
        overwrite: false,
      })
    );
  });

  it('I2: saving is order-aware — persists the current linear order and builds the template in that order', async () => {
    wireDynamicEntries([{ sequenceId: 'seq-a', sequenceName: 'A' }, { sequenceId: 'seq-b', sequenceName: 'B' }]);
    saveTemplateMock.mockResolvedValue({ id: 't1' } as any);
    await openEditor();

    await saveTemplateAs('Ordered');

    // The runtime queue order is rewritten to the editor's linear order before the template is built.
    await waitFor(() => expect(replaceEntriesMock).toHaveBeenCalledWith('q1', ['seq-a', 'seq-b']));
    await waitFor(() =>
      expect(saveTemplateMock).toHaveBeenCalledWith({
        name: 'Ordered',
        entries: [
          { sequenceId: 'seq-a', scheduleType: 'OncePerRun' },
          { sequenceId: 'seq-b', scheduleType: 'OncePerRun' },
        ],
        overwrite: false,
      })
    );
  });

  it('I3: adding a sequence places it in "Once per run" and saves it as OncePerRun', async () => {
    wireDynamicEntries([{ sequenceId: 'seq-a', sequenceName: 'A' }]);
    listSequencesMock.mockResolvedValue([{ id: 'seq-c', name: 'Charlie', steps: [] }] as any);
    (addQueueEntry as jest.MockedFunction<typeof addQueueEntry>).mockImplementation((_id: string, sequenceId: string) => {
      return Promise.resolve({ entryId: 'e2', sequenceId, sequenceName: 'Charlie', stale: false } as any);
    });
    // After add, getQueue should reflect both entries.
    getQueueMock.mockResolvedValue({
      ...queue({ entryCount: 2 }),
      entries: [
        { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'A', stale: false },
        { entryId: 'e2', sequenceId: 'seq-c', sequenceName: 'Charlie', stale: false },
      ],
    } as any);
    replaceEntriesMock.mockResolvedValue({} as any);
    saveTemplateMock.mockResolvedValue({ id: 't1' } as any);
    await openEditor();

    fireEvent.change(screen.getByLabelText('Add sequence'), { target: { value: 'seq-c' } });
    fireEvent.click(screen.getByText('Add'));

    const oncePerRun = await screen.findByRole('region', { name: 'Once per run' });
    await waitFor(() => expect(within(oncePerRun).getByText('Charlie')).toBeInTheDocument());

    await saveTemplateAs('Added');

    await waitFor(() =>
      expect(saveTemplateMock).toHaveBeenCalledWith({
        name: 'Added',
        entries: [
          { sequenceId: 'seq-a', scheduleType: 'OncePerRun' },
          { sequenceId: 'seq-c', scheduleType: 'OncePerRun' },
        ],
        overwrite: false,
      })
    );
  });

  it('I4: a card moved out of "Scheduled" before save is emitted with the destination type and no timer fields', async () => {
    // Open a queue linked to a template whose first entry is a Timer with a time-of-day.
    getQueueMock.mockResolvedValue({
      ...queue({ entryCount: 2 }),
      linkedTemplateId: 't1',
      linkedTemplateName: 'Timed',
      entries: [
        { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'A', stale: false },
        { entryId: 'e2', sequenceId: 'seq-b', sequenceName: 'B', stale: false },
      ],
    } as any);
    getTemplateMock.mockResolvedValue({
      id: 't1', name: 'Timed', entryCount: 2, createdAt: null, updatedAt: null,
      entries: [
        { sequenceId: 'seq-a', sequenceName: 'A', stale: false, scheduleType: 'Timer', timerTimeOfDay: '15:30', timerRelativeOffset: null },
        { sequenceId: 'seq-b', sequenceName: 'B', stale: false, scheduleType: 'OncePerRun', timerTimeOfDay: null, timerRelativeOffset: null },
      ],
    } as any);
    replaceEntriesMock.mockResolvedValue({} as any);
    saveTemplateMock.mockResolvedValue({ id: 't1' } as any);
    await openEditor();

    // The Timer card renders in "Scheduled" with its time-of-day restored.
    expect(await screen.findByLabelText('Timer time for A')).toHaveValue('15:30');

    // Move it out to "Once per run"; its timer detail is retained in state but must not be emitted.
    fireEvent.change(screen.getByLabelText('Schedule type for A'), { target: { value: 'OncePerRun' } });

    await saveTemplateAs('Moved', 'Timed');

    await waitFor(() =>
      expect(saveTemplateMock).toHaveBeenCalledWith({
        name: 'Moved',
        entries: [
          { sequenceId: 'seq-a', scheduleType: 'OncePerRun' },
          { sequenceId: 'seq-b', scheduleType: 'OncePerRun' },
        ],
        overwrite: false,
      })
    );
  });
});

describe('QueuesPage template delete wiring', () => {
  it('deletes a template from the picker without touching the queue entries', async () => {
    deleteTemplateMock.mockResolvedValue(undefined as any);
    const picker = await openLoadSection();
    expect(screen.getAllByTestId('queue-entry')).toHaveLength(2);

    fireEvent.click(within(picker).getByText('Delete'));
    const confirm = await screen.findByRole('dialog', { name: 'Confirm Delete' });
    listTemplatesMock.mockResolvedValue([] as any);
    fireEvent.click(within(confirm).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(deleteTemplateMock).toHaveBeenCalledWith('t1'));
    expect(replaceEntriesMock).not.toHaveBeenCalled();
    expect(screen.getAllByTestId('queue-entry')).toHaveLength(2);
  });
});
