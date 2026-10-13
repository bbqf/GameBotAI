import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { DesktopOnlyNotice } from '../DesktopOnlyNotice';

describe('DesktopOnlyNotice', () => {
  it('shows the area name, the sentence, and the back button', () => {
    const onBack = jest.fn();
    render(<DesktopOnlyNotice areaLabel="Authoring" onBack={onBack} />);
    expect(screen.getByRole('heading', { name: 'Authoring' })).toBeInTheDocument();
    expect(screen.getByText('Use a larger screen for this area.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Back to Queues' }));
    expect(onBack).toHaveBeenCalledTimes(1);
  });
});
