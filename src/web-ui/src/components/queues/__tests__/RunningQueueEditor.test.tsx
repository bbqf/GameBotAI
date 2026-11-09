import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { RunningQueueEditor } from '../RunningQueueEditor';
import {
  addQueueEntry,
  getQueueMonitor,
  removeQueueEntry,
  replaceQueueEntries,
  runNextEntry,
  setQueueTemplateLink,
} from '../../../services/queues';
import { getQueueTemplate, listQueueTemplates, saveQueueTemplate } from '../../../services/queueTemplates';

jest.mock('../../../services/queues');
jest.mock('../../../services/queueTemplates');
// The upgrade notice reads its own data. It is not part of these tests.
jest.mock('../TemplateUpgradeNotice', () => ({ TemplateUpgradeNotice: () => null }));

const getTemplateMock = getQueueTemplate as jest.MockedFunction<typeof getQueueTemplate>;
const listTemplatesMock = listQueueTemplates as jest.MockedFunction<typeof listQueueTemplates>;
const saveTemplateMock = saveQueueTemplate as jest.MockedFunction<typeof saveQueueTemplate>;
const linkMock = setQueueTemplateLink as jest.MockedFunction<typeof setQueueTemplateLink>;

const templateEntry = (id: string | null, sequenceId: string, name: string, over: Record<string, unknown> = {}) => ({
  ...(id ? { entryId: id } : {}),
  sequenceId, sequenceName: name, stale: false, scheduleType: 'OncePerRun', timerTimeOfDay: null,
  timerRelativeOffset: null, enabled: true, ...over,
});

const template = () => ({
  id: 't1', name: 'Daily Farm', entryCount: 2, createdAt: null, updatedAt: null,
  entries: [templateEntry('te1', 'seq-a', 'Alpha'), templateEntry('te2', 'seq-b', 'Bravo', { enabled: false })],
});

const queue = (over: Record<string, unknown> = {}) => ({
  id: 'q1',
  linkedTemplateId: 't1',
  linkedTemplateName: 'Daily Farm',
  entries: [
    { entryId: 'r1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false },
    { entryId: 'r2', sequenceId: 'seq-b', sequenceName: 'Bravo', stale: false },
  ],
  ...over,
});

const sequences = [{ id: 'seq-a', name: 'Alpha' }, { id: 'seq-b', name: 'Bravo' }, { id: 'seq-c', name: 'Charlie' }] as any;

beforeEach(() => {
  jest.resetAllMocks();
  getTemplateMock.mockResolvedValue(template() as any);
  listTemplatesMock.mockResolvedValue([{ id: 't1', name: 'Daily Farm', entryCount: 2, createdAt: null, updatedAt: null }] as any);
  saveTemplateMock.mockResolvedValue({ id: 't1', name: 'Daily Farm' } as any);
});

const openEditor = async (over: Record<string, unknown> = {}, props: Record<string, unknown> = {}) => {
  render(<RunningQueueEditor queue={queue(over) as any} sequences={sequences} {...props} />);
  await screen.findByRole('region', { name: 'Queue sequences' });
};

describe('RunningQueueEditor with a linked template', () => {
  it('reads the entries from the linked template, not from the queue', async () => {
    getTemplateMock.mockResolvedValue({
      ...template(),
      entries: [templateEntry('te1', 'seq-a', 'Alpha'), templateEntry('te9', 'seq-c', 'Charlie')],
    } as any);

    await openEditor();

    expect(screen.getByRole('button', { name: 'Remove Alpha' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Remove Charlie' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remove Bravo' })).not.toBeInTheDocument();
    expect(getTemplateMock).toHaveBeenCalledWith('t1');
  });

  it('keeps the template entry IDs and the enabled flag when it saves', async () => {
    await openEditor();

    fireEvent.click(screen.getByRole('button', { name: 'Save Template' }));

    await waitFor(() => expect(saveTemplateMock).toHaveBeenCalledTimes(1));
    expect(saveTemplateMock).toHaveBeenCalledWith({
      name: 'Daily Farm',
      entries: [
        { entryId: 'te1', sequenceId: 'seq-a', scheduleType: 'OncePerRun' },
        { entryId: 'te2', sequenceId: 'seq-b', scheduleType: 'OncePerRun', enabled: false },
      ],
      overwrite: true,
    });
  });

  it('removes an entry from the working list and saves without it', async () => {
    await openEditor();

    fireEvent.click(screen.getByRole('button', { name: 'Remove Alpha' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Template' }));

    await waitFor(() => expect(saveTemplateMock).toHaveBeenCalledTimes(1));
    expect(saveTemplateMock.mock.calls[0][0].entries).toEqual([
      { entryId: 'te2', sequenceId: 'seq-b', scheduleType: 'OncePerRun', enabled: false },
    ]);
  });

  it('sends the changes to the template only, never to the queue entries', async () => {
    await openEditor();

    fireEvent.click(screen.getByRole('button', { name: 'Remove Alpha' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Template' }));

    await waitFor(() => expect(saveTemplateMock).toHaveBeenCalled());
    expect(addQueueEntry).not.toHaveBeenCalled();
    expect(removeQueueEntry).not.toHaveBeenCalled();
    expect(replaceQueueEntries).not.toHaveBeenCalled();
    expect(linkMock).not.toHaveBeenCalled();
  });

  it('tells the page and the operator that the template is saved', async () => {
    const onTemplateSaved = jest.fn();
    await openEditor({}, { onTemplateSaved });

    fireEvent.click(screen.getByRole('button', { name: 'Save Template' }));

    expect(await screen.findByText('Template "Daily Farm" saved successfully.')).toBeInTheDocument();
    expect(onTemplateSaved).toHaveBeenCalledTimes(1);
  });

  it('shows the error and keeps the edits when the save fails', async () => {
    saveTemplateMock.mockRejectedValue(new Error('Template is locked.'));
    await openEditor();

    fireEvent.click(screen.getByRole('button', { name: 'Remove Alpha' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Template' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Template is locked.');
    expect(screen.queryByRole('button', { name: 'Remove Alpha' })).not.toBeInTheDocument();
  });

  it('links the saved template when the save gives a different template ID', async () => {
    saveTemplateMock.mockResolvedValue({ id: 't2', name: 'Daily Farm' } as any);
    linkMock.mockResolvedValue({} as any);
    await openEditor();

    fireEvent.click(screen.getByRole('button', { name: 'Save Template' }));

    await waitFor(() => expect(linkMock).toHaveBeenCalledWith('q1', 't2'));
  });

  it('has no load picker and no Reload button for a running queue', async () => {
    await openEditor();

    expect(screen.queryByRole('button', { name: /Reload/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Load template' })).not.toBeInTheDocument();
  });

  it('starts from the edits that the page already holds when it gets carried edits', async () => {
    await openEditor({}, {
      carried: {
        entries: [{ entryId: 'r1', sequenceId: 'seq-c', sequenceName: 'Charlie', stale: false }],
        schedule: { r1: { scheduleType: 'OncePerRun', timerTimeOfDay: '' } },
      },
    });

    expect(screen.getByRole('button', { name: 'Remove Charlie' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remove Alpha' })).not.toBeInTheDocument();
    expect(getTemplateMock).not.toHaveBeenCalled();
  });
});

describe('RunningQueueEditor with no linked template (FR-018)', () => {
  const noTemplate = { linkedTemplateId: null, linkedTemplateName: null };

  it('shows the entries read-only with the message and no way to change them', async () => {
    render(<RunningQueueEditor queue={queue(noTemplate) as any} sequences={sequences} />);

    expect(await screen.findByText('Link a template to edit this queue while it runs.')).toBeInTheDocument();
    const list = screen.getByRole('list', { name: 'Queue sequences (read only)' });
    expect(within(list).getByText('Alpha')).toBeInTheDocument();
    expect(within(list).getByText('Bravo')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Remove / })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save Template' })).not.toBeInTheDocument();
    expect(getTemplateMock).not.toHaveBeenCalled();
  });

  it('links a template with the link action and tells the page', async () => {
    linkMock.mockResolvedValue({} as any);
    const onLinked = jest.fn();
    render(<RunningQueueEditor queue={queue(noTemplate) as any} sequences={sequences} onLinked={onLinked} />);

    const picker = await screen.findByLabelText('Template to link');
    await waitFor(() => expect(within(picker).getByRole('option', { name: 'Daily Farm' })).toBeInTheDocument());
    fireEvent.change(picker, { target: { value: 't1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Link template' }));

    await waitFor(() => expect(linkMock).toHaveBeenCalledWith('q1', 't1'));
    await waitFor(() => expect(onLinked).toHaveBeenCalledTimes(1));
  });

  it('keeps the link action off until the operator picks a template', async () => {
    render(<RunningQueueEditor queue={queue(noTemplate) as any} sequences={sequences} />);

    expect(await screen.findByRole('button', { name: 'Link template' })).toBeDisabled();
  });

  it('shows the error when the link fails', async () => {
    linkMock.mockRejectedValue(new Error('Could not link.'));
    render(<RunningQueueEditor queue={queue(noTemplate) as any} sequences={sequences} />);

    const picker = await screen.findByLabelText('Template to link');
    await waitFor(() => expect(within(picker).getByRole('option', { name: 'Daily Farm' })).toBeInTheDocument());
    fireEvent.change(picker, { target: { value: 't1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Link template' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not link.');
  });
});

describe('RunningQueueEditor Run next (feature 184)', () => {
  const monitorMock = getQueueMonitor as jest.MockedFunction<typeof getQueueMonitor>;
  const runNextMock = runNextEntry as jest.MockedFunction<typeof runNextEntry>;

  it('reads the monitor, turns Run next on for an idle entry, and sends the call with the entry ID', async () => {
    monitorMock.mockResolvedValue({ running: true, entryStates: { te1: 'idle' } } as any);
    runNextMock.mockResolvedValue({ entryId: 'te1', sequenceId: 'seq-a', alreadyQueued: false, state: 'queued' });
    await openEditor();

    const button = await screen.findByRole('button', { name: 'Run next Alpha' });
    await waitFor(() => expect(button).toBeEnabled());
    const readsBefore = monitorMock.mock.calls.length;
    fireEvent.click(button);

    await waitFor(() => expect(runNextMock).toHaveBeenCalledWith('q1', 'te1'));
    expect(runNextMock).toHaveBeenCalledTimes(1);
    // The editor reads the monitor again after the call.
    await waitFor(() => expect(monitorMock.mock.calls.length).toBeGreaterThan(readsBefore));
  });

  it('shows the text of a refused call and does not send it again', async () => {
    monitorMock.mockResolvedValue({ running: true, entryStates: { te1: 'idle' } } as any);
    runNextMock.mockRejectedValue(new Error('The entry runs now. Wait until it ends.'));
    await openEditor();

    const button = await screen.findByRole('button', { name: 'Run next Alpha' });
    await waitFor(() => expect(button).toBeEnabled());
    fireEvent.click(button);

    expect(await screen.findByText('The entry runs now. Wait until it ends.')).toBeInTheDocument();
    expect(runNextMock).toHaveBeenCalledTimes(1);
  });

  it('keeps Run next off for a scheduled entry and a disabled entry, with the reason on the row', async () => {
    monitorMock.mockResolvedValue({ running: true, entryStates: { te1: 'scheduled' } } as any);
    await openEditor();

    await waitFor(() => expect(screen.getByRole('button', { name: 'Run next Alpha' })).toHaveAttribute('title', 'Scheduled. Use the timer to change it.'));
    expect(screen.getByRole('button', { name: 'Run next Alpha' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Run next Bravo' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Run next Bravo' })).toHaveAttribute('title', 'The entry is disabled');
  });

  it('reads no state and sends no call when the connection is lost', async () => {
    monitorMock.mockResolvedValue({ running: true, entryStates: { te1: 'idle' } } as any);
    await openEditor({}, { connectionState: 'lost' });

    expect(monitorMock).not.toHaveBeenCalled();
    const button = screen.getByRole('button', { name: 'Run next Alpha' });
    expect(button).toBeDisabled();
    fireEvent.click(button);
    expect(runNextMock).not.toHaveBeenCalled();
  });

  it('shows the stopped hint when the monitor says the queue does not run', async () => {
    monitorMock.mockResolvedValue({ running: false, entryStates: {} } as any);
    await openEditor();

    expect(await screen.findByText('Start the queue to use Run next.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Run next Alpha' })).toBeDisabled();
  });
});