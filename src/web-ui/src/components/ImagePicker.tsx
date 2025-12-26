import React, { useEffect, useMemo, useState } from 'react';
import { getJson } from '../lib/api';

type ImageRef = { id: string };

export const ImagePicker: React.FC<{
  onSelect: (id: string) => void;
}> = ({ onSelect }) => {
  const [items, setItems] = useState<ImageRef[]>([]);
  const [loading, setLoading] = useState(false);
  const [q, setQ] = useState('');
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    const load = async () => {
      setLoading(true);
      setMessage(null);
      try {
        const data = await getJson<ImageRef[]>("/images");
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
    return items.filter(i => i.id.toLowerCase().includes(qq));
  }, [items, q]);

  return (
    <section>
      <h3>Image Picker</h3>
      <div className="row">
        <label htmlFor="imageSearch">Search</label>
        <input id="imageSearch" type="text" value={q} onChange={(e) => setQ(e.target.value)} placeholder="image id" />
      </div>
      {message && <div className="message">{message}</div>}
      <div>
        {loading && <div>Loading...</div>}
        {!loading && filtered.length === 0 && <div>No images</div>}
        {!loading && filtered.length > 0 && (
          <ul>
            {filtered.map(i => (
              <li key={i.id}>
                <button onClick={() => onSelect(i.id)}>{i.id}</button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
};