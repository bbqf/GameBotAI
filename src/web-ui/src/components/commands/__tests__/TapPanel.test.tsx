import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { TapPanel } from '../TapPanel';

jest.mock('../../images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ value, onChange, onStaleChange, error, label, required: _required }: any) => (
    <div>
      {label && <label htmlFor="mock-image-input">{label}</label>}
      <input
        id="mock-image-input"
        data-testid="image-input"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
      <button
        type="button"
        data-testid="mark-stale"
        onClick={() => onStaleChange?.(true)}
      >
        Mark Stale
      </button>
      <button
        type="button"
        data-testid="clear-stale"
        onClick={() => onStaleChange?.(false)}
      >
        Clear Stale
      </button>
      {error && <div role="alert">{error}</div>}
    </div>
  ),
}));

describe('TapPanel', () => {
  describe('field rendering', () => {
    it('renders the reference image selector field', () => {
      render(<TapPanel onConfirm={() => {}} onCancel={() => {}} />);
      expect(screen.getByTestId('image-input')).toBeInTheDocument();
    });

    it('renders confidence, offsetX, and offsetY inputs', () => {
      render(<TapPanel onConfirm={() => {}} onCancel={() => {}} />);
      expect(screen.getByLabelText(/confidence/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/offset x/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/offset y/i)).toBeInTheDocument();
    });

    it('renders Add and Cancel buttons', () => {
      render(<TapPanel onConfirm={() => {}} onCancel={() => {}} />);
      expect(screen.getByRole('button', { name: 'Add' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    });

    it('renders Save button (not Add) when initialValue is provided', () => {
      render(
        <TapPanel
          initialValue={{ referenceImageId: 'img-a' }}
          onConfirm={() => {}}
          onCancel={() => {}}
        />
      );
      expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Add' })).toBeNull();
    });

    it('does not render any fields beyond image, confidence, offsetX, offsetY', () => {
      render(<TapPanel onConfirm={() => {}} onCancel={() => {}} />);
      const inputs = screen.getAllByRole('textbox').concat(screen.queryAllByRole('spinbutton'));
      // image-input (text), confidence (spinbutton), offsetX (spinbutton), offsetY (spinbutton)
      expect(inputs.length).toBe(4);
    });
  });

  describe('initialValue pre-fills fields', () => {
    it('pre-fills referenceImageId from initialValue', () => {
      render(
        <TapPanel
          initialValue={{ referenceImageId: 'img-x', confidence: '0.9', offsetX: '5', offsetY: '-3' }}
          onConfirm={() => {}}
          onCancel={() => {}}
        />
      );
      expect(screen.getByTestId('image-input')).toHaveValue('img-x');
      expect(screen.getByLabelText(/confidence/i)).toHaveValue(0.9);
      expect(screen.getByLabelText(/offset x/i)).toHaveValue(5);
      expect(screen.getByLabelText(/offset y/i)).toHaveValue(-3);
    });
  });

  describe('validation — referenceImageId required', () => {
    it('does not call onConfirm and shows error when referenceImageId is empty', () => {
      const onConfirm = jest.fn();
      render(<TapPanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).not.toHaveBeenCalled();
      expect(screen.getByRole('alert')).toHaveTextContent('Reference image is required.');
    });

    it('does not call onConfirm and shows error when referenceImageId is stale', () => {
      const onConfirm = jest.fn();
      render(<TapPanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.change(screen.getByTestId('image-input'), { target: { value: 'deleted-img' } });
      fireEvent.click(screen.getByTestId('mark-stale'));
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).not.toHaveBeenCalled();
      expect(screen.getByRole('alert')).toHaveTextContent('Reference image is required.');
    });
  });

  describe('validation — confidence range', () => {
    it('does not call onConfirm and shows error when confidence is above 1', () => {
      const onConfirm = jest.fn();
      render(<TapPanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.change(screen.getByTestId('image-input'), { target: { value: 'img-a' } });
      fireEvent.change(screen.getByLabelText(/confidence/i), { target: { value: '1.5' } });
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).not.toHaveBeenCalled();
      expect(screen.getByRole('alert')).toHaveTextContent('Confidence must be a number between 0 and 1.');
    });

    it('does not call onConfirm and shows error when confidence is below 0', () => {
      const onConfirm = jest.fn();
      render(<TapPanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.change(screen.getByTestId('image-input'), { target: { value: 'img-a' } });
      fireEvent.change(screen.getByLabelText(/confidence/i), { target: { value: '-0.1' } });
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).not.toHaveBeenCalled();
      expect(screen.getByRole('alert')).toHaveTextContent('Confidence must be a number between 0 and 1.');
    });

    it('allows empty confidence (optional field)', () => {
      const onConfirm = jest.fn();
      render(<TapPanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.change(screen.getByTestId('image-input'), { target: { value: 'img-a' } });
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).toHaveBeenCalled();
    });
  });

  describe('successful confirm', () => {
    it('calls onConfirm with all four values when valid', () => {
      const onConfirm = jest.fn();
      render(<TapPanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.change(screen.getByTestId('image-input'), { target: { value: 'img-a' } });
      fireEvent.change(screen.getByLabelText(/confidence/i), { target: { value: '0.8' } });
      fireEvent.change(screen.getByLabelText(/offset x/i), { target: { value: '10' } });
      fireEvent.change(screen.getByLabelText(/offset y/i), { target: { value: '-5' } });
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).toHaveBeenCalledWith({
        referenceImageId: 'img-a',
        confidence: '0.8',
        offsetX: '10',
        offsetY: '-5',
      });
    });

    it('calls onConfirm with undefined optional fields when left empty', () => {
      const onConfirm = jest.fn();
      render(<TapPanel onConfirm={onConfirm} onCancel={() => {}} />);
      fireEvent.change(screen.getByTestId('image-input'), { target: { value: 'img-a' } });
      fireEvent.change(screen.getByLabelText(/offset x/i), { target: { value: '' } });
      fireEvent.change(screen.getByLabelText(/offset y/i), { target: { value: '' } });
      fireEvent.click(screen.getByRole('button', { name: 'Add' }));
      expect(onConfirm).toHaveBeenCalledWith({
        referenceImageId: 'img-a',
        confidence: undefined,
        offsetX: undefined,
        offsetY: undefined,
      });
    });
  });

  describe('cancel', () => {
    it('calls onCancel and does not call onConfirm when Cancel is clicked', () => {
      const onConfirm = jest.fn();
      const onCancel = jest.fn();
      render(<TapPanel onConfirm={onConfirm} onCancel={onCancel} />);
      fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      expect(onCancel).toHaveBeenCalled();
      expect(onConfirm).not.toHaveBeenCalled();
    });
  });

  describe('disabled state', () => {
    it('disables all buttons when disabled prop is true', () => {
      render(<TapPanel onConfirm={() => {}} onCancel={() => {}} disabled />);
      expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    });
  });
});
