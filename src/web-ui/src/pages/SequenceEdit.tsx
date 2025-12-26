import React, { useEffect, useState } from 'react';
import { ApiError, getJson, putJson } from '../lib/api';
import { parseFromError, ParsedValidation } from '../lib/validation';
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
  const [fieldErrors, setFieldErrors] = useState<ParsedValidation | null>(null);

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
      setFieldErrors(null);
    } catch (e: unknown) {
      if (e instanceof ApiError && e.status === 400) {
        const parsed = parseFromError(e);
        setFieldErrors(parsed);
        const summary = parsed ? [...Object.values(parsed.byField).flat(), ...parsed.general].join('; ') : 'Validation failed';
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
            {fieldErrors?.byField['name'] && fieldErrors.byField['name'].length > 0 && (
              <div className="message">{fieldErrors.byField['name'].join('; ')}</div>
            )}
          </FormField>
          <div className="actions">
            <button disabled={saving} onClick={onSave}>Save</button>
          </div>
          {fieldErrors && (
            <div className="message">
              {fieldErrors.general.length > 0 && (
                <div>{fieldErrors.general.join('; ')}</div>
              )}
              {/* Display block-related issues in a compact list */}
              {Object.entries(fieldErrors.byField).map(([key, msgs]) => (
                key !== 'name' && msgs.length > 0 ? (
                  <div key={key}>{key}: {msgs.join('; ')}</div>
                ) : null
              ))}
            </div>
          )}
        </div>
      )}

      {message && <div className="message">{message}</div>}
    </section>
  );
};