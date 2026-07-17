import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { GoToHomeScreenPanel } from '../GoToHomeScreenPanel';

describe('GoToHomeScreenPanel', () => {
  describe('field rendering', () => {
    it('renders a description of the action', () => {
      render(<GoToHomeScreenPanel onConfirm={() => {}} onCancel={() => {}} />);
      expect(
        screen.getByText(/presses the android home button/i)
      ).toBeInTheDocument();
    });

    it('renders Add and Cancel buttons', () => {
      render(<GoToHomeScreenPanel onConfirm={() => {}} onCancel={() => {}} />);
      expect(screen.getByRole('button', { name: 'Add' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    });

    it('does not render any input fields', () => {
      render(<GoToHomeScreenPanel onConfirm={() => {}} onCancel={() => {}} />);
      expect(screen.queryAllByRole('textbox')).toHaveLength(0);
      expect(screen.queryAllByRole('spinbutton')).toHaveLength(0);
      expect(screen.queryAllByRole('combobox')).toHaveLength(0);
    });
  });

  describe('confirm', () => {
    it('calls onConfirm when Add is clicked', () => {
      const onConfirm = jest.fn();
      render(<GoToHomeScreenPanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).toHaveBeenCalledTimes(1);
    });
  });

  describe('cancel', () => {
    it('calls onCancel and does not call onConfirm when Cancel is clicked', () => {
      const onConfirm = jest.fn();
      const onCancel = jest.fn();
      render(<GoToHomeScreenPanel onConfirm={onConfirm} onCancel={onCancel} />);
      fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      expect(onCancel).toHaveBeenCalledTimes(1);
      expect(onConfirm).not.toHaveBeenCalled();
    });
  });

  describe('disabled state', () => {
    it('disables Add and Cancel buttons when disabled', () => {
      render(<GoToHomeScreenPanel onConfirm={() => {}} onCancel={() => {}} disabled />);
      expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    });
  });
});
