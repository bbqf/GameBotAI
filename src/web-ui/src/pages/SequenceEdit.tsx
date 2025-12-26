import React, { useEffect, useState } from 'react';
import { ApiError, getJson, putJson } from '../lib/api';
import { FormField } from '../components/FormField';

type Sequence = {
  id: string;
  name?: string;
  steps?: any[];
  blocks?: any[];
  createdAt?: string;
  updatedAt?: string;
};

export const SequenceEdit: React.FC = () => {
  const [id, setId] = useState('');
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [model, setModel] = useState<Sequence | null>(null);

  const onFetch = async () => {
    setLoading(true);
    setMessage(null);
    try {
      const data = await getJson<Sequence>(`/api/sequences/${encodeURIComponent(id)}`);
      setModel(data ?? null);
      if (!data) setMessage('No content');
    } catch (e: unknown) {
      if (e instanceof ApiError && e.status === 404) setMessage('Not found');
      else if (e instanceof Error) setMessage(`Error: ${e.message}`);
      else setMessage('Error: Unknown');
    } finally {
      setLoading(false);
    }
  };

  const onSave = async () => {
    if (!model) return;
    setSaving(true);
    setMessage(null);
    try {
      const payload = { ...model };
      const saved = await putJson<Sequence>(`/api/sequences/${encodeURIComponent(model.id)}`, payload);
      setModel(saved ?? null);
      setMessage('Saved');
    } catch (e: unknown) {
      if (e instanceof ApiError && e.status === 400) {
        const summary = e.errors?.map(er => er.message).join('; ') ?? 'Validation failed';
        setMessage(`Validation error: ${summary}`);
      } else if (e instanceof Error) setMessage(`Error: ${e.message}`);
      else setMessage('Error: Unknown');
    } finally {
      setSaving(false);
    }
  };

  return (
    <section>
      <h2>Edit Sequence</h2>
      <div className="row">
        <label htmlFor="editId">Sequence ID</label>
        <input id="editId" type="text" value={id} onChange={(e) => setId(e.target.value)} placeholder="Enter sequence id" />
        <button disabled={!id || loading} onClick={onFetch}>Load</button>
      </div>

      {model && (
        <div>
          <FormField label="Name" htmlFor="editName">
            <input
              id="editName"
              type="text"
              value={model.name ?? ''}
              onChange={(e) => setModel({ ...model, name: e.target.value })}
            />
          </FormField>
          <div className="actions">
            <button disabled={saving} onClick={onSave}>Save</button>
          </div>
        </div>
      )}

      {message && <div className="message">{message}</div>}
    </section>
  );
};