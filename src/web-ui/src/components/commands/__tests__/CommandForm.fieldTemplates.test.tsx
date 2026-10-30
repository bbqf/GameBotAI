import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { CommandForm, CommandFormValue, StepEntry } from '../CommandForm';

jest.mock('@dnd-kit/core', () => {
  const React = jest.requireActual('react');
  return {
    DndContext: ({ children }: any) => React.createElement(React.Fragment, null, children),
    PointerSensor: class {},
    useSensor: jest.fn(),
    useSensors: jest.fn(() => []),
  };
});
jest.mock('@dnd-kit/sortable', () => ({
  SortableContext: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  verticalListSortingStrategy: {},
  useSortable: () => ({ attributes: {}, listeners: {}, setNodeRef: jest.fn(), transform: null, transition: undefined, isDragging: false }),
  arrayMove: jest.fn(),
}));
jest.mock('@dnd-kit/utilities', () => ({ CSS: { Translate: { toString: () => '' }, Transform: { toString: () => '' } } }));
jest.mock('../../images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ id, label, value, onChange }: any) => (
    <>
      {label && <label htmlFor={id}>{label}</label>}
      <input id={id} value={value} onChange={(e) => onChange(e.target.value)} />
    </>
  ),
}));

const swipeStep: StepEntry = {
  id: 'swipe-1',
  type: 'Swipe',
  swipe: { startX: '1', startY: '2', endX: '3', endY: '4' },
  fieldTemplates: {
    'swipe.durationMs': '{{delay}}',
    // No control has this key. The type change rule keeps it, because it is not a key of another type.
    'custom.key': '{{x}}',
  },
};

const form = (steps: StepEntry[]): CommandFormValue => ({
  name: 'C',
  steps,
  parameters: [{ name: 'delay', type: 'number', required: false }],
});

describe('CommandForm field templates (feature 169)', () => {
  it('keeps all keys when the type stays the same', () => {
    const onChange = jest.fn();
    render(<CommandForm value={form([swipeStep])} commandOptions={[]} onChange={onChange} />);
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    fireEvent.click(screen.getAllByRole('button', { name: 'Save' })[0]);
    const saved = onChange.mock.calls[0][0].steps[0] as StepEntry;
    expect(saved.type).toBe('Swipe');
    expect(saved.fieldTemplates).toEqual(swipeStep.fieldTemplates);
  });

  it('adds a new step of another type with no keys of the old type', () => {
    const onChange = jest.fn();
    render(<CommandForm value={form([swipeStep])} commandOptions={[]} onChange={onChange} />);
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    // A change of the type in the selector starts a new step. The old step keeps its own keys.
    fireEvent.change(screen.getByRole('combobox', { name: /action type/i }), { target: { value: 'PrimitiveTap' } });
    fireEvent.change(screen.getByLabelText('Reference image *'), { target: { value: 'img-1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    const steps = onChange.mock.calls[0][0].steps as StepEntry[];
    expect(steps[0].fieldTemplates).toEqual(swipeStep.fieldTemplates);
    expect(steps[1].type).toBe('PrimitiveTap');
    expect(steps[1].fieldTemplates).toBeUndefined();
  });

  it('drops the keys of another step type when a step changes its type', () => {
    // The panels never turn a step into another type, so the rule has a guard in the save of the form.
    const onChange = jest.fn();
    const keyStep: StepEntry = {
      id: 'k1',
      type: 'KeyInput',
      keyInput: { key: 'HOME' },
      fieldTemplates: { 'swipe.startX': '{{delay}}', 'custom.key': '{{x}}' },
    };
    render(<CommandForm value={form([keyStep])} commandOptions={[]} onChange={onChange} />);
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    fireEvent.click(screen.getAllByRole('button', { name: 'Save' })[0]);
    const saved = onChange.mock.calls[0][0].steps[0] as StepEntry;
    expect(saved.fieldTemplates).toEqual({ 'custom.key': '{{x}}' });
  });
});
