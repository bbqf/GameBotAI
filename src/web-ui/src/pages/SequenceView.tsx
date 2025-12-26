import React, { useEffect, useState } from 'react';
import { apiGet } from '../lib/api';

type Sequence = {
  id: string;
  name?: string;
  steps?: any[];
  blocks?: any[];
};

export const SequenceView: React.FC<{ defaultId?: string }> = ({ defaultId }) => {
  const [id, setId] = useState(defaultId ?? '');
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [seq, setSeq] = useState<Sequence | null>(null);

  useEffect(() => {
    if (defaultId && defaultId !== id) setId(defaultId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [defaultId]);

  const onFetch = async () => {
    setLoading(true);
    setMessage(null);
    setSeq(null);
    try {
      const res = await apiGet(`/api/sequences/${encodeURIComponent(id)}`);
      if (res.status === 200) {
        const data = await res.json();
        setSeq(data);
      } else if (res.status === 404) {
        setMessage('Not found');
      } else {
        setMessage(`Unexpected status: ${res.status}`);
      }
    } catch (e: any) {
      setMessage(`Error: ${e?.message ?? e}`);
    } finally {
      setLoading(false);
    }
  };

  return (
    <section>
      <h2>View Sequence</h2>
      <div className="row">
        <label htmlFor="sequenceId">Sequence ID</label>
        <input
          id="sequenceId"
          type="text"
          value={id}
          onChange={(e) => setId(e.target.value)}
          placeholder="Enter sequence id"
        />
        <button disabled={!id || loading} onClick={onFetch}>Fetch</button>
      </div>
      {message && <div className="message" role="alert" aria-live="polite">{message}</div>}
      {seq && (
        <pre className="json">{JSON.stringify(seq, null, 2)}</pre>
      )}
    </section>
  );
};