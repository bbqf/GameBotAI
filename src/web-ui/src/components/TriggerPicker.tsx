import React, { useEffect, useMemo, useState } from 'react';
import { getJson } from '../lib/api';

type TriggerDto = {
  id: string;
  type: string;
  enabled?: boolean;
};

export const TriggerPicker: React.FC<{
  onSelect: (id: string) => void;
}> = ({ onSelect }) => {
  const [items, setItems] = useState<TriggerDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [q, setQ] = useState('');
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    const load = async () => {
      setLoading(true);
      setMessage(null);
      try {
        const data = await getJson<TriggerDto[]>("/triggers");
        setItems(Array.isArray(data) ? data : []);
      } catch (e: any) {
        setMessage(e?.message ?? String(e));
      } finally {
        setLoading(false);
      }
    };
    load();
  }, []);

  const filtered = useMemo(() => {
    const qq = q.trim().toLowerCase();
    if (!qq) return items;
    return items.filter(i => i.id.toLowerCase().includes(qq) || (i.type ?? '').toLowerCase().includes(qq));
  }, [items, q]);

  return (
    <section>
      <h3>Trigger Picker</h3>
      <div className="row">
        <label htmlFor="triggerSearch">Search</label>
        <input id="triggerSearch" type="text" value={q} onChange={(e) => setQ(e.target.value)} placeholder="id or type" />
      </div>
      {message && <div className="message">{message}</div>}
      <div>
        {loading && <div>Loading...</div>}
        {!loading && filtered.length === 0 && <div>No triggers</div>}
        {!loading && filtered.length > 0 && (
          <ul>
            {filtered.map(i => (
              <li key={i.id}>
                <button onClick={() => onSelect(i.id)}>{i.id}</button> <span style={{ opacity: 0.7 }}>({i.type})</span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
};