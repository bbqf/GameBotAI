import React, { useState } from 'react';
import { apiPost } from '../lib/api';

type CreateResponse = {
  id: string;
  name?: string;
};

export const SequencesCreate: React.FC = () => {
  const [name, setName] = useState('');
  const [creating, setCreating] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [created, setCreated] = useState<CreateResponse | null>(null);

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
      const res = await apiPost('/api/sequences', payload);
      if (res.status === 201) {
        const data = await res.json();
        setCreated(data);
        setMessage(`Created sequence with id ${data.id}`);
      } else if (res.status === 400) {
        const err = await res.json();
        const summary = Array.isArray(err.errors)
          ? err.errors.map((e: any) => e.message ?? JSON.stringify(e)).join('; ')
          : (err.message ?? 'Validation failed');
        setMessage(`Validation error: ${summary}`);
      } else {
        setMessage(`Unexpected status: ${res.status}`);
      }
    } catch (e: any) {
      setMessage(`Error: ${e?.message ?? e}`);
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
      </div>
      <div className="actions">
        <button disabled={!name || creating} onClick={onCreate}>Create</button>
      </div>
      {message && <div className="message">{message}</div>}
      {created && (
        <div className="result">
          <div>ID: {created.id}</div>
          {created.name && <div>Name: {created.name}</div>}
        </div>
      )}
    </section>
  );
};