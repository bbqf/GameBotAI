import React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { PhoneEntryPage, PhoneEntryPageProps } from '../PhoneEntryPage';
import * as sequencesApi from '../../../services/sequences';
import * as templatesApi from '../../../services/queueTemplates';
import { ApiError } from '../../../lib/api';
import { setLeaveGuard, shouldBlockLeave } from '../../../lib/phoneLeaveGuard';

jest.mock('../../../services/sequences');
jest.mock('../../../services/queueTemplates');

const sequencesMock = sequencesApi as jest.Mocked<typeof sequencesApi>;
const templatesMock = templatesApi as jest.Mocked<typeof templatesApi>;

const queueDetail = { id: 'q1', name: 'Daily queue', status: 'Stopped', entries: [] } as unknown as PhoneEntryPageProps['detail'];

const makeTemplate = (over: Partial<templatesApi.QueueTemplateDetail['entries'][number]> = {}): templatesApi.QueueTemplateDetail => ({
  id: 't1', name: 'Daily tpl', entryCount: 2, createdAt: null, updatedAt: null,
  entries: [
    {
      entryId: 'e1', sequenceId: 's1', sequenceName: 'Help All', stale: false, scheduleType: 'Timer',
      timerTimeOfDay: '14:30:00', timerRelativeOffset: null, enabled: true,
      parameterValues: [{ name: 'count', value: '5' }],
      effectiveParameters: [{ name: 'count', value: '5', originLayer: 'entry', declared: true }],
      ...over
    },
    { entryId: 'e2', sequenceId: 's2', sequenceName: 'Gift', stale: false, scheduleType: 'OncePerRun', timerTimeOfDay: null, timerRelativeOffset: null, enabled: true }
  ]
});

const sequence = (parameters: unknown[] | undefined) =>
  ({ id: 's1', name: 'Help All', steps: [], parameters }) as unknown as sequencesApi.SequenceDto;

const COUNT = { name: 'count', type: 'number', default: '3', required: false };

const setup = (over: Partial<PhoneEntryPageProps> = {}) => {
  const props: PhoneEntryPageProps = {
    detail: queueDetail,
    template: makeTemplate(),
    entryId: 'e1',
    connectionState: 'online',
    onBack: jest.fn(),
    onSaved: jest.fn(),
    ...over
  };
  const view = render(<PhoneEntryPage {...props} />);
  return { props, view };
};

beforeEach(() => {
  jest.resetAllMocks();
  sequencesMock.getSequence.mockResolvedValue(sequence([COUNT]));
});

describe('PhoneEntryPage content (rules E-01 to E-04)', () => {
  it('shows Back, the entry name, the queue name, the schedule as text, and the parameter form', async () => {
    setup();

    expect(await screen.findByLabelText('Parameter values')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Back/ })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Help All' })).toBeInTheDocument();
    expect(screen.getByText('Daily queue')).toBeInTheDocument();
    expect(screen.getByTestId('entry-schedule')).toHaveTextContent('Daily at 14:30');
    expect(screen.getByText('count')).toBeInTheDocument();
  });

  it('shows the schedule with no control', async () => {
    setup();
    await screen.findByLabelText('Parameter values');

    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(document.querySelector('input[type="time"]')).toBeNull();
    expect(screen.getByText('Use a larger screen to edit schedules.')).toBeInTheDocument();
  });

  it('shows the effective value text of a row that uses its default', async () => {
    setup({ template: makeTemplate({ parameterValues: [], effectiveParameters: [{ name: 'count', value: '7', originLayer: 'queue', declared: true }] }) });

    expect(await screen.findByText('7')).toBeInTheDocument();
    expect(screen.getByText(/from the queue/)).toBeInTheDocument();
  });

  it('reads the sequence one time', async () => {
    setup();
    await screen.findByLabelText('Parameter values');

    expect(sequencesMock.getSequence).toHaveBeenCalledTimes(1);
    expect(sequencesMock.getSequence).toHaveBeenCalledWith('s1');
  });

  it('shows the text for a sequence with no parameters and the Add a value button', async () => {
    sequencesMock.getSequence.mockResolvedValue(sequence([]));
    setup({ template: makeTemplate({ parameterValues: [], effectiveParameters: [] }) });

    expect(await screen.findByText('This sequence has no parameters.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Parameter values')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Add a value' }));
    expect(screen.getByLabelText('New parameter value name')).toBeInTheDocument();
  });

  it('has Save off until a value changes', async () => {
    setup();
    await screen.findByLabelText('Parameter values');

    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });
});

describe('PhoneEntryPage save (rules E-05, E-07)', () => {
  const change = async (value: string) => {
    await screen.findByLabelText('Parameter values');
    fireEvent.change(screen.getByLabelText('Value for count'), { target: { value } });
  };

  it('sends one call with the new values of this entry and shows Saved.', async () => {
    const saved = makeTemplate({ parameterValues: [{ name: 'count', value: '9' }] });
    templatesMock.saveQueueTemplate.mockResolvedValue(saved);
    const { props } = setup();

    await change('9');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(templatesMock.saveQueueTemplate).toHaveBeenCalledTimes(1));
    const request = templatesMock.saveQueueTemplate.mock.calls[0][0];
    expect(request.overwrite).toBe(true);
    expect(request.entries[0].parameterValues).toEqual([{ name: 'count', value: '9' }]);
    expect(request.entries).toHaveLength(2);
    expect(await screen.findByText('Saved.')).toBeInTheDocument();
    expect(props.onSaved).toHaveBeenCalledWith(saved);
  });

  it('does not send a second call for a second press during the save', async () => {
    let resolve: (value: templatesApi.QueueTemplateDetail) => void = () => undefined;
    templatesMock.saveQueueTemplate.mockReturnValue(new Promise((r) => { resolve = r; }));
    setup();

    await change('9');
    const save = screen.getByRole('button', { name: 'Save' });
    fireEvent.click(save);
    fireEvent.click(save);

    expect(templatesMock.saveQueueTemplate).toHaveBeenCalledTimes(1);
    await act(async () => { resolve(makeTemplate()); });
  });

  it('shows the error text, keeps the typed value, and sends no second call after a failed save', async () => {
    templatesMock.saveQueueTemplate.mockRejectedValue(new ApiError(400, 'The server refused the value.'));
    setup();

    await change('99');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('The server refused the value.')).toHaveAttribute('role', 'alert');
    expect(screen.getByLabelText('Value for count')).toHaveDisplayValue('99');
    expect(templatesMock.saveQueueTemplate).toHaveBeenCalledTimes(1);
    expect(screen.queryByText('Saved.')).not.toBeInTheDocument();
  });

  it('does not replace typed values when a new template read arrives (a poll)', async () => {
    const { props, view } = setup();
    await change('12');

    view.rerender(<PhoneEntryPage {...props} template={makeTemplate({ parameterValues: [{ name: 'count', value: '6' }] })} />);

    expect(screen.getByLabelText('Value for count')).toHaveValue('12');
  });

  it('shows the new saved values from a template read when nothing is typed', async () => {
    const { props, view } = setup();
    await screen.findByLabelText('Parameter values');

    view.rerender(<PhoneEntryPage {...props} template={makeTemplate({ parameterValues: [{ name: 'count', value: '6' }] })} />);

    expect(screen.getByLabelText('Value for count')).toHaveValue('6');
  });
});

describe('PhoneEntryPage failed read and missing entry (rules E-06, E-12)', () => {
  it('shows the error text and the stored values as plain text, with Save off', async () => {
    sequencesMock.getSequence.mockRejectedValue(new Error('down'));
    setup();

    expect(await screen.findByText('The parameters did not load.')).toBeInTheDocument();
    expect(screen.getByText('count: 5')).toBeInTheDocument();
    expect(screen.queryByLabelText('Parameter values')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('shows Entry not found and a way back for an entry that is not in the template', () => {
    const { props } = setup({ entryId: 'nope' });

    expect(screen.getByText('Entry not found')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Back/ }));
    expect(props.onBack).toHaveBeenCalledTimes(1);
  });

  it('shows Entry not found when there is no template', () => {
    setup({ template: null });

    expect(screen.getByText('Entry not found')).toBeInTheDocument();
  });

  it('calls onBack for the Back button', async () => {
    const { props } = setup();
    await screen.findByLabelText('Parameter values');

    fireEvent.click(screen.getByRole('button', { name: /Back/ }));

    expect(props.onBack).toHaveBeenCalledTimes(1);
  });
});

describe('PhoneEntryPage unsaved edits and the connection (rules E-08, E-09)', () => {
  const typeValue = async (value: string) => {
    await screen.findByLabelText('Parameter values');
    fireEvent.change(screen.getByLabelText('Value for count'), { target: { value } });
  };

  afterEach(() => setLeaveGuard(null));

  it('opens the dialog for Back with unsaved edits, and does not leave', async () => {
    const { props } = setup();
    await typeValue('9');

    fireEvent.click(screen.getByRole('button', { name: /Back/ }));

    expect(screen.getByRole('alertdialog', { name: 'Unsaved changes' })).toBeInTheDocument();
    expect(props.onBack).not.toHaveBeenCalled();
  });

  it('closes the dialog and keeps the typed value for Keep editing', async () => {
    const { props } = setup();
    await typeValue('9');
    fireEvent.click(screen.getByRole('button', { name: /Back/ }));

    fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }));

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Value for count')).toHaveValue('9');
    expect(props.onBack).not.toHaveBeenCalled();
    expect(shouldBlockLeave()).toBe(true);
  });

  it('leaves and clears the guard for Discard changes', async () => {
    const { props } = setup();
    await typeValue('9');
    fireEvent.click(screen.getByRole('button', { name: /Back/ }));

    fireEvent.click(screen.getByRole('button', { name: 'Discard changes' }));

    expect(props.onBack).toHaveBeenCalledTimes(1);
    expect(shouldBlockLeave()).toBe(false);
  });

  it('leaves at once for Back with no edits', async () => {
    const { props } = setup();
    await screen.findByLabelText('Parameter values');

    fireEvent.click(screen.getByRole('button', { name: /Back/ }));

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(props.onBack).toHaveBeenCalledTimes(1);
  });

  it('sets the guard only while there are unsaved edits', async () => {
    setup();
    await screen.findByLabelText('Parameter values');
    expect(shouldBlockLeave()).toBe(false);

    fireEvent.change(screen.getByLabelText('Value for count'), { target: { value: '9' } });
    expect(shouldBlockLeave()).toBe(true);

    fireEvent.change(screen.getByLabelText('Value for count'), { target: { value: '5' } });
    expect(shouldBlockLeave()).toBe(false);
  });

  it('removes the guard when the page goes away', async () => {
    const { view } = setup();
    await typeValue('9');
    expect(shouldBlockLeave()).toBe(true);

    view.unmount();

    expect(shouldBlockLeave()).toBe(false);
  });

  it('opens the dialog when the leave request counter goes up and there are unsaved edits', async () => {
    const { props, view } = setup();
    await typeValue('9');

    view.rerender(<PhoneEntryPage {...props} leaveRequest={1} />);

    expect(screen.getByRole('alertdialog', { name: 'Unsaved changes' })).toBeInTheDocument();
  });

  it('opens no dialog when the leave request counter goes up and nothing is typed', async () => {
    const { props, view } = setup();
    await screen.findByLabelText('Parameter values');

    view.rerender(<PhoneEntryPage {...props} leaveRequest={1} />);

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('turns Save off while the connection is lost, and sends no save when the connection returns', async () => {
    const { props, view } = setup({ connectionState: 'lost' });
    await typeValue('9');
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();

    view.rerender(<PhoneEntryPage {...props} connectionState="online" />);

    expect(templatesMock.saveQueueTemplate).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
  });
});
