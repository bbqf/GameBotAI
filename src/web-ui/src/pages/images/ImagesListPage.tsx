import React, { useEffect, useMemo, useState } from 'react';
import { ApiError } from '../../lib/api';
import { ImageDetailPage } from './ImageDetailPage';
import { listImages, uploadImage } from '../../services/images';
import { EmulatorCaptureCropper } from '../../components/images/EmulatorCaptureCropper';

export const ImagesListPage: React.FC = () => {
  const [ids, setIds] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | undefined>(undefined);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [createId, setCreateId] = useState('');
  const [createFile, setCreateFile] = useState<File | null>(null);
  const [createMessage, setCreateMessage] = useState<string | null>(null);

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
      <EmulatorCaptureCropper />
      <div className="images-header">
        <button type="button" onClick={() => { void load(); }} disabled={loading}>Refresh</button>
      </div>
      <section className="image-create" aria-label="Create image">
        <h3>Create Image</h3>
        <label htmlFor="create-image-id">Image ID</label>
        <input
          id="create-image-id"
          value={createId}
          onChange={(e) => { setCreateId(e.target.value); setCreateMessage(null); setError(undefined); }}
          placeholder="enter image id"
        />
        <label htmlFor="create-image-file">File (PNG/JPEG, ≤10 MB)</label>
        <input id="create-image-file" type="file" accept="image/png,image/jpeg" onChange={(e) => setCreateFile(e.target.files?.[0] ?? null)} />
        <button
          type="button"
          onClick={async () => {
            setCreateMessage(null);
            setError(undefined);
            if (!createId.trim() || !createFile) {
              setError('ID and file are required');
              return;
            }
            try {
              await uploadImage(createId.trim(), createFile);
              setCreateMessage('Image uploaded.');
              setSelectedId(createId.trim());
              await load();
            } catch (e: any) {
              if (e instanceof ApiError) setError(e.message || 'Upload failed');
              else setError(e?.message ?? 'Upload failed');
            }
          }}
          disabled={loading}
        >
          Upload
        </button>
        {createMessage && <div className="form-hint">{createMessage}</div>}
      </section>
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
        <ImageDetailPage
          imageId={selectedId}
          onUploaded={async () => { await load(); }}
          onDeleted={async () => { setSelectedId(null); await load(); }}
        />
      )}
    </section>
  );
};
