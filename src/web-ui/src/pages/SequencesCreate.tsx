import React, { useState } from 'react';
import { ApiError, postJson } from '../lib/api';
import { parseFromError, ParsedValidation } from '../lib/validation';

type CreateResponse = {
  id: string;
  name?: string;
};

export const SequencesCreate: React.FC<{ onCreated?: (id: string) => void }> = ({ onCreated }) => {
  const [name, setName] = useState('');
  const [creating, setCreating] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [created, setCreated] = useState<CreateResponse | null>(null);
  const [fieldErrors, setFieldErrors] = useState<ParsedValidation | null>(null);

  const onCreate = async () => {
    setCreating(true);
    setMessage(null);
    setCreated(null);
    try {
      // Minimal payload; server sets timestamps
      const payload = {
        name,
        steps: [],
        blocks: []
      };
      const data = await postJson<CreateResponse>('/api/sequences', payload);
      setCreated(data);
      setMessage(`Created sequence with id ${data?.id}`);
      if (data?.id && onCreated) onCreated(data.id);
      setFieldErrors(null);
    } catch (e: unknown) {
      if (e instanceof ApiError && e.status === 400) {
        const parsed = parseFromError(e);
        setFieldErrors(parsed);
        const summary = parsed ? [...Object.values(parsed.byField).flat(), ...parsed.general].join('; ') : 'Validation failed';
        setMessage(`Validation error: ${summary}`);
      } else if (e instanceof Error) {
        setMessage(`Error: ${e.message}`);
      } else {
        setMessage('Error: Unknown');
      }
    } finally {
      setCreating(false);
    }
  };

  return (
    <section>
      <h2>Create Sequence</h2>
      <div className="row">
        <label htmlFor="sequenceName">Name</label>
        <input
          id="sequenceName"
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="My Sequence"
        />
        {fieldErrors?.byField['name'] && fieldErrors.byField['name'].length > 0 && (
          <div className="message">{fieldErrors.byField['name'].join('; ')}</div>
        )}
      </div>
      <div className="actions">
        <button disabled={!name || creating} onClick={onCreate}>Create</button>
      </div>
      {message && <div className="message" role="alert" aria-live="polite">{message}</div>}
      {fieldErrors && (
        <div className="message">
          {fieldErrors.general.length > 0 && (
            <div>{fieldErrors.general.join('; ')}</div>
          )}
          {Object.entries(fieldErrors.byField).map(([key, msgs]) => (
            key !== 'name' && msgs.length > 0 ? (
              <div key={key}>{key}: {msgs.join('; ')}</div>
            ) : null
          ))}
        </div>
      )}
      {created && (
        <div className="result">
          <div>ID: {created.id}</div>
          {created.name && <div>Name: {created.name}</div>}
        </div>
      )}
    </section>
  );
};