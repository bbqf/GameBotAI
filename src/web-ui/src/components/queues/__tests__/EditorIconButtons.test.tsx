import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueueSchedulingAreas } from '../QueueSchedulingAreas';
import { QueueTemplateControls } from '../QueueTemplateControls';
import { QueueGameControls } from '../QueueGameControls';
import { RunningQueueEditor } from '../RunningQueueEditor';
import { EntrySchedule } from '../QueueEntryList';
import { QueueEntryDto } from '../../../services/queues';
import { SequenceDto } from '../../../services/sequences';
import { getQueueTemplate, listQueueTemplates } from '../../../services/queueTemplates';

jest.mock('../../../services/queues');
jest.mock('../../../services/queueTemplates');
jest.mock('../TemplateUpgradeNotice', () => ({ TemplateUpgradeNotice: () => null }));

const sequences = [{ id: 'seq-a', name: 'Alpha', steps: [] }] as unknown as SequenceDto[];

const entry = (entryId: string): QueueEntryDto => ({ entryId, sequenceId: `seq-${entryId}`, sequenceName: entryId.toUpperCase(), stale: false });

const baseProps = () => ({
  sequences,
  onAdd: jest.fn(),
  onRemove: jest.fn(),
  onReorderAndReassign: jest.fn(),
  onParameterValuesChange: jest.fn(),
  onTimerTimeChange: jest.fn(),
  onTimerModeChange: jest.fn(),
});

/** Gives the buttons of the container that show visible text. An icon button has an svg and no text. */
const buttonsWithText = (container: HTMLElement): string[] =>
  Array.from(container.querySelectorAll('button'))
    .filter((button) => (button.textContent ?? '').trim() !== '')
    .map((button) => (button.textContent ?? '').trim());

describe('Parameters and Remove of a sequence row (feature 184, US3)', () => {
  const renderRow = (schedule: EntrySchedule = { scheduleType: 'OncePerRun', timerTimeOfDay: '' }) => {
    const props = baseProps();
    render(<QueueSchedulingAreas entries={[entry('a')]} entrySchedule={{ a: schedule }} {...props} />);
    return { props, row: screen.getByTestId('queue-entry') };
  };

  it('are icon buttons with the names "Parameters for <label>" and "Remove <label>" and the tooltips "Parameters" and "Remove"', () => {
    const { row } = renderRow();

    const parameters = within(row).getByRole('button', { name: 'Parameters for A' });
    const remove = within(row).getByRole('button', { name: 'Remove A' });
    expect(parameters).toHaveAttribute('title', 'Parameters');
    expect(remove).toHaveAttribute('title', 'Remove');
    expect(parameters.querySelector('svg')).not.toBeNull();
    expect(remove.querySelector('svg')).not.toBeNull();
  });

  it('shows no visible button text in the row', () => {
    const { row } = renderRow();

    expect(buttonsWithText(row)).toEqual([]);
  });

  it('gives Remove the destructive tone', () => {
    const { row } = renderRow();

    expect(within(row).getByRole('button', { name: 'Remove A' })).toHaveClass('icon-button--destructive');
    expect(within(row).getByRole('button', { name: 'Parameters for A' })).not.toHaveClass('icon-button--destructive');
  });

  it('keeps aria-expanded on Parameters and opens the form on a press', () => {
    const { row } = renderRow();
    const parameters = within(row).getByRole('button', { name: 'Parameters for A' });
    expect(parameters).toHaveAttribute('aria-expanded', 'false');

    fireEvent.click(parameters);

    expect(parameters).toHaveAttribute('aria-expanded', 'true');
  });

  it('calls onRemove with the entry ID for a press on Remove', () => {
    const { props, row } = renderRow();

    fireEvent.click(within(row).getByRole('button', { name: 'Remove A' }));

    expect(props.onRemove).toHaveBeenCalledWith('a');
  });

  it('keeps the form fields and the enable switch (no function is lost)', () => {
    const { row } = renderRow({ scheduleType: 'Timer', timerTimeOfDay: '15:30', timerMode: 'timeOfDay', enabled: true });

    expect(within(row).getByRole('switch', { name: 'Disable A' })).toBeInTheDocument();
    expect(within(row).getByLabelText('Schedule type for A')).toBeInTheDocument();
    expect(within(row).getByLabelText('Timer time for A')).toBeInTheDocument();
  });
});

describe('Buttons of the template editor page (feature 184, FR-011)', () => {
  it('the scheduling areas show the Add button as an icon with a name and a tooltip', () => {
    const { container } = render(<QueueSchedulingAreas entries={[entry('a')]} entrySchedule={{}} {...baseProps()} />);

    const add = screen.getByRole('button', { name: 'Add' });
    expect(add).toHaveAttribute('title', 'Add sequence');
    expect(add.querySelector('svg')).not.toBeNull();
    // The dropdown of the Add control is a component of its own. It is not an action button.
    expect(buttonsWithText(container).filter((text) => text === 'Add')).toEqual([]);
  });

  it('the template controls show Save Template and Reload Template as icons with a name and a tooltip', () => {
    const { container } = render(
      <QueueTemplateControls
        associatedTemplateName="Daily"
        status="Stopped"
        onSaveTemplate={jest.fn().mockResolvedValue(undefined)}
        onLoadTemplate={jest.fn()}
        onReload={jest.fn()}
      />
    );

    const save = screen.getByRole('button', { name: 'Save Template' });
    const reload = screen.getByRole('button', { name: 'Reload Template' });
    expect(save).toHaveAttribute('title', 'Save changes to "Daily".');
    expect(reload).toHaveAttribute('title', 'Reload Template');
    expect(save.querySelector('svg')).not.toBeNull();
    expect(reload.querySelector('svg')).not.toBeNull();
    // The template name is a link-style button that shows the name. It is not an action button.
    expect(buttonsWithText(container)).toEqual(['Daily']);
  });

  it('the game controls show Unlink as an icon with a name and a tooltip', () => {
    const { container } = render(
      <QueueGameControls linkedGameId="g1" linkedGameName="Game" status="Stopped" onLink={jest.fn()} onUnlink={jest.fn()} />
    );

    const unlink = screen.getByRole('button', { name: 'Unlink' });
    expect(unlink).toHaveAttribute('title', 'Unlink');
    expect(unlink.querySelector('svg')).not.toBeNull();
    expect(buttonsWithText(container)).toEqual(['Game']);
  });
});

describe('Audit of the running queue editor (feature 184, FR-011, SC-005)', () => {
  const getTemplateMock = getQueueTemplate as jest.MockedFunction<typeof getQueueTemplate>;
  const listTemplatesMock = listQueueTemplates as jest.MockedFunction<typeof listQueueTemplates>;

  beforeEach(() => {
    jest.resetAllMocks();
    getTemplateMock.mockResolvedValue({
      id: 't1', name: 'Daily Farm', entryCount: 1, createdAt: null, updatedAt: null,
      entries: [{ entryId: 'te1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false, scheduleType: 'OncePerRun', timerTimeOfDay: null, timerRelativeOffset: null, enabled: true }],
    } as any);
    listTemplatesMock.mockResolvedValue([]);
  });

  it('has no action button with visible text, and each icon button has an accessible name and a tooltip', async () => {
    const queue = {
      id: 'q1', linkedTemplateId: 't1', linkedTemplateName: 'Daily Farm',
      entries: [{ entryId: 'r1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false }],
    };
    const { container } = render(<RunningQueueEditor queue={queue as any} sequences={sequences} />);
    await screen.findByRole('region', { name: 'Queue sequences' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Remove Alpha' })).toBeInTheDocument());

    expect(buttonsWithText(container)).toEqual([]);
    const buttons = Array.from(container.querySelectorAll('button'));
    expect(buttons.length).toBeGreaterThan(0);
    buttons.forEach((button) => {
      expect(button.getAttribute('aria-label') ?? '').not.toBe('');
      expect(button.getAttribute('title') ?? '').not.toBe('');
      expect(button.querySelector('svg')).not.toBeNull();
    });
  });
});
