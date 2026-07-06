import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { CommandForm, CommandFormValue, StepEntry } from '../CommandForm';

// ── DnD mocks ─────────────────────────────────────────────────────────────────

jest.mock('@dnd-kit/core', () => {
  const React = jest.requireActual('react');
  const MockDndContext = jest.fn(({ children }: any) =>
    React.createElement(React.Fragment, null, children)
  );
  return {
    DndContext: MockDndContext,
    PointerSensor: class {},
    useSensor: jest.fn(),
    useSensors: jest.fn(() => []),
  };
});

jest.mock('@dnd-kit/sortable', () => ({
  SortableContext: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  verticalListSortingStrategy: {},
  useSortable: () => ({
    attributes: {},
    listeners: {},
    setNodeRef: jest.fn(),
    transform: null,
    transition: undefined,
    isDragging: false,
  }),
  arrayMove: jest.fn((arr: unknown[], from: number, to: number) => {
    const result = [...arr];
    const [item] = result.splice(from, 1);
    result.splice(to, 0, item);
    return result;
  }),
}));

jest.mock('@dnd-kit/utilities', () => ({
  CSS: { Translate: { toString: () => '' }, Transform: { toString: () => '' } },
}));

// ── ImageSelectorDropdown mock ────────────────────────────────────────────────

jest.mock('../../images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ value, onChange, onStaleChange: _onStaleChange, error, label, id }: any) => (
    <div>
      {label && <label htmlFor={id ?? 'mock-img'}>{label}</label>}
      <input
        id={id ?? 'mock-img'}
        data-testid={`image-input-${id ?? 'default'}`}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
      {error && <div role="alert">{error}</div>}
    </div>
  ),
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

const tapStep: StepEntry = {
  id: 'tap-1',
  type: 'PrimitiveTap',
  primitiveTap: { detectionTarget: { referenceImageId: 'img-a', offsetX: '0', offsetY: '0' } },
};

const waitStep: StepEntry = {
  id: 'wait-1',
  type: 'WaitForImage',
  waitForImage: { timeoutMs: '2000', detectionTarget: { referenceImageId: 'img-b', confidence: '0.8' } },
};

const ensureStep: StepEntry = {
  id: 'ensure-1',
  type: 'EnsureGameRunning',
};

const commandStep: StepEntry = {
  id: 'cmd-1',
  type: 'Command',
  targetId: 'other-cmd',
};

const emptyForm: CommandFormValue = { name: 'My Command', steps: [] };

const getSelector = () => screen.getByRole('combobox', { name: /action type/i });

// ── US1: Selector flow and scaffolding ────────────────────────────────────────

describe('CommandForm — US1: selector and scaffolding', () => {
  it('renders the action type selector', () => {
    render(<CommandForm value={emptyForm} commandOptions={[]} onChange={() => {}} />);
    expect(getSelector()).toBeInTheDocument();
  });

  it('selector lists exactly Tap, Wait for Image, Ensure Game Running (no Command)', () => {
    render(<CommandForm value={emptyForm} commandOptions={[]} onChange={() => {}} />);
    const options = Array.from((getSelector() as HTMLSelectElement).options).map((o) => o.text);
    expect(options).toContain('Tap');
    expect(options).toContain('Wait for Image');
    expect(options).toContain('Ensure Game Running');
    expect(options.some((t) => /command/i.test(t) && !/ensure/i.test(t))).toBe(false);
  });

  it('shows no "Add command" button (FR-001, SC-002)', () => {
    render(<CommandForm value={emptyForm} commandOptions={[]} onChange={() => {}} />);
    expect(screen.queryByText(/add command step/i)).toBeNull();
    expect(screen.queryByText(/add command/i)).toBeNull();
  });

  it('shows no panel when selector is blank', () => {
    render(<CommandForm value={emptyForm} commandOptions={[]} onChange={() => {}} />);
    expect(screen.queryByText(/reference image \*/i)).toBeNull();
    expect(screen.queryByText(/timeout/i)).toBeNull();
    expect(screen.queryByText(/checks that the game/i)).toBeNull();
  });

  it('shows Tap panel when PrimitiveTap is selected', () => {
    render(<CommandForm value={emptyForm} commandOptions={[]} onChange={() => {}} />);
    fireEvent.change(getSelector(), { target: { value: 'PrimitiveTap' } });
    expect(screen.getByText(/reference image \*/i)).toBeInTheDocument();
  });

  it('shows Wait for Image panel when WaitForImage is selected', () => {
    render(<CommandForm value={emptyForm} commandOptions={[]} onChange={() => {}} />);
    fireEvent.change(getSelector(), { target: { value: 'WaitForImage' } });
    expect(screen.getByLabelText(/timeout \(ms\)/i)).toBeInTheDocument();
  });

  it('shows Ensure Game Running panel when EnsureGameRunning is selected', () => {
    render(<CommandForm value={emptyForm} commandOptions={[]} onChange={() => {}} />);
    fireEvent.change(getSelector(), { target: { value: 'EnsureGameRunning' } });
    expect(screen.getByText(/checks that the game is in the foreground/i)).toBeInTheDocument();
  });

  it('switching action type closes the previous panel (FR-006, SC-005)', () => {
    render(<CommandForm value={emptyForm} commandOptions={[]} onChange={() => {}} />);
    fireEvent.change(getSelector(), { target: { value: 'PrimitiveTap' } });
    expect(screen.getByText(/reference image \*/i)).toBeInTheDocument();
    fireEvent.change(getSelector(), { target: { value: 'WaitForImage' } });
    expect(screen.queryByText(/reference image \*/i)).toBeNull();
    expect(screen.getByLabelText(/timeout \(ms\)/i)).toBeInTheDocument();
  });

  it('switching action type resets editingStepId so no old initialValue leaks', () => {
    const onChange = jest.fn();
    render(
      <CommandForm
        value={{ ...emptyForm, steps: [tapStep] }}
        commandOptions={[]}
        onChange={onChange}
      />
    );
    // Open Tap panel in edit mode for tapStep
    fireEvent.click(screen.getAllByText('Edit')[0]);
    expect(getSelector()).toHaveValue('PrimitiveTap');

    // Switch to WaitForImage → should show empty panel (no pre-filled timeout from tapStep)
    fireEvent.change(getSelector(), { target: { value: 'WaitForImage' } });
    const timeoutInput = screen.getByLabelText(/timeout \(ms\)/i);
    // Default timeout is 1000 (fresh panel, not an old editingStep's value)
    expect(timeoutInput).toHaveValue(1000);
  });

  it('existing Command-type step still renders in the step list (FR-010)', () => {
    render(
      <CommandForm
        value={{ ...emptyForm, steps: [commandStep] }}
        commandOptions={[{ value: 'other-cmd', label: 'Other Cmd' }]}
        onChange={() => {}}
      />
    );
    expect(screen.getByText(/Command: Other Cmd/)).toBeInTheDocument();
  });

  it('Delete button fires for Command-type step (FR-010)', () => {
    const onChange = jest.fn();
    render(
      <CommandForm
        value={{ ...emptyForm, steps: [commandStep] }}
        commandOptions={[{ value: 'other-cmd', label: 'Other Cmd' }]}
        onChange={onChange}
      />
    );
    fireEvent.click(screen.getByText('Delete'));
    expect(onChange).toHaveBeenCalledWith({ ...emptyForm, steps: [] });
  });

  it('clicking Edit on a Command-type step does NOT open any panel (I2 guard)', () => {
    render(
      <CommandForm
        value={{ ...emptyForm, steps: [commandStep] }}
        commandOptions={[{ value: 'other-cmd', label: 'Other Cmd' }]}
        onChange={() => {}}
      />
    );
    fireEvent.click(screen.getByText('Edit'));
    expect(getSelector()).toHaveValue('');
    expect(screen.queryByText(/reference image \*/i)).toBeNull();
    expect(screen.queryByText(/timeout/i)).toBeNull();
    expect(screen.queryByText(/checks that the game/i)).toBeNull();
  });

  it('step list labels read "Tap: …" not "Primitive tap: …" (plan C5)', () => {
    render(
      <CommandForm
        value={{ ...emptyForm, steps: [tapStep] }}
        commandOptions={[]}
        onChange={() => {}}
      />
    );
    expect(screen.getByText(/^Tap: img-a/)).toBeInTheDocument();
    expect(screen.queryByText(/primitive tap/i)).toBeNull();
  });
});

// ── US2: Tap panel — add and edit flow ────────────────────────────────────────

describe('CommandForm — US2: Tap panel add and edit', () => {
  it('adding a Tap step calls onChange with PrimitiveTap step and resets selector (FR-009, SC-006)', () => {
    const onChange = jest.fn();
    render(<CommandForm value={emptyForm} commandOptions={[]} onChange={onChange} />);
    fireEvent.change(getSelector(), { target: { value: 'PrimitiveTap' } });
    fireEvent.change(screen.getByTestId('image-input-tap-panel-image'), { target: { value: 'img-x' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onChange).toHaveBeenCalled();
    const updated: CommandFormValue = onChange.mock.calls[0][0];
    expect(updated.steps).toHaveLength(1);
    expect(updated.steps[0].type).toBe('PrimitiveTap');
    expect(updated.steps[0].primitiveTap?.detectionTarget.referenceImageId).toBe('img-x');
    // Selector should reset to blank
    expect(getSelector()).toHaveValue('');
  });

  it('clicking Edit on a Tap step opens the panel pre-filled (FR-011, SC-007)', () => {
    render(
      <CommandForm
        value={{ ...emptyForm, steps: [tapStep] }}
        commandOptions={[]}
        onChange={() => {}}
      />
    );
    fireEvent.click(screen.getAllByText('Edit')[0]);
    expect(getSelector()).toHaveValue('PrimitiveTap');
    const imageInput = screen.getByTestId('image-input-tap-panel-image');
    expect(imageInput).toHaveValue('img-a');
  });

  it('saving an edited Tap step updates it in-place (SC-007)', () => {
    const onChange = jest.fn();
    render(
      <CommandForm
        value={{ ...emptyForm, steps: [tapStep] }}
        commandOptions={[]}
        onChange={onChange}
      />
    );
    fireEvent.click(screen.getAllByText('Edit')[0]);
    fireEvent.change(screen.getByTestId('image-input-tap-panel-image'), { target: { value: 'img-new' } });
    // Panel Save button is type="button"; form Submit Save is type="submit" — take the first
    fireEvent.click(screen.getAllByRole('button', { name: 'Save' })[0]);
    const updated: CommandFormValue = onChange.mock.calls[0][0];
    expect(updated.steps).toHaveLength(1);
    expect(updated.steps[0].id).toBe('tap-1');
    expect(updated.steps[0].primitiveTap?.detectionTarget.referenceImageId).toBe('img-new');
    expect(getSelector()).toHaveValue('');
  });
});

// ── US3: Wait for Image panel — add and edit flow ─────────────────────────────

describe('CommandForm — US3: Wait for Image panel add and edit', () => {
  it('adding a WaitForImage step (timeout only) calls onChange and resets selector', () => {
    const onChange = jest.fn();
    render(<CommandForm value={emptyForm} commandOptions={[]} onChange={onChange} />);
    fireEvent.change(getSelector(), { target: { value: 'WaitForImage' } });
    fireEvent.change(screen.getByLabelText(/timeout \(ms\)/i), { target: { value: '3000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    const updated: CommandFormValue = onChange.mock.calls[0][0];
    expect(updated.steps[0].type).toBe('WaitForImage');
    expect(updated.steps[0].waitForImage?.timeoutMs).toBe('3000');
    expect(updated.steps[0].waitForImage?.detectionTarget).toBeUndefined();
    expect(getSelector()).toHaveValue('');
  });

  it('clicking Edit on a WaitForImage step opens the panel pre-filled', () => {
    render(
      <CommandForm
        value={{ ...emptyForm, steps: [waitStep] }}
        commandOptions={[]}
        onChange={() => {}}
      />
    );
    fireEvent.click(screen.getAllByText('Edit')[0]);
    expect(getSelector()).toHaveValue('WaitForImage');
    expect(screen.getByLabelText(/timeout \(ms\)/i)).toHaveValue(2000);
    expect(screen.getByTestId('image-input-wfi-panel-image')).toHaveValue('img-b');
  });

  it('saving an edited WaitForImage step updates it in-place', () => {
    const onChange = jest.fn();
    render(
      <CommandForm
        value={{ ...emptyForm, steps: [waitStep] }}
        commandOptions={[]}
        onChange={onChange}
      />
    );
    fireEvent.click(screen.getAllByText('Edit')[0]);
    fireEvent.change(screen.getByLabelText(/timeout \(ms\)/i), { target: { value: '9000' } });
    // Panel Save button is type="button"; form Submit Save is type="submit" — take the first
    fireEvent.click(screen.getAllByRole('button', { name: 'Save' })[0]);
    const updated: CommandFormValue = onChange.mock.calls[0][0];
    expect(updated.steps[0].id).toBe('wait-1');
    expect(updated.steps[0].waitForImage?.timeoutMs).toBe('9000');
    expect(getSelector()).toHaveValue('');
  });
});

// ── US4: Ensure Game Running panel — add flow ─────────────────────────────────

describe('CommandForm — US4: Ensure Game Running panel add', () => {
  it('adding EnsureGameRunning step calls onChange with correct type and resets selector', () => {
    const onChange = jest.fn();
    render(<CommandForm value={emptyForm} commandOptions={[]} onChange={onChange} />);
    fireEvent.change(getSelector(), { target: { value: 'EnsureGameRunning' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    const updated: CommandFormValue = onChange.mock.calls[0][0];
    expect(updated.steps[0].type).toBe('EnsureGameRunning');
    expect(getSelector()).toHaveValue('');
  });

  it('panel cancel resets selector to blank without adding a step', () => {
    const onChange = jest.fn();
    render(<CommandForm value={emptyForm} commandOptions={[]} onChange={onChange} />);
    fireEvent.change(getSelector(), { target: { value: 'EnsureGameRunning' } });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onChange).not.toHaveBeenCalled();
    expect(getSelector()).toHaveValue('');
  });

  it('clicking Edit on an EnsureGameRunning step opens the panel', () => {
    render(
      <CommandForm
        value={{ ...emptyForm, steps: [ensureStep] }}
        commandOptions={[]}
        onChange={() => {}}
      />
    );
    fireEvent.click(screen.getAllByText('Edit')[0]);
    expect(getSelector()).toHaveValue('EnsureGameRunning');
    expect(screen.getByText(/checks that the game is in the foreground/i)).toBeInTheDocument();
  });
});
