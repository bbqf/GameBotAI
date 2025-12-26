import React, { useCallback, useRef } from 'react';

export const validateRequired = (value: string, label: string): string | null => {
  if (!value || value.trim().length === 0) return `${label} is required`;
  return null;
};

export const tryParseJson = <T = Record<string, unknown>>(text: string): { value?: T; error?: string } => {
  const trimmed = (text ?? '').trim();
  if (trimmed.length === 0) return { value: undefined };
  try {
    const obj = JSON.parse(trimmed) as T;
    return { value: obj };
  } catch (e: any) {
    return { error: 'Invalid JSON' };
  }
};

export const FormError: React.FC<{ message?: string }> = ({ message }) => {
  if (!message) return null;
  return (
    <div className="form-error" role="alert">
      {message}
    </div>
  );
};

export type FieldValidator = (value: string) => string | null;

export const useFormFocusOnError = () => {
  const firstErrorRef = useRef<HTMLInputElement | HTMLTextAreaElement | null>(null);
  const registerErrorField = useCallback((el: HTMLInputElement | HTMLTextAreaElement | null) => {
    if (el && !firstErrorRef.current) firstErrorRef.current = el;
  }, []);
  const focusFirstError = useCallback(() => {
    firstErrorRef.current?.focus();
    firstErrorRef.current = null;
  }, []);
  return { registerErrorField, focusFirstError };
};

export const useFieldValidation = (value: string, validators: FieldValidator[]): { error?: string } => {
  for (const v of validators) {
    const res = v(value);
    if (res) return { error: res };
  }
  return {};
};
