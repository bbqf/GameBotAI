import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import type { ParameterDeclaration } from '../../parameters/types';

/** Parameters for the panel tests (feature 169): one of each kind that the fit rules tell apart. */
export const SAMPLE_PARAMETERS: ParameterDeclaration[] = [
  { name: 'delay', type: 'number', required: false },
  { name: 'label', type: 'text', required: false },
  { name: 'buttonToTap', type: 'text', kind: 'image', required: false },
  { name: 'tone', type: 'number', kind: 'choice', required: false },
  { name: 'word', type: 'text', kind: 'choice', required: false },
];

/** Renders a panel element with the sample parameters. Extra props replace the defaults. */
export const renderPanelWithParameters = (
  panel: React.ReactElement,
  extraProps: Record<string, unknown> = {},
) => render(React.cloneElement(panel, { parameters: SAMPLE_PARAMETERS, ...extraProps }));

/** Opens the list of the field and selects the parameter. `fieldLabel` is the label of the control. */
export const selectParameter = (fieldLabel: string, name: string): void => {
  fireEvent.click(screen.getByRole('button', { name: `Use a parameter for ${fieldLabel}` }));
  const option = screen
    .getAllByRole('option')
    .find((o) => o.querySelector('.parameter-source__option-name')?.textContent === name);
  if (!option) throw new Error(`No option named ${name}`);
  fireEvent.click(option);
};

/** Removes the parameter from the field. */
export const removeParameter = (fieldLabel: string): void => {
  fireEvent.click(screen.getByRole('button', { name: `Remove parameter from ${fieldLabel}` }));
};

/** Checks that the plain input of the field shows and that no parameter is set. `visibleLabel` is the label text. */
export const expectPlainInput = (visibleLabel: string, controlLabel: string): void => {
  expect(screen.getByLabelText(visibleLabel)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: `Use a parameter for ${controlLabel}` })).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: `Remove parameter from ${controlLabel}` })).not.toBeInTheDocument();
};

/** Checks that a service message shows at the control of the field and is linked to its button. */
export const expectFieldError = (controlLabel: string, message: string): void => {
  const alert = screen.getAllByRole('alert').find((a) => a.textContent === message);
  expect(alert).toBeDefined();
  const button =
    screen.queryByRole('button', { name: `Use a parameter for ${controlLabel}` })
    ?? screen.getByRole('button', { name: `Remove parameter from ${controlLabel}` });
  expect(button).toHaveAccessibleDescription(message);
};
