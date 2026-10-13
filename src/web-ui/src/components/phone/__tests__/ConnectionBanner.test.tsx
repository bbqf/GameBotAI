import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { ConnectionBanner } from '../ConnectionBanner';

describe('ConnectionBanner', () => {
  it('shows nothing when online with a token', () => {
    const { container } = render(<ConnectionBanner state="online" unauthorized={false} onOpenConfiguration={jest.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('shows "Connection lost" when the state is lost', () => {
    render(<ConnectionBanner state="lost" unauthorized={false} onOpenConfiguration={jest.fn()} />);
    expect(screen.getByText('Connection lost')).toBeInTheDocument();
  });

  it('shows the token prompt with a button to Configuration after a 401', () => {
    const onOpen = jest.fn();
    render(<ConnectionBanner state="online" unauthorized onOpenConfiguration={onOpen} />);
    expect(screen.getByText('Access token needed. Open Configuration.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Open Configuration' }));
    expect(onOpen).toHaveBeenCalledTimes(1);
  });
});
