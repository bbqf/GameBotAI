import React, { useEffect, useMemo, useState } from 'react';
import { listImages } from '../../services/images';

export const ImagesListPage: React.FC = () => {
  const [ids, setIds] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | undefined>(undefined);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError(undefined);
    try {
      const data = await listImages();
      setIds(data);
    } catch (e: any) {
      setError(e?.message ?? 'Failed to load images');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, []);

  const sortedIds = useMemo(() => [...ids].sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' })), [ids]);

  return (
    <section>
      <h2>Images</h2>
      <div className="images-header">
        <button type="button" onClick={() => { void load(); }} disabled={loading}>Refresh</button>
      </div>
      {error && <div className="form-error" role="alert">{error}</div>}
      <table className="images-table" aria-label="Images table">
        <thead>
          <tr>
            <th>Image ID</th>
          </tr>
        </thead>
        <tbody>
          {loading && <tr><td>Loading...</td></tr>}
          {!loading && sortedIds.length === 0 && (
            <tr><td>No images found. Add an image via the API to begin.</td></tr>
          )}
          {!loading && sortedIds.length > 0 && sortedIds.map((id) => (
            <tr key={id}>
              <td>
                <button
                  type="button"
                  className="link-button"
                  onClick={() => { setSelectedId(id); }}
                >
                  {id}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {selectedId && (
        <section className="image-detail" aria-label="Image detail">
          <h2>Image Detail</h2>
          <p className="form-hint">Selected image</p>
          <p aria-label="selected-image-id" data-testid="selected-image-id">{selectedId}</p>
          <p className="form-hint">Preview and overwrite actions will appear here.</p>
        </section>
      )}
    </section>
  );
};
