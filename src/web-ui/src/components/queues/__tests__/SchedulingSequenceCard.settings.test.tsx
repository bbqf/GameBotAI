import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { DndContext } from '@dnd-kit/core';
import { SortableContext } from '@dnd-kit/sortable';
import { SchedulingSequenceCard } from '../SchedulingSequenceCard';
import type { SchedulingCard } from '../schedulingAreas';

const card = (over: Partial<SchedulingCard['schedule']> = {}, enabled = true): SchedulingCard => ({
  entryId: 'e1',
  sequenceId: 's1',
  label: 'Mine',
  stale: false,
  enabled,
  schedule: { scheduleType: 'OncePerRun', timerTimeOfDay: '', ...over },
});

const renderCard = (props: Partial<React.ComponentProps<typeof SchedulingSequenceCard>> = {}, c = card()) =>
  render(
    <DndContext>
      <SortableContext items={[c.entryId]}>
        <SchedulingSequenceCard
          card={c}
          areaId="oncePerRun"
          onRemove={jest.fn()}
          onReassign={jest.fn()}
          onToggleEnabled={jest.fn()}
          onTimerTimeChange={jest.fn()}
          onTimerModeChange={jest.fn()}
          onTimerRelativeOffsetChange={jest.fn()}
          onParameterValuesChange={jest.fn()}
          {...props}
        />
      </SortableContext>
    </DndContext>
  );

describe('SchedulingSequenceCard main row (feature 185, rules D-01 to D-03, D-08, D-09, D-12)', () => {
  it('has no schedule select and no timer input on the main row', () => {
    renderCard({}, card({ scheduleType: 'Timer', timerTimeOfDay: '14:30' }));

    expect(screen.queryByLabelText('Schedule type for Mine')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Timer mode for Mine')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Timer time for Mine')).not.toBeInTheDocument();
  });

  it('has the settings button with its name, a title, no text, and aria-expanded', () => {
    renderCard();

    const button = screen.getByRole('button', { name: 'Entry settings for Mine' });
    expect(button).toHaveAttribute('title', 'Entry settings');
    expect(button).toHaveAttribute('aria-expanded', 'false');
    expect(button.textContent).toBe('');
  });

  it('has no Parameters button any more', () => {
    renderCard();

    expect(screen.queryByRole('button', { name: 'Parameters for Mine' })).not.toBeInTheDocument();
  });

  it('is closed at the start and shows no panel', () => {
    renderCard();

    expect(screen.queryByTestId('entry-settings-panel')).not.toBeInTheDocument();
  });

  it('asks the editor to toggle its own entry', () => {
    const onToggleOpen = jest.fn();
    renderCard({ onToggleOpen });

    fireEvent.click(screen.getByRole('button', { name: 'Entry settings for Mine' }));

    expect(onToggleOpen).toHaveBeenCalledTimes(1);
    expect(onToggleOpen).toHaveBeenCalledWith('e1');
  });

  it('shows the panel and sets aria-expanded when it is open', () => {
    renderCard({ open: true });

    expect(screen.getByRole('button', { name: 'Entry settings for Mine' })).toHaveAttribute('aria-expanded', 'true');
    const panel = screen.getByTestId('entry-settings-panel');
    expect(within(panel).getByLabelText('Schedule type for Mine')).toBeInTheDocument();
  });

  it('keeps the settings button on when the editor is disabled', () => {
    renderCard({ disabled: true });

    expect(screen.getByRole('button', { name: 'Entry settings for Mine' })).toBeEnabled();
  });

  it('shows the schedule summary for each kind of schedule', () => {
    const { unmount } = renderCard({}, card({ scheduleType: 'OncePerRun' }));
    expect(screen.getByTestId('schedule-summary')).toHaveTextContent('Once each run');
    unmount();

    const timeOfDay = renderCard({ areaId: 'scheduled' }, card({ scheduleType: 'Timer', timerTimeOfDay: '14:30' }));
    expect(screen.getByTestId('schedule-summary')).toHaveTextContent('Daily at 14:30');
    timeOfDay.unmount();

    renderCard({ areaId: 'scheduled' }, card({ scheduleType: 'Timer', timerMode: 'relative', timerRelativeOffset: '01:00:00' }));
    expect(screen.getByTestId('schedule-summary')).toHaveTextContent('Every 01:00:00');
  });

  it('lets the timer mode decide the summary when an old value stays in the other field', () => {
    renderCard(
      { areaId: 'scheduled' },
      card({ scheduleType: 'Timer', timerMode: 'timeOfDay', timerTimeOfDay: '08:00', timerRelativeOffset: '00:10:00' })
    );

    expect(screen.getByTestId('schedule-summary')).toHaveTextContent('Daily at 08:00');
  });

  it('follows the schedule when the card gets a new schedule', () => {
    const view = renderCard({ areaId: 'scheduled' }, card({ scheduleType: 'Timer', timerTimeOfDay: '14:30' }));
    expect(screen.getByTestId('schedule-summary')).toHaveTextContent('Daily at 14:30');

    view.rerender(
      <DndContext>
        <SortableContext items={['e1']}>
          <SchedulingSequenceCard
            card={card({ scheduleType: 'Timer', timerTimeOfDay: '09:15' })}
            areaId="scheduled"
            onRemove={jest.fn()}
            onReassign={jest.fn()}
          />
        </SortableContext>
      </DndContext>
    );

    expect(screen.getByTestId('schedule-summary')).toHaveTextContent('Daily at 09:15');
  });

  it('shows the Parameters badge on a closed card that has values', () => {
    renderCard({}, card({ parameterValues: [{ name: 'a', value: '1' }] }));

    expect(screen.getByLabelText('Has parameter overrides')).toBeInTheDocument();
  });

  it('has at most 5 controls on the main row (SC-001)', () => {
    renderCard({ runNext: { queueRunning: true, onRunNext: jest.fn() } as never });

    const row = screen.getByTestId('queue-entry').querySelector('.scheduling-card__row') as HTMLElement;
    const controls = row.querySelectorAll('button, input, select, textarea, [role="switch"]');
    // Handle (a span with drag attributes) is not a form control, so the count of form controls is 4 at most.
    expect(controls.length).toBeLessThanOrEqual(4);
    expect(row.querySelectorAll('[aria-label="Drag to reorder"]').length + controls.length).toBeLessThanOrEqual(5);
  });
});
