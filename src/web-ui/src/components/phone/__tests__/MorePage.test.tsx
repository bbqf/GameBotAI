import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { MorePage } from '../MorePage';

describe('MorePage', () => {
  it('shows the heading More', () => {
    render(<MorePage onOpen={jest.fn()} />);
    expect(screen.getByRole('heading', { name: 'More' })).toBeInTheDocument();
  });

  it('shows 3 buttons in order, each with an icon and a chevron', () => {
    render(<MorePage onOpen={jest.fn()} />);
    const buttons = screen.getAllByRole('button');
    expect(buttons.map((b) => b.textContent)).toEqual(['Configuration', 'Gamepacks', 'Update']);
    buttons.forEach((b) => expect(b.querySelectorAll('svg')).toHaveLength(2));
  });

  it('calls onOpen with the area id', () => {
    const onOpen = jest.fn();
    render(<MorePage onOpen={onOpen} />);
    fireEvent.click(screen.getByRole('button', { name: 'Configuration' }));
    expect(onOpen).toHaveBeenLastCalledWith('configuration');
    fireEvent.click(screen.getByRole('button', { name: 'Gamepacks' }));
    expect(onOpen).toHaveBeenLastCalledWith('gamepacks');
    fireEvent.click(screen.getByRole('button', { name: 'Update' }));
    expect(onOpen).toHaveBeenLastCalledWith('update');
  });

  it('shows the group Desktop only with 2 dimmed rows that are not buttons', () => {
    const onOpen = jest.fn();
    render(<MorePage onOpen={onOpen} />);
    expect(screen.getByText('Desktop only')).toBeInTheDocument();
    const off = document.querySelectorAll('[aria-disabled="true"]');
    expect(off).toHaveLength(2);
    expect(Array.from(off).map((el) => el.querySelector('.phone-more__name')?.textContent)).toEqual(['Execution logs', 'Authoring']);
    off.forEach((el) => {
      expect(within(el as HTMLElement).queryByRole('button')).toBeNull();
      expect(el).toHaveTextContent('Use a computer');
      fireEvent.click(el);
    });
    expect(onOpen).not.toHaveBeenCalled();
  });
});
