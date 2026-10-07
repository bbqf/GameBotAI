import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { EnsureGameRunningPanel } from '../EnsureGameRunningPanel';

describe('EnsureGameRunningPanel', () => {
  describe('field rendering', () => {
    it('renders a description of the action', () => {
      render(<EnsureGameRunningPanel onConfirm={() => {}} onCancel={() => {}} />);
      expect(
        screen.getByText(/checks that the game is in the foreground/i)
      ).toBeInTheDocument();
    });

    it('renders Add and Cancel buttons', () => {
      render(<EnsureGameRunningPanel onConfirm={() => {}} onCancel={() => {}} />);
      expect(screen.getByRole('button', { name: 'Add' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    });

    it('does not render any input fields', () => {
      render(<EnsureGameRunningPanel onConfirm={() => {}} onCancel={() => {}} />);
      expect(screen.queryAllByRole('textbox')).toHaveLength(0);
      expect(screen.queryAllByRole('spinbutton')).toHaveLength(0);
      expect(screen.queryAllByRole('combobox')).toHaveLength(0);
    });
  });

  describe('confirm', () => {
    it('calls onConfirm when Add is clicked', () => {
      const onConfirm = jest.fn();
      render(<EnsureGameRunningPanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).toHaveBeenCalledTimes(1);
    });
  });

  describe('force restart option', () => {
    it('shows the Force restart checkbox, unchecked by default', () => {
      render(<EnsureGameRunningPanel onConfirm={() => {}} onCancel={() => {}} />);
      const box = screen.getByRole('checkbox', { name: /force restart/i });
      expect(box).toBeInTheDocument();
      expect(box).not.toBeChecked();
    });

    it('passes false to onConfirm when the box is not checked', () => {
      const onConfirm = jest.fn();
      render(<EnsureGameRunningPanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).toHaveBeenCalledWith(false);
    });

    it('passes true to onConfirm when the box is checked', () => {
      const onConfirm = jest.fn();
      render(<EnsureGameRunningPanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.click(screen.getByRole('checkbox', { name: /force restart/i }));
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).toHaveBeenCalledWith(true);
    });

    it('starts checked when the step has the option', () => {
      render(<EnsureGameRunningPanel onConfirm={() => {}} onCancel={() => {}} initialForceRestart />);
      expect(screen.getByRole('checkbox', { name: /force restart/i })).toBeChecked();
    });
  });

  describe('cancel', () => {
    it('calls onCancel and does not call onConfirm when Cancel is clicked', () => {
      const onConfirm = jest.fn();
      const onCancel = jest.fn();
      render(<EnsureGameRunningPanel onConfirm={onConfirm} onCancel={onCancel} />);
      fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      expect(onCancel).toHaveBeenCalledTimes(1);
      expect(onConfirm).not.toHaveBeenCalled();
    });
  });

  describe('disabled state', () => {
    it('disables Add and Cancel buttons when disabled', () => {
      render(<EnsureGameRunningPanel onConfirm={() => {}} onCancel={() => {}} disabled />);
      expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    });
  });
});
