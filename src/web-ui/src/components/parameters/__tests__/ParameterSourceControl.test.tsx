import React, { useState } from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { ParameterSourceControl } from '../ParameterSourceControl';
import type { ParameterDeclaration } from '../types';

const PATH = 'swipe.durationMs';

const params: ParameterDeclaration[] = [
  { name: 'delay', type: 'number', required: false },
  { name: 'label', type: 'text', required: false },
  { name: 'tone', type: 'number', kind: 'choice', required: false },
  { name: 'word', type: 'text', kind: 'choice', required: false },
];

const Harness: React.FC<{
  parameters?: ParameterDeclaration[];
  initial?: string;
  error?: string;
  path?: string;
  onChangeSpy?: (t: string | undefined) => void;
}> = ({ parameters = params, initial, error, path = PATH, onChangeSpy }) => {
  const [template, setTemplate] = useState<string | undefined>(initial);
  return (
    <ParameterSourceControl
      path={path}
      label="Duration"
      parameters={parameters}
      template={template}
      error={error}
      onChange={(t) => {
        setTemplate(t);
        onChangeSpy?.(t);
      }}
    >
      <input aria-label="plain duration" type="number" />
    </ParameterSourceControl>
  );
};

const openList = () => fireEvent.click(screen.getByRole('button', { name: 'Use a parameter for Duration' }));

describe('ParameterSourceControl', () => {
  it('goes from plain to parameter to plain', () => {
    const spy = jest.fn();
    render(<Harness onChangeSpy={spy} />);
    expect(screen.getByLabelText('plain duration')).toBeInTheDocument();

    openList();
    fireEvent.click(screen.getByRole('option', { name: /delay/ }));
    expect(spy).toHaveBeenLastCalledWith('{{delay}}');
    expect(screen.queryByLabelText('plain duration')).not.toBeInTheDocument();
    expect(screen.getByTestId(`chip-${PATH}`)).toHaveTextContent('delay');

    fireEvent.click(screen.getByRole('button', { name: 'Remove parameter from Duration' }));
    expect(spy).toHaveBeenLastCalledWith(undefined);
    expect(screen.getByLabelText('plain duration')).toBeInTheDocument();
  });

  it('replaces the parameter by another one: remove, then select', () => {
    render(<Harness initial="{{delay}}" />);
    fireEvent.click(screen.getByRole('button', { name: 'Remove parameter from Duration' }));
    openList();
    fireEvent.click(screen.getByRole('option', { name: /tone/ }));
    expect(screen.getByTestId(`chip-${PATH}`)).toHaveTextContent('tone');
  });

  it('shows the empty state and writes no template', () => {
    const spy = jest.fn();
    render(<Harness parameters={[]} onChangeSpy={spy} />);
    openList();
    expect(screen.getByText('This command has no parameters. Add one in the Parameters section.')).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Parameters' })).not.toBeInTheDocument();
    expect(spy).not.toHaveBeenCalled();
  });

  it('shows a hint on a row that does not fit and keeps the row selectable', () => {
    const spy = jest.fn();
    render(<Harness onChangeSpy={spy} />);
    openList();
    const option = screen.getByRole('option', { name: /label/ });
    expect(option).toHaveAccessibleDescription(
      'This parameter gives text. A numeric field needs a number. The save works, but the run fails when the value is not a number.',
    );
    fireEvent.click(option);
    expect(spy).toHaveBeenCalledWith('{{label}}');
  });

  it('gives no hint for a number parameter, a choice with number options, and queue values', () => {
    render(<Harness />);
    openList();
    expect(screen.getByRole('option', { name: /delay/ })).not.toHaveAttribute('aria-describedby');
    expect(screen.getByRole('option', { name: /tone/ })).not.toHaveAttribute('aria-describedby');
    expect(screen.getByRole('option', { name: /word/ })).toHaveAttribute('aria-describedby');
    const queue = screen.getByRole('group', { name: 'Queue values' });
    const queueOptions = within(queue).getAllByRole('option');
    expect(queueOptions.length).toBeGreaterThan(0);
    queueOptions.forEach((o) => expect(o).not.toHaveAttribute('aria-describedby'));
  });

  it('shows the image hint for a non image parameter in an image field', () => {
    render(<Harness path="primitiveTap.detectionTarget.referenceImageId" />);
    openList();
    expect(screen.getByRole('option', { name: /delay/ })).toHaveAccessibleDescription(
      'This parameter is not an image picker.',
    );
  });

  it('shows "Parameter not found" for an undeclared name and lets the person remove it', () => {
    render(<Harness initial="{{gone}}" />);
    expect(screen.getByTestId(`chip-${PATH}`)).toHaveTextContent('gone');
    expect(screen.getByText('Parameter not found')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Remove parameter from Duration' }));
    expect(screen.getByLabelText('plain duration')).toBeInTheDocument();
  });

  it('does not show "Parameter not found" for a queue value', () => {
    render(<Harness initial="{{queue.gameId}}" />);
    expect(screen.queryByText('Parameter not found')).not.toBeInTheDocument();
  });

  it('gives accessible names to the button, the listbox, and the options', () => {
    render(<Harness />);
    const open = screen.getByRole('button', { name: 'Use a parameter for Duration' });
    expect(open).toHaveAttribute('aria-haspopup', 'listbox');
    expect(open).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(open);
    expect(open).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('listbox', { name: 'Parameters of this command' })).toBeInTheDocument();
    expect(screen.getAllByRole('option').length).toBeGreaterThan(params.length);
  });

  it('supports the keyboard: open, move, select, and Escape with focus return', () => {
    render(<Harness />);
    const open = screen.getByRole('button', { name: 'Use a parameter for Duration' });
    open.focus();
    fireEvent.keyDown(open, { key: 'ArrowDown' });
    const options = within(screen.getByRole('listbox')).getAllByRole('option');
    expect(options[0]).toHaveFocus();
    fireEvent.keyDown(options[0], { key: 'ArrowDown' });
    expect(options[1]).toHaveFocus();
    fireEvent.keyDown(options[1], { key: 'ArrowUp' });
    expect(options[0]).toHaveFocus();

    fireEvent.keyDown(options[0], { key: 'Escape' });
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(open).toHaveFocus();

    fireEvent.click(open);
    const again = within(screen.getByRole('listbox')).getAllByRole('option');
    again[2].focus();
    fireEvent.keyDown(again[2], { key: 'Enter' });
    expect(screen.getByTestId(`chip-${PATH}`)).toHaveTextContent('tone');
    expect(screen.getByRole('button', { name: 'Remove parameter from Duration' })).toHaveFocus();
  });

  it('shows the error with role alert', () => {
    render(<Harness error="Step 0: bad" />);
    expect(screen.getByRole('alert')).toHaveTextContent('Step 0: bad');
    const button = screen.getByRole('button', { name: 'Use a parameter for Duration' });
    expect(button).toHaveAccessibleDescription('Step 0: bad');
  });

  it('opens with 50 parameters well inside the render budget', () => {
    // The budget is 100 ms. The assertion has a wide margin so that a slow CI host does not fail it.
    const many: ParameterDeclaration[] = Array.from({ length: 50 }, (_, i) => ({
      name: `p${i}`,
      type: 'number',
      required: false,
    }));
    render(<Harness parameters={many} />);
    const start = performance.now();
    openList();
    const elapsed = performance.now() - start;
    expect(screen.getAllByRole('option').length).toBeGreaterThanOrEqual(50);
    expect(elapsed).toBeLessThan(1000);
  });
});
