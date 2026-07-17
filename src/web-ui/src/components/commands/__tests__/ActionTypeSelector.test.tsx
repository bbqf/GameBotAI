import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { ActionTypeSelector } from '../ActionTypeSelector';

describe('ActionTypeSelector', () => {
  it('renders a blank first option', () => {
    render(<ActionTypeSelector value="" onChange={() => {}} />);
    const select = screen.getByRole('combobox', { name: /action type/i });
    expect(select).toHaveValue('');
    const options = Array.from((select as HTMLSelectElement).options);
    expect(options[0].value).toBe('');
  });

  it('renders exactly Tap, Wait for Image, Ensure Game Running, Ensure Emulator Running, Go to Home Screen, Key Input, Swipe options', () => {
    render(<ActionTypeSelector value="" onChange={() => {}} />);
    const select = screen.getByRole('combobox', { name: /action type/i });
    const options = Array.from((select as HTMLSelectElement).options).map((o) => ({
      value: o.value,
      text: o.text,
    }));
    expect(options).toEqual([
      { value: '', text: expect.any(String) },
      { value: 'PrimitiveTap', text: 'Tap' },
      { value: 'WaitForImage', text: 'Wait for Image' },
      { value: 'EnsureGameRunning', text: 'Ensure Game Running' },
      { value: 'EnsureEmulatorRunning', text: 'Ensure Emulator Running' },
      { value: 'GoToHomeScreen', text: 'Go to Home Screen' },
      { value: 'KeyInput', text: 'Key Input' },
      { value: 'Swipe', text: 'Swipe' },
    ]);
    expect(options).toHaveLength(8);
  });

  it('onChange fires with EnsureEmulatorRunning when Ensure Emulator Running is selected', () => {
    const onChange = jest.fn();
    render(<ActionTypeSelector value="" onChange={onChange} />);
    fireEvent.change(screen.getByRole('combobox', { name: /action type/i }), {
      target: { value: 'EnsureEmulatorRunning' },
    });
    expect(onChange).toHaveBeenCalledWith('EnsureEmulatorRunning');
  });

  it('onChange fires with PrimitiveTap when Tap is selected', () => {
    const onChange = jest.fn();
    render(<ActionTypeSelector value="" onChange={onChange} />);
    fireEvent.change(screen.getByRole('combobox', { name: /action type/i }), {
      target: { value: 'PrimitiveTap' },
    });
    expect(onChange).toHaveBeenCalledWith('PrimitiveTap');
  });

  it('onChange fires with WaitForImage when Wait for Image is selected', () => {
    const onChange = jest.fn();
    render(<ActionTypeSelector value="" onChange={onChange} />);
    fireEvent.change(screen.getByRole('combobox', { name: /action type/i }), {
      target: { value: 'WaitForImage' },
    });
    expect(onChange).toHaveBeenCalledWith('WaitForImage');
  });

  it('onChange fires with EnsureGameRunning when Ensure Game Running is selected', () => {
    const onChange = jest.fn();
    render(<ActionTypeSelector value="" onChange={onChange} />);
    fireEvent.change(screen.getByRole('combobox', { name: /action type/i }), {
      target: { value: 'EnsureGameRunning' },
    });
    expect(onChange).toHaveBeenCalledWith('EnsureGameRunning');
  });

  it('onChange fires with GoToHomeScreen when Go to Home Screen is selected', () => {
    const onChange = jest.fn();
    render(<ActionTypeSelector value="" onChange={onChange} />);
    fireEvent.change(screen.getByRole('combobox', { name: /action type/i }), {
      target: { value: 'GoToHomeScreen' },
    });
    expect(onChange).toHaveBeenCalledWith('GoToHomeScreen');
  });

  it('onChange fires with empty string when blank option is selected', () => {
    const onChange = jest.fn();
    render(<ActionTypeSelector value="PrimitiveTap" onChange={onChange} />);
    fireEvent.change(screen.getByRole('combobox', { name: /action type/i }), {
      target: { value: '' },
    });
    expect(onChange).toHaveBeenCalledWith('');
  });

  it('reflects current value in the select element', () => {
    render(<ActionTypeSelector value="WaitForImage" onChange={() => {}} />);
    expect(screen.getByRole('combobox', { name: /action type/i })).toHaveValue('WaitForImage');
  });

  it('is disabled when disabled prop is true', () => {
    render(<ActionTypeSelector value="" onChange={() => {}} disabled />);
    expect(screen.getByRole('combobox', { name: /action type/i })).toBeDisabled();
  });
});
