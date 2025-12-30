import React, { useEffect, useMemo, useState } from 'react';
import { ApiError } from '../../lib/api';
import { deleteImage, getImageBlob, getImageMetadata, overwriteImage, uploadImage, ImageMetadata } from '../../services/images';

const MAX_SIZE_LABEL = '10 MB';

export type ImageDetailProps = {
  imageId: string;
  onDeleted?: () => void;
  onUploaded?: (id: string) => void;
};

export const ImageDetailPage: React.FC<ImageDetailProps> = ({ imageId, onDeleted, onUploaded }) => {
  const [meta, setMeta] = useState<ImageMetadata | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);

  const id = useMemo(() => imageId.trim(), [imageId]);

  const load = async () => {
    if (!id) return;
    setLoading(true);
    setError(null);
    setNotFound(false);
    setMessage(null);
    try {
      const m = await getImageMetadata(id);
      setMeta(m);
      const blob = await getImageBlob(id);
      const url = URL.createObjectURL(blob);
      setPreviewUrl((prev) => {
        if (prev) URL.revokeObjectURL(prev);
        return url;
      });
    } catch (e: any) {
      if (e instanceof ApiError && e.status === 404) {
        setNotFound(true);
        setMeta(null);
        setPreviewUrl(null);
      } else {
        setError(e?.message ?? 'Failed to load image');
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
    return () => {
      if (previewUrl) URL.revokeObjectURL(previewUrl);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const f = e.target.files?.[0];
    setFile(f ?? null);
    setMessage(null);
  };

  const handleUpload = async (mode: 'create' | 'overwrite') => {
    if (!file) {
      setError('Choose a file to upload');
      return;
    }
    setUploading(true);
    setError(null);
    setMessage(null);
    try {
      if (mode === 'create') {
        await uploadImage(id, file);
        setMessage('Image uploaded.');
      } else {
        await overwriteImage(id, file);
        setMessage('Image overwritten.');
      }
      onUploaded?.(id);
      await load();
    } catch (e: any) {
      if (e instanceof ApiError) {
        setError(e.message || 'Upload failed');
      } else {
        setError(e?.message ?? 'Upload failed');
      }
    } finally {
      setUploading(false);
    }
  };

  const handleDelete = async () => {
    setError(null);
    setMessage(null);
    try {
      await deleteImage(id);
      setMessage('Image deleted.');
      setMeta(null);
      setPreviewUrl(null);
      onDeleted?.();
    } catch (e: any) {
      setError(e?.message ?? 'Failed to delete image');
    }
  };

  if (!id) return null;

  return (
    <section className="image-detail" aria-label="Image detail section">
      <h2>Image Detail</h2>
      <p className="form-hint">Manage and overwrite stored image</p>

      {loading && <div>Loading...</div>}
      {notFound && <div role="alert">Image not found. Select another ID or upload to create it.</div>}
      {error && <div className="form-error" role="alert">{error}</div>}
      {message && <div className="form-hint">{message}</div>}

      <div className="image-meta">
        <div><strong>ID:</strong> <span data-testid="selected-image-id">{id}</span></div>
        {meta && (
          <ul>
            <li><strong>Content Type:</strong> {meta.contentType}</li>
            <li><strong>Size:</strong> {meta.sizeBytes} bytes</li>
            {meta.filename && <li><strong>Filename:</strong> {meta.filename}</li>}
            {meta.updatedAtUtc && <li><strong>Updated:</strong> {meta.updatedAtUtc}</li>}
          </ul>
        )}
      </div>

      {previewUrl && <img src={previewUrl} alt={`Preview of ${id}`} style={{ maxWidth: '320px', border: '1px solid #ddd' }} />}

      <div className="image-upload">
        <label htmlFor="image-file">Upload (PNG/JPEG, ≤ {MAX_SIZE_LABEL})</label>
        <input id="image-file" type="file" accept="image/png,image/jpeg" onChange={handleFileChange} />
        <div className="actions-row">
          <button type="button" disabled={uploading || !file} onClick={() => void handleUpload('create')}>Upload New</button>
          <button type="button" disabled={uploading || !file} onClick={() => void handleUpload('overwrite')}>Overwrite</button>
        </div>
      </div>

      <div className="image-delete">
        <button type="button" onClick={() => void handleDelete()} disabled={uploading}>Delete</button>
      </div>
    </section>
  );
};
