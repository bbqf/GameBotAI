import React, { useMemo, useState } from 'react';
import { ActionForm, ActionFormValue } from '../../components/actions/ActionForm';
import { useActionTypes } from '../../services/useActionTypes';
import { createAction } from '../../services/actionsApi';
import { validateAttributes } from '../../services/validation';
import { ApiError } from '../../lib/api';
import { ValidationMessage } from '../../types/actions';

const initialForm: ActionFormValue = { name: '', type: '', attributes: {} };

export const CreateActionPage: React.FC = () => {
  const { data, loading, error } = useActionTypes();
  const [form, setForm] = useState<ActionFormValue>(initialForm);
  const [errors, setErrors] = useState<ValidationMessage[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [message, setMessage] = useState<string | undefined>(undefined);

  const actionTypes = data?.items ?? [];

  const selectedType = useMemo(() => actionTypes.find((t) => t.key === form.type), [actionTypes, form.type]);

  const runValidation = (): ValidationMessage[] => {
    const messages: ValidationMessage[] = [];
    if (!form.name.trim()) messages.push({ field: 'name', message: 'Name is required', severity: 'error' });
    if (!form.type) messages.push({ field: 'type', message: 'Action type is required', severity: 'error' });
    if (selectedType) {
      messages.push(...validateAttributes(selectedType.attributeDefinitions, form.attributes));
    }
    return messages;
  };

  const submit = async () => {
    setMessage(undefined);
    const validation = runValidation();
    if (validation.length > 0) {
      setErrors(validation);
      return;
    }

    setSubmitting(true);
    try {
      await createAction({ name: form.name.trim(), type: form.type, attributes: form.attributes });
      setErrors([]);
      setMessage('Action created successfully.');
      setForm(initialForm);
    } catch (err: any) {
      if (err instanceof ApiError && err.errors?.length) {
        const mapped: ValidationMessage[] = err.errors.map((e) => ({
          field: e.field,
          message: e.message,
          severity: 'error'
        }));
        setErrors(mapped);
      } else {
        setErrors([{ message: err?.message ?? 'Failed to create action', severity: 'error' }]);
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <section>
      <h2>Create Action</h2>
      {error && <div className="form-error" role="alert">{error}</div>}
      {message && <div className="form-hint">{message}</div>}
      <ActionForm
        actionTypes={actionTypes}
        value={form}
        loading={loading}
        errors={errors}
        submitting={submitting}
        onChange={(v) => { setErrors([]); setForm(v); }}
        onSubmit={() => { void submit(); }}
      />
    </section>
  );
};
