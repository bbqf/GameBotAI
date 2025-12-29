import React, { useEffect, useMemo, useState } from 'react';
import { ActionForm, ActionFormValue } from '../../components/actions/ActionForm';
import { useActionTypes } from '../../services/useActionTypes';
import { useGames } from '../../services/useGames';
import { createAction } from '../../services/actionsApi';
import { validateAttributes } from '../../services/validation';
import { ApiError } from '../../lib/api';
import { ValidationMessage } from '../../types/actions';

const initialForm: ActionFormValue = { name: '', gameId: '', type: '', attributes: {} };

type CreateActionPageProps = {
  initialValue?: ActionFormValue;
  onCreated?: () => void;
  onCancel?: () => void;
};

export const CreateActionPage: React.FC<CreateActionPageProps> = ({ initialValue, onCreated, onCancel }) => {
  const { data, loading, error } = useActionTypes();
  const { data: games, loading: gamesLoading, error: gamesError } = useGames();
  const [form, setForm] = useState<ActionFormValue>(initialValue ?? initialForm);
  const [errors, setErrors] = useState<ValidationMessage[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [message, setMessage] = useState<string | undefined>(undefined);

  useEffect(() => {
    setForm(initialValue ?? initialForm);
  }, [initialValue]);

  const actionTypes = useMemo(() => data?.items ?? [], [data]);

  const selectedType = useMemo(() => actionTypes.find((t) => t.key === form.type), [actionTypes, form.type]);

  const runValidation = (): ValidationMessage[] => {
    const messages: ValidationMessage[] = [];
    if (!form.name.trim()) messages.push({ field: 'name', message: 'Name is required', severity: 'error' });
    if (!form.gameId) messages.push({ field: 'gameId', message: 'Game is required', severity: 'error' });
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
      await createAction({ name: form.name.trim(), gameId: form.gameId, type: form.type, attributes: form.attributes });
      setErrors([]);
      setMessage('Action created successfully.');
      setForm(initialValue ?? initialForm);
      onCreated?.();
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
      {gamesError && <div className="form-error" role="alert">{gamesError}</div>}
      {message && <div className="form-hint">{message}</div>}
      <ActionForm
        actionTypes={actionTypes}
        games={games ?? []}
        value={form}
        loading={loading || gamesLoading}
        errors={errors}
        submitting={submitting}
        onChange={(v) => { setErrors([]); setForm(v); }}
        onSubmit={() => { void submit(); }}
        onCancel={onCancel}
      />
    </section>
  );
};
