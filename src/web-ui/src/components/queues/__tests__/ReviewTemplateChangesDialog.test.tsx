import React from 'react';
import { readFileSync } from 'fs';
import { join } from 'path';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ReviewTemplateChangesDialog } from '../ReviewTemplateChangesDialog';
import { applyTemplate, getTemplateDiff, QueueTemplateDiffDto, TemplateDifferenceDto } from '../../../services/queues';
import { ApiError } from '../../../lib/api';

jest.mock('../../../services/queues', () => {
  const actual = jest.requireActual('../../../services/queues');
  return { ...actual, getTemplateDiff: jest.fn(), applyTemplate: jest.fn() };
});

const getDiffMock = getTemplateDiff as jest.MockedFunction<typeof getTemplateDiff>;
const applyMock = applyTemplate as jest.MockedFunction<typeof applyTemplate>;

const difference = (over: Partial<TemplateDifferenceDto> = {}): TemplateDifferenceDto => ({
  key: 'disabled:1', type: 'disabled', entryId: '1', sequenceName: 'Alpha', summary: "Disable the entry 'Alpha'.",
  selectedByDefault: true, affectsSchedule: false, ...over
});

const diff = (differences: TemplateDifferenceDto[], over: Partial<QueueTemplateDiffDto> = {}): QueueTemplateDiffDto => ({
  queueId: 'q1', templateId: 't1', needsUpgrade: false, baseToken: 'sha256:abc', differences, ...over
});

const flush = async () => {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
};

const refused = (status: number, code: string, message: string, hint?: string) =>
  new ApiError(status, message, undefined, { error: { code, message, hint } });

describe('ReviewTemplateChangesDialog', () => {
  beforeEach(() => {
    jest.resetAllMocks();
  });

  it('lists each difference with its summary, and selects all but a schedule change', async () => {
    getDiffMock.mockResolvedValue(diff([
      difference(),
      difference({ key: 'added:2', type: 'added', entryId: '2', sequenceName: 'Bravo', summary: "Add the entry 'Bravo' (once per run).", affectsSchedule: true }),
      difference({ key: 'schedule:3', type: 'schedule', entryId: '3', sequenceName: 'Charlie', summary: "Change the schedule of the entry 'Charlie' from at 06:00 to at 07:30.", selectedByDefault: false, affectsSchedule: true })
    ]));

    render(<ReviewTemplateChangesDialog queueId="q1" paused onClose={() => {}} />);

    const rows = await screen.findAllByTestId('template-difference');
    expect(rows).toHaveLength(3);
    expect(screen.getByLabelText("Disable the entry 'Alpha'.")).toBeChecked();
    expect(screen.getByLabelText("Add the entry 'Bravo' (once per run).")).toBeChecked();
    const schedule = screen.getByLabelText(/Change the schedule of the entry 'Charlie'/);
    expect(schedule).not.toBeChecked();
    expect(screen.getByTestId('schedule-change-note')).toHaveTextContent('Schedule change');
    expect(screen.getByTestId('review-selected-count')).toHaveTextContent('2 of 3 selected');
  });

  it('shows that there is nothing to apply', async () => {
    getDiffMock.mockResolvedValue(diff([]));

    render(<ReviewTemplateChangesDialog queueId="q1" paused onClose={() => {}} />);

    expect(await screen.findByTestId('review-nothing')).toHaveTextContent('nothing to apply');
    expect(screen.queryByRole('button', { name: /Apply/ })).not.toBeInTheDocument();
  });

  it('tells the operator to upgrade a template that needs it', async () => {
    getDiffMock.mockResolvedValue(diff([], { needsUpgrade: true }));

    render(<ReviewTemplateChangesDialog queueId="q1" paused onClose={() => {}} />);

    expect(await screen.findByTestId('review-needs-upgrade')).toHaveTextContent('Upgrade the template first');
  });

  it('applies only the selected differences with the token that it read', async () => {
    getDiffMock.mockResolvedValue(diff([
      difference(),
      difference({ key: 'removed:2', type: 'removed', entryId: '2', sequenceName: 'Bravo', summary: "Remove the entry 'Bravo'." })
    ]));
    applyMock.mockResolvedValue({ applied: ['disabled:1'], planVersion: 1 });
    const onApplied = jest.fn();

    render(<ReviewTemplateChangesDialog queueId="q1" paused onClose={() => {}} onApplied={onApplied} />);
    fireEvent.click(await screen.findByLabelText("Remove the entry 'Bravo'."));
    fireEvent.click(screen.getByRole('button', { name: 'Apply 1 change' }));

    await waitFor(() => expect(applyMock).toHaveBeenCalledWith('q1', 'sha256:abc', ['disabled:1']));
    expect(await screen.findByTestId('review-done')).toHaveTextContent('1 change applied');
    expect(screen.getByTestId('review-done')).toHaveTextContent('Resume the queue');
    expect(onApplied).toHaveBeenCalledTimes(1);
  });

  it('lets the operator select a schedule change on purpose', async () => {
    getDiffMock.mockResolvedValue(diff([
      difference({ key: 'schedule:3', type: 'schedule', entryId: '3', summary: 'Change the schedule of the entry.', selectedByDefault: false, affectsSchedule: true })
    ]));
    applyMock.mockResolvedValue({ applied: ['schedule:3'], planVersion: 1 });

    render(<ReviewTemplateChangesDialog queueId="q1" paused onClose={() => {}} />);
    const box = await screen.findByLabelText('Change the schedule of the entry.');
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
    fireEvent.click(box);
    fireEvent.click(screen.getByRole('button', { name: 'Apply 1 change' }));

    await waitFor(() => expect(applyMock).toHaveBeenCalledWith('q1', 'sha256:abc', ['schedule:3']));
  });

  it('turns Apply off while the queue is not paused and says what to do', async () => {
    getDiffMock.mockResolvedValue(diff([difference()]));

    render(<ReviewTemplateChangesDialog queueId="q1" paused={false} onClose={() => {}} />);

    expect(await screen.findByTestId('review-not-paused')).toHaveTextContent('Pause the queue first');
    expect(screen.getByRole('button', { name: 'Apply 1 change' })).toBeDisabled();
  });

  it('turns Apply off when no difference is selected', async () => {
    getDiffMock.mockResolvedValue(diff([difference()]));

    render(<ReviewTemplateChangesDialog queueId="q1" paused onClose={() => {}} />);
    fireEvent.click(await screen.findByLabelText("Disable the entry 'Alpha'."));

    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
    expect(screen.getByTestId('review-selected-count')).toHaveTextContent('0 of 1 selected');
  });

  it('asks for a new review when the template changed after the read', async () => {
    getDiffMock
      .mockResolvedValueOnce(diff([difference()]))
      .mockResolvedValueOnce(diff([difference(), difference({ key: 'removed:2', type: 'removed', entryId: '2', summary: "Remove the entry 'Bravo'." })], { baseToken: 'sha256:new' }));
    applyMock.mockRejectedValue(refused(409, 'template_changed', 'The template changed after you read the differences.', 'Review the changes again.'));

    render(<ReviewTemplateChangesDialog queueId="q1" paused onClose={() => {}} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Apply 1 change' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('The template changed after you read the differences.');
    expect(alert).toHaveTextContent('Review the changes again.');
    fireEvent.click(screen.getByRole('button', { name: 'Review again' }));

    expect(await screen.findByLabelText("Remove the entry 'Bravo'.")).toBeInTheDocument();
    expect(getDiffMock).toHaveBeenCalledTimes(2);
  });

  it('shows the message and the hint of any other refusal', async () => {
    getDiffMock.mockResolvedValue(diff([difference()]));
    applyMock.mockRejectedValue(refused(409, 'not_paused', 'The queue is not paused.', 'Pause the queue first.'));

    render(<ReviewTemplateChangesDialog queueId="q1" paused onClose={() => {}} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Apply 1 change' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('The queue is not paused.');
    expect(alert).toHaveTextContent('Pause the queue first.');
    expect(screen.queryByRole('button', { name: 'Review again' })).not.toBeInTheDocument();
  });

  it('shows the error when the differences cannot be read', async () => {
    getDiffMock.mockRejectedValue(refused(409, 'not_running', 'The queue does not run, so there is no run to compare.', 'Start the queue first.'));

    render(<ReviewTemplateChangesDialog queueId="q1" paused onClose={() => {}} />);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('The queue does not run');
    expect(alert).toHaveTextContent('Start the queue first.');
  });

  it('closes on request', async () => {
    getDiffMock.mockResolvedValue(diff([]));
    const onClose = jest.fn();

    render(<ReviewTemplateChangesDialog queueId="q1" paused onClose={onClose} />);
    await flush();
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('does not apply twice while a call is open', async () => {
    getDiffMock.mockResolvedValue(diff([difference()]));
    let finish: (v: { applied: string[]; planVersion: number }) => void = () => {};
    applyMock.mockReturnValue(new Promise((resolve) => { finish = resolve; }));

    render(<ReviewTemplateChangesDialog queueId="q1" paused onClose={() => {}} />);
    const button = await screen.findByRole('button', { name: 'Apply 1 change' });
    fireEvent.click(button);
    fireEvent.click(button);

    expect(applyMock).toHaveBeenCalledTimes(1);
    await act(async () => { finish({ applied: ['disabled:1'], planVersion: 1 }); });
  });
});

describe('ReviewTemplateChangesDialog styles', () => {
  const css = readFileSync(join(__dirname, '..', 'ReviewTemplateChangesDialog.css'), 'utf8');

  it('gives each control a hit area of at least 44 px and an input font of at least 16 px', () => {
    expect(css).toMatch(/min-height:\s*44px/);
    expect(css).toMatch(/font-size:\s*16px/);
  });

  it('uses one column that wraps, with no horizontal scroll', () => {
    expect(css).toMatch(/overflow-wrap:\s*anywhere/);
    expect(css).toMatch(/flex-wrap:\s*wrap/);
    expect(css).not.toMatch(/overflow-x:\s*scroll/);
  });
});

describe('ReviewTemplateChangesDialog: template that needs an upgrade', () => {
  it('offers the upgrade for the template of the queue', async () => {
    getDiffMock.mockResolvedValue(diff([], { needsUpgrade: true, templateId: 't9' }));

    render(<ReviewTemplateChangesDialog queueId="q1" paused onClose={() => {}} />);

    expect(await screen.findByTestId('review-needs-upgrade')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Upgrade template' })).toBeInTheDocument();
  });
});

describe('ReviewTemplateChangesDialog: new order (user story 4)', () => {
  it('lists the order difference and selects it at first', async () => {
    getDiffMock.mockResolvedValue(diff([
      difference({ key: 'order:all', type: 'order', entryId: '', sequenceName: '', summary: 'Put the entries in the order of the template. No schedule changes.' })
    ]));
    applyMock.mockResolvedValue({ applied: ['order:all'], planVersion: 1 });

    render(<ReviewTemplateChangesDialog queueId="q1" paused onClose={() => {}} />);

    const box = await screen.findByLabelText('Put the entries in the order of the template. No schedule changes.');
    expect(box).toBeChecked();
    fireEvent.click(screen.getByRole('button', { name: 'Apply 1 change' }));
    await waitFor(() => expect(applyMock).toHaveBeenCalledWith('q1', 'sha256:abc', ['order:all']));
  });
});
