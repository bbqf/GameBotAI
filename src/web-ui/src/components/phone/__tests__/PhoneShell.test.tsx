import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { PhoneHeader, PhoneShell } from '../PhoneShell';
import { navigationAreas } from '../../../types/navigation';

describe('PhoneShell', () => {
  it('shows the area title, the content, and the tab bar', () => {
    render(
      <PhoneShell areas={navigationAreas} activeArea="execution" onChange={jest.fn()}>
        <p>Body text</p>
      </PhoneShell>
    );
    expect(screen.getByRole('heading', { name: 'Execution' })).toBeInTheDocument();
    expect(screen.getByRole('main')).toHaveTextContent('Body text');
    expect(screen.getByRole('navigation', { name: 'Primary Navigation' })).toBeInTheDocument();
  });

  it('shows a back control only when onBack is set', () => {
    const { rerender } = render(<PhoneHeader title="Queues" />);
    expect(screen.queryByRole('button', { name: 'Back' })).not.toBeInTheDocument();
    const onBack = jest.fn();
    rerender(<PhoneHeader title="Queues" onBack={onBack} />);
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(onBack).toHaveBeenCalledTimes(1);
  });
});
