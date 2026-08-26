import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { QueueSchedulingAreas } from '../QueueSchedulingAreas';
import { EntrySchedule } from '../QueueEntryList';
import { QueueEntryDto } from '../../../services/queues';
import { SequenceDto } from '../../../services/sequences';

/**
 * Feature 078: the per-entry parameter binding form.
 *
 * The form and its card existed and were unit-tested in isolation, but SchedulingArea never accepted
 * or forwarded the two props that reveal them, so the panel was unreachable in the running app — the
 * declarations never travelled from the sequence list down to the card. These tests exercise the
 * whole chain from QueueSchedulingAreas down, which is where the break was.
 */
const sequences = [
  {
    id: 'seq-pit',
    name: 'PNS Pit Ensure Mining',
    steps: [],
    parameters: [
      { name: 'sectionRowY', type: 'number', default: '569', required: false, description: 'Y of the Enter Field row.' },
    ],
  },
  { id: 'seq-plain', name: 'Plain', steps: [] },
] as unknown as SequenceDto[];

const entries: QueueEntryDto[] = [
  { entryId: 'e1', sequenceId: 'seq-pit', sequenceName: 'PNS Pit Ensure Mining', stale: false },
  { entryId: 'e2', sequenceId: 'seq-plain', sequenceName: 'Plain', stale: false },
];

const entrySchedule: Record<string, EntrySchedule> = {
  e1: { scheduleType: 'OncePerRun', timerTimeOfDay: '' },
  e2: { scheduleType: 'OncePerRun', timerTimeOfDay: '' },
};

const baseProps = () => ({
  sequences,
  onAdd: jest.fn(),
  onRemove: jest.fn(),
  onReorderAndReassign: jest.fn(),
});

const renderAreas = (over: Record<string, unknown> = {}) =>
  render(
    <QueueSchedulingAreas
      entries={entries}
      entrySchedule={entrySchedule}
      {...baseProps()}
      onParameterValuesChange={jest.fn()}
      {...over}
    />,
  );

describe('QueueSchedulingAreas per-entry parameters', () => {
  it('offers a Parameters panel on each entry once a change handler is supplied', () => {
    renderAreas();
    expect(screen.getByLabelText('Parameters for PNS Pit Ensure Mining')).toBeInTheDocument();
  });

  it('omits the panel entirely when no handler is supplied', () => {
    // Guards the opposite direction: a caller that does not persist values must not offer the UI.
    renderAreas({ onParameterValuesChange: undefined });
    expect(screen.queryByLabelText('Parameters for PNS Pit Ensure Mining')).not.toBeInTheDocument();
  });

  it("shows the referenced sequence's declarations, which is what used to never arrive", () => {
    renderAreas();
    fireEvent.click(screen.getByLabelText('Parameters for PNS Pit Ensure Mining'));
    expect(screen.getByText('sectionRowY')).toBeInTheDocument();
  });

  it('reports a supplied value for the entry it was entered on', () => {
    const onParameterValuesChange = jest.fn();
    renderAreas({ onParameterValuesChange });

    fireEvent.click(screen.getByLabelText('Parameters for PNS Pit Ensure Mining'));
    const panel = screen.getByLabelText('Parameters for PNS Pit Ensure Mining').closest('.scheduling-card');
    const scope = within(panel as HTMLElement);

    // Every row starts on "inherit" with its value disabled, so the common case is zero clicks;
    // supplying a value means opting out of inheritance first.
    expect(scope.getByLabelText('Value for sectionRowY')).toBeDisabled();
    fireEvent.click(scope.getByLabelText('Inherit sectionRowY'));
    fireEvent.change(scope.getByLabelText('Value for sectionRowY'), { target: { value: '631' } });

    expect(onParameterValuesChange).toHaveBeenCalledWith(
      'e1',
      expect.arrayContaining([expect.objectContaining({ name: 'sectionRowY', value: '631' })]),
    );
  });

  it('still offers the panel for a sequence that declares nothing, so ad-hoc values are reachable', () => {
    // FR-012a: an ad-hoc name on an entry reaches a command at any depth, so an undeclared
    // sequence must not hide the panel.
    renderAreas();
    expect(screen.getByLabelText('Parameters for Plain')).toBeInTheDocument();
  });
});
