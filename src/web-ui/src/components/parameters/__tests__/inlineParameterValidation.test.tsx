import React from 'react';
import { render, screen } from '@testing-library/react';
import { ParameterizableField } from '../ParameterizableField';
import { buildEditorScope, originLayerLabel } from '../types';
import type { ParameterDeclaration } from '../types';

/**
 * Feature 078 (FR-029): parameter problems reported by the backend must render at the offending
 * field, not only as a form-level banner — an operator editing a ten-field command should not have to
 * guess which field the message is about.
 */
describe('inline parameter validation', () => {
  const scope = buildEditorScope([]);

  it('anchors an unresolvable-reference error at the field it concerns', () => {
    render(
      <ParameterizableField
        label="ADB serial"
        value="{{typo}}"
        onChange={() => {}}
        scope={scope}
        error="Step '0': 'typo' is not declared here and is not a queue built-in."
      />,
    );

    const field = screen.getByLabelText('ADB serial');
    const alert = screen.getByRole('alert');

    expect(alert).toHaveTextContent("'typo' is not declared here");
    expect(field).toHaveAttribute('aria-invalid', 'true');
    // The message is wired to the input via aria-describedby, so a screen reader reaches it too.
    expect(field.getAttribute('aria-describedby')).toBe(alert.getAttribute('id'));
  });

  it('renders a skipped-static-check warning as a non-blocking notice', () => {
    render(
      <ParameterizableField
        label="Reference image ID"
        value="{{img}}"
        onChange={() => {}}
        scope={scope}
        warning="This field is parametrized, so its target is checked at run time instead of now."
      />,
    );

    expect(screen.getByText(/checked at run time instead of now/)).toBeInTheDocument();
    // A warning must not mark the field invalid — it never blocks a save.
    expect(screen.getByLabelText('Reference image ID')).not.toHaveAttribute('aria-invalid');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('lets an error take precedence over a warning on the same field', () => {
    render(
      <ParameterizableField
        label="ADB serial"
        value="{{typo}}"
        onChange={() => {}}
        scope={scope}
        error="'typo' is not declared here."
        warning="Checked at run time."
      />,
    );

    expect(screen.getByRole('alert')).toHaveTextContent("'typo' is not declared here.");
    expect(screen.queryByText('Checked at run time.')).not.toBeInTheDocument();
  });

  it('tells a numeric field that only a whole-field placeholder is accepted', () => {
    render(
      <ParameterizableField
        label="Timeout"
        value="{{waitMs}}"
        onChange={() => {}}
        scope={scope}
        numeric
      />,
    );

    expect(screen.getByText(/either a plain number or exactly one parameter/)).toBeInTheDocument();
  });
});

describe('editor scope labelling', () => {
  const declaration = (over: Partial<ParameterDeclaration> = {}): ParameterDeclaration => ({
    name: 'adbSerial',
    type: 'text',
    default: null,
    required: false,
    description: 'Target emulator.',
    ...over,
  });

  it('describes a declaration with no default as awaiting a caller, not as already set', () => {
    // Regression: this used to read "set on this entry" inside the command editor, which is wrong —
    // no call site has been chosen at that point.
    const entry = buildEditorScope([declaration()])[0];

    expect(entry.originLayer).toBe('declared');
    expect(originLayerLabel(entry.originLayer)).toBe('supplied by a caller');
  });

  it('describes a declaration with a default as falling back to that default', () => {
    const entry = buildEditorScope([declaration({ default: 'emulator-5558' })])[0];

    expect(entry.originLayer).toBe('default');
    expect(entry.value).toBe('emulator-5558');
  });

  it('always offers the four queue built-ins after the declarations', () => {
    const entries = buildEditorScope([declaration()]);

    expect(entries.map((e) => e.name)).toEqual([
      'adbSerial',
      'queue.emulatorSerial',
      'queue.instanceName',
      'queue.instanceIndex',
      'queue.gameId',
    ]);
    expect(entries.slice(1).every((e) => e.originLayer === 'queue')).toBe(true);
  });

  it('skips a half-typed declaration so the picker never offers a blank name', () => {
    expect(buildEditorScope([declaration({ name: '   ' })]).map((e) => e.name)).not.toContain('   ');
  });
});
