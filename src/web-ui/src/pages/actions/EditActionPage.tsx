import React, { useEffect, useMemo, useState } from 'react';
import { ActionForm, ActionFormValue } from '../../components/actions/ActionForm';
import { useActionTypes } from '../../services/useActionTypes';
import { getAction, updateAction } from '../../services/actionsApi';
import { validateAttributes } from '../../services/validation';
import { ApiError } from '../../lib/api';
import { ValidationMessage } from '../../types/actions';

type EditActionPageProps = { actionId?: string };

export const EditActionPage: React.FC<EditActionPageProps> = ({ actionId }) => {
  const id = actionId;
  const { data: typeCatalog, loading: typesLoading, error: typesError } = useActionTypes();
  const [loading, setLoading] = useState(true);
  const [errors, setErrors] = useState<ValidationMessage[]>([]);
  const [form, setForm] = useState<ActionFormValue>({ name: '', type: '', attributes: {} });
  const [message, setMessage] = useState<string | undefined>(undefined);
  const [loadError, setLoadError] = useState<string | undefined>(undefined);
  const [submitting, setSubmitting] = useState(false);

  const actionTypes = typeCatalog?.items ?? [];
  const selectedType = useMemo(() => actionTypes.find((t) => t.key === form.type), [actionTypes, form.type]);

  useEffect(() => {
    if (!id) {
      setLoadError('Missing action id');
      setLoading(false);
      return;
    }

    let active = true;
    const load = async () => {
      try {
        const a = await getAction(id);
        if (!active) return;
        setForm({ name: a.name, type: a.type, attributes: a.attributes ?? {} });
      } catch (err: any) {
        if (!active) return;
        setLoadError(err?.message ?? 'Failed to load action');
      } finally {
        if (active) setLoading(false);
      }
    };
    void load();
    return () => { active = false; };
  }, [id]);

  const runValidation = (): ValidationMessage[] => {
    const messages: ValidationMessage[] = [];
    if (!form.name.trim()) messages.push({ field: 'name', message: 'Name is required', severity: 'error' });
    if (!form.type) messages.push({ field: 'type', message: 'Action type is required', severity: 'error' });
    if (selectedType) messages.push(...validateAttributes(selectedType.attributeDefinitions, form.attributes));
    return messages;
  };

  const submit = async () => {
    if (!id) return;
    setMessage(undefined);
    const validation = runValidation();
    if (validation.length > 0) {
      setErrors(validation);
      return;
    }

    setSubmitting(true);
    try {
      await updateAction(id, { name: form.name.trim(), type: form.type, attributes: form.attributes });
      setErrors([]);
      setMessage('Action updated successfully.');
    } catch (err: any) {
      if (err instanceof ApiError && err.errors?.length) {
        const mapped: ValidationMessage[] = err.errors.map((e) => ({ field: e.field, message: e.message, severity: 'error' }));
        setErrors(mapped);
      } else {
        setErrors([{ message: err?.message ?? 'Failed to update action', severity: 'error' }]);
      }
    } finally {
      setSubmitting(false);
    }
  };

  if (loading) {
    return (<section><h2>Edit Action</h2><p>Loading...</p></section>);
  }

  if (loadError) {
    return (<section><h2>Edit Action</h2><div className="form-error" role="alert">{loadError}</div></section>);
  }

  return (
    <section>
      <h2>Edit Action</h2>
      {typesError && <div className="form-error" role="alert">{typesError}</div>}
      {message && <div className="form-hint">{message}</div>}
      <ActionForm
        actionTypes={actionTypes}
        value={form}
        loading={typesLoading}
        errors={errors}
        submitting={submitting}
        onChange={(v) => { setErrors([]); setForm(v); }}
        onSubmit={() => { void submit(); }}
      />
    </section>
  );
};
