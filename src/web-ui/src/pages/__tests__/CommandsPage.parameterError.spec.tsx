import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { CommandsPage } from '../CommandsPage';
import { ApiError } from '../../lib/api';
import { resetPageMocks, serveCommand, updateCommandMock } from './commandsPageTestSetup';

jest.mock('../../services/commands');
jest.mock('../../services/games', () => ({ listGames: jest.fn() }));
jest.mock('@dnd-kit/core', () => ({
  DndContext: jest.fn(),
  PointerSensor: class {},
  useSensor: jest.fn(),
  useSensors: jest.fn(),
}));
jest.mock('@dnd-kit/sortable', () => ({
  SortableContext: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  verticalListSortingStrategy: {},
  useSortable: () => ({ attributes: {}, listeners: {}, setNodeRef: jest.fn(), transform: null, transition: undefined, isDragging: false }),
  arrayMove: jest.fn(),
}));
jest.mock('@dnd-kit/utilities', () => ({ CSS: { Translate: { toString: () => '' }, Transform: { toString: () => '' } } }));
jest.mock('../../components/images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ id, label, value, onChange }: any) => (
    <>
      {label && <label htmlFor={id}>{label}</label>}
      <input id={id} value={value} onChange={(e) => onChange(e.target.value)} />
    </>
  ),
}));

const PATH = 'swipe.durationMs';
const messageFor = (order: number) =>
  `Step ${order}: parameter 'label' gives text, but field '${PATH}' needs a number. Use a number parameter.`;

const rejection = (message: string) =>
  new ApiError(400, 'Parameter error', undefined, {
    error: 'text_parameter_in_numeric_field',
    details: [{ code: 'text_parameter_in_numeric_field', message, fieldPath: PATH, parameterName: 'label' }],
  });

const swipe = (order: number, withTemplate: boolean) => ({
  type: 'Swipe',
  order,
  swipe: { startX: 1, startY: 2, endX: 3, endY: 4, durationMs: 10 },
  ...(withTemplate ? { fieldTemplates: { [PATH]: '{{label}}' } } : {}),
});

describe('CommandsPage service error at the field (feature 169)', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    resetPageMocks();
    serveCommand({
      id: 'c1',
      name: 'Cmd',
      steps: [swipe(0, false), swipe(1, true)],
      parameters: [{ name: 'label', type: 'text', required: false }],
    });
  });

  const openAndSave = async () => {
    render(<CommandsPage />);
    await screen.findByText('Cmd');
    fireEvent.click(screen.getByText('Cmd'));
    await screen.findByText('Edit Command');
    fireEvent.click(screen.getAllByText('Save')[0]);
  };

  it('shows the message at the field of the named step only, and in the form summary', async () => {
    updateCommandMock.mockRejectedValue(rejection(messageFor(1)));
    await openAndSave();

    const panel = await waitFor(() => {
      const el = document.querySelector('.action-panel--swipe') as HTMLElement | null;
      expect(el).not.toBeNull();
      return el as HTMLElement;
    });
    // The panel of step 1 (the second step) is open and shows the message at the duration control.
    expect(within(panel).getByRole('alert')).toHaveTextContent(messageFor(1));
    expect(within(panel).getByTestId(`chip-${PATH}`)).toHaveTextContent('label');
    // The form summary shows the same message, with the field and the parameter name.
    const summary = [screen.getByTestId('command-form-summary')];
    expect(summary[0]).toHaveTextContent(messageFor(1));
    expect(summary[0].textContent).toContain(PATH);
    expect(summary[0].textContent).toContain("'label'");
  });

  it('shows no field message for the other step', async () => {
    updateCommandMock.mockRejectedValue(rejection(messageFor(1)));
    await openAndSave();
    await waitFor(() => expect(document.querySelector('.action-panel--swipe')).not.toBeNull());
    // Only one panel is open and only one field alert exists inside the panels.
    const panelAlerts = within(document.querySelector('.action-panel--swipe') as HTMLElement).getAllByRole('alert');
    expect(panelAlerts).toHaveLength(1);
    expect(document.querySelectorAll('.action-panel--swipe')).toHaveLength(1);
  });

  it('shows the message in the form summary only when the step is unknown', async () => {
    updateCommandMock.mockRejectedValue(rejection(messageFor(7)));
    await openAndSave();

    expect(await screen.findByTestId('command-form-summary')).toHaveTextContent(messageFor(7));
    expect(document.querySelector('.action-panel--swipe')).toBeNull();
  });
});
