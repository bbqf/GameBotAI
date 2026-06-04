import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { WaitForImagePanel } from '../WaitForImagePanel';

jest.mock('../../images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ value, onChange, label }: any) => (
    <div>
      {label && <label htmlFor="mock-wfi-image">{label}</label>}
      <input
        id="mock-wfi-image"
        data-testid="wfi-image-input"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </div>
  ),
}));

describe('WaitForImagePanel', () => {
  describe('field rendering', () => {
    it('renders timeoutMs, image, and confidence inputs', () => {
      render(<WaitForImagePanel onConfirm={() => {}} onCancel={() => {}} />);
      expect(screen.getByLabelText(/timeout/i)).toBeInTheDocument();
      expect(screen.getByTestId('wfi-image-input')).toBeInTheDocument();
      expect(screen.getByLabelText(/confidence/i)).toBeInTheDocument();
    });

    it('renders Add and Cancel buttons', () => {
      render(<WaitForImagePanel onConfirm={() => {}} onCancel={() => {}} />);
      expect(screen.getByRole('button', { name: 'Add' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    });

    it('renders Save button when initialValue is provided', () => {
      render(
        <WaitForImagePanel
          initialValue={{ timeoutMs: '2000' }}
          onConfirm={() => {}}
          onCancel={() => {}}
        />
      );
      expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Add' })).toBeNull();
    });

    it('does not render any fields beyond timeout, image, confidence', () => {
      render(<WaitForImagePanel onConfirm={() => {}} onCancel={() => {}} />);
      const spinbuttons = screen.getAllByRole('spinbutton');
      // timeout (spinbutton) + confidence (spinbutton); image is text/testid
      expect(spinbuttons.length).toBe(2);
    });

    it('does not have stale blocking — a stale-like image value does not block confirm', () => {
      const onConfirm = jest.fn();
      render(<WaitForImagePanel onConfirm={onConfirm} onCancel={() => {}} />);
      // Set a timeout to make form valid
      fireEvent.change(screen.getByLabelText(/timeout/i), { target: { value: '1000' } });
      // Even with a referenceImageId that could be stale, confirm should fire
      fireEvent.change(screen.getByTestId('wfi-image-input'), { target: { value: 'some-img' } });
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).toHaveBeenCalled();
    });
  });

  describe('initialValue pre-fills fields', () => {
    it('pre-fills all fields from initialValue', () => {
      render(
        <WaitForImagePanel
          initialValue={{ timeoutMs: '3000', referenceImageId: 'img-y', confidence: '0.7' }}
          onConfirm={() => {}}
          onCancel={() => {}}
        />
      );
      expect(screen.getByLabelText(/timeout/i)).toHaveValue(3000);
      expect(screen.getByTestId('wfi-image-input')).toHaveValue('img-y');
      expect(screen.getByLabelText(/confidence/i)).toHaveValue(0.7);
    });
  });

  describe('validation — timeout required', () => {
    it('does not call onConfirm and shows error when timeout is empty', () => {
      const onConfirm = jest.fn();
      render(<WaitForImagePanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.change(screen.getByLabelText(/timeout/i), { target: { value: '' } });
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).not.toHaveBeenCalled();
      expect(screen.getByRole('alert')).toHaveTextContent('Timeout must be a non-negative whole number (ms).');
    });

    it('does not call onConfirm and shows error when timeout is non-integer', () => {
      const onConfirm = jest.fn();
      render(<WaitForImagePanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.change(screen.getByLabelText(/timeout/i), { target: { value: '1.5' } });
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).not.toHaveBeenCalled();
      expect(screen.getByRole('alert')).toHaveTextContent('Timeout must be a non-negative whole number (ms).');
    });

    it('does not call onConfirm and shows error when timeout is negative', () => {
      const onConfirm = jest.fn();
      render(<WaitForImagePanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.change(screen.getByLabelText(/timeout/i), { target: { value: '-1' } });
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).not.toHaveBeenCalled();
    });
  });

  describe('validation — confidence range', () => {
    it('does not call onConfirm and shows error when confidence is out of range', () => {
      const onConfirm = jest.fn();
      render(<WaitForImagePanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.change(screen.getByLabelText(/confidence/i), { target: { value: '2' } });
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).not.toHaveBeenCalled();
      expect(screen.getByRole('alert')).toHaveTextContent('Confidence must be a number between 0 and 1.');
    });

    it('allows empty confidence (optional)', () => {
      const onConfirm = jest.fn();
      render(<WaitForImagePanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).toHaveBeenCalled();
    });
  });

  describe('successful confirm', () => {
    it('calls onConfirm with timeout only when image is empty', () => {
      const onConfirm = jest.fn();
      render(<WaitForImagePanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.change(screen.getByLabelText(/timeout/i), { target: { value: '2000' } });
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).toHaveBeenCalledWith({
        timeoutMs: '2000',
        referenceImageId: undefined,
        confidence: undefined,
      });
    });

    it('calls onConfirm with all three values when all are filled', () => {
      const onConfirm = jest.fn();
      render(<WaitForImagePanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.change(screen.getByLabelText(/timeout/i), { target: { value: '5000' } });
      fireEvent.change(screen.getByTestId('wfi-image-input'), { target: { value: 'img-z' } });
      fireEvent.change(screen.getByLabelText(/confidence/i), { target: { value: '0.6' } });
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).toHaveBeenCalledWith({
        timeoutMs: '5000',
        referenceImageId: 'img-z',
        confidence: '0.6',
      });
    });
  });

  describe('cancel', () => {
    it('calls onCancel and does not call onConfirm', () => {
      const onConfirm = jest.fn();
      const onCancel = jest.fn();
      render(<WaitForImagePanel onConfirm={onConfirm} onCancel={onCancel} />);
      fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      expect(onCancel).toHaveBeenCalled();
      expect(onConfirm).not.toHaveBeenCalled();
    });
  });

  describe('disabled state', () => {
    it('disables Add and Cancel buttons when disabled', () => {
      render(<WaitForImagePanel onConfirm={() => {}} onCancel={() => {}} disabled />);
      expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    });
  });
});
