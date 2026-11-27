import React from 'react';
import { render, screen } from '@testing-library/react';
import { FooterBar } from '../FooterBar';

describe('FooterBar', () => {
  it('shows the left and the right area', () => {
    render(<FooterBar left={<span>Left text</span>} right={<span>Right text</span>} />);
    expect(screen.getByText('Left text')).toBeInTheDocument();
    expect(screen.getByText('Right text')).toBeInTheDocument();
  });

  it('works with no left area', () => {
    const { container } = render(<FooterBar right={<span>Right text</span>} />);
    expect(container.querySelector('.ui-footer-bar__left')).toBeEmptyDOMElement();
  });
});