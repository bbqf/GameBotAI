import React from 'react';

type FormSectionProps = {
  id?: string;
  title: string;
  description?: string;
  actions?: React.ReactNode;
  children: React.ReactNode;
};

export const FormSection: React.FC<FormSectionProps> = ({ id, title, description, actions, children }) => {
  const headingId = id ? `${id}-heading` : undefined;
  return (
    <section className="form-section" aria-labelledby={headingId} id={id}>
      <div className="form-section__header">
        <h3 id={headingId}>{title}</h3>
        {actions && <div className="form-section__actions">{actions}</div>}
      </div>
      {description && <p className="form-section__description">{description}</p>}
      <div className="form-section__body">{children}</div>
    </section>
  );
};

type FormActionsProps = {
  submitLabel?: string;
  cancelLabel?: string;
  onCancel?: () => void;
  submitting?: boolean;
  disabled?: boolean;
  children?: React.ReactNode;
};

export const FormActions: React.FC<FormActionsProps> = ({
  submitLabel = 'Save',
  cancelLabel = 'Cancel',
  onCancel,
  submitting,
  disabled,
  children
}) => {
  return (
    <div className="form-actions">
      <button type="submit" disabled={submitting || disabled}>{submitLabel}</button>
      {onCancel && (
        <button type="button" onClick={onCancel} disabled={submitting || disabled}>{cancelLabel}</button>
      )}
      {children}
    </div>
  );
};
