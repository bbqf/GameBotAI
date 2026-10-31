import React from 'react';
import { readFileSync } from 'fs';
import { join } from 'path';
import { fireEvent, render, screen } from '@testing-library/react';
import { IconButton, ICON_NAMES } from '../IconButton';

describe('IconButton (feature 173, US3)', () => {
  it('has the accessible name from the label and the tooltip from the title', () => {
    render(<IconButton icon="edit" label="Edit Daily" title="Edit" onClick={() => undefined} />);

    const button = screen.getByRole('button', { name: 'Edit Daily' });
    expect(button).toHaveAttribute('aria-label', 'Edit Daily');
    expect(button).toHaveAttribute('title', 'Edit');
  });

  it('uses the label as the tooltip when no title is given', () => {
    render(<IconButton icon="play" label="Start" onClick={() => undefined} />);

    expect(screen.getByRole('button', { name: 'Start' })).toHaveAttribute('title', 'Start');
  });

  it('draws an icon that screen readers skip, with no visible text', () => {
    render(<IconButton icon="stop" label="Stop Daily" title="Stop" onClick={() => undefined} />);

    const button = screen.getByRole('button', { name: 'Stop Daily' });
    const svg = button.querySelector('svg');
    expect(svg).not.toBeNull();
    expect(svg).toHaveAttribute('aria-hidden', 'true');
    expect(button.textContent).toBe('');
  });

  it('draws a different icon for each name', () => {
    const shapes = ICON_NAMES.map((icon) => {
      const { container, unmount } = render(<IconButton icon={icon} label={icon} onClick={() => undefined} />);
      const shape = container.querySelector('svg')?.innerHTML ?? '';
      unmount();
      return shape;
    });

    expect(new Set(shapes).size).toBe(ICON_NAMES.length);
    shapes.forEach((shape) => expect(shape).not.toBe(''));
  });

  it('calls onClick once per click', () => {
    const onClick = jest.fn();
    render(<IconButton icon="duplicate" label="Duplicate Daily" onClick={onClick} />);

    fireEvent.click(screen.getByRole('button', { name: 'Duplicate Daily' }));

    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('does not call onClick when it is disabled', () => {
    const onClick = jest.fn();
    render(<IconButton icon="delete" label="Delete Daily" disabled onClick={onClick} />);

    const button = screen.getByRole('button', { name: 'Delete Daily' });
    fireEvent.click(button);

    expect(button).toBeDisabled();
    expect(onClick).not.toHaveBeenCalled();
  });

  it('is a native button, so the keyboard reaches it and Enter and Space press it', () => {
    render(<IconButton icon="pause" label="Pause Daily" onClick={() => undefined} />);

    const button = screen.getByRole('button', { name: 'Pause Daily' });
    expect(button.tagName).toBe('BUTTON');
    expect(button).toHaveAttribute('type', 'button');
    expect(button).not.toHaveAttribute('tabindex', '-1');
    button.focus();
    expect(document.activeElement).toBe(button);
  });

  it('marks the destructive tone with its own class', () => {
    render(
      <>
        <IconButton icon="edit" label="Edit" onClick={() => undefined} />
        <IconButton icon="delete" label="Delete" tone="destructive" onClick={() => undefined} />
      </>
    );

    expect(screen.getByRole('button', { name: 'Edit' })).not.toHaveClass('icon-button--destructive');
    expect(screen.getByRole('button', { name: 'Delete' })).toHaveClass('icon-button', 'icon-button--destructive');
  });
});

describe('IconButton styles', () => {
  const css = readFileSync(join(__dirname, '..', 'IconButton.css'), 'utf8');

  it('makes the button 32 px, with a focus ring', () => {
    expect(css).toMatch(/width:\s*32px/);
    expect(css).toMatch(/height:\s*32px/);
    expect(css).toMatch(/\.icon-button:focus-visible/);
  });

  it('gives the destructive tone its own color', () => {
    expect(css).toMatch(/\.icon-button--destructive/);
  });
});
