import React, { useState } from 'react';
import { ActionForm, ActionFormValue } from '../../components/actions/ActionForm';
import { useActionTypes } from '../../services/useActionTypes';

export const CreateActionPage: React.FC = () => {
  const { data, loading, error } = useActionTypes();
  const [form, setForm] = useState<ActionFormValue>({ name: '', type: '', attributes: {} });
  const [submitMessage, setSubmitMessage] = useState<string | undefined>(undefined);

  const actionTypes = data?.items ?? [];

  return (
    <section>
      <h2>Create Action</h2>
      {error && <div className="form-error" role="alert">{error}</div>}
      {submitMessage && <div className="form-hint">{submitMessage}</div>}
      <ActionForm
        actionTypes={actionTypes}
        value={form}
        loading={loading}
        errors={[]}
        onChange={setForm}
        onSubmit={() => {
          // Placeholder submit until story implementation
          setSubmitMessage('Submit handler will be implemented in story work.');
        }}
      />
    </section>
  );
};
