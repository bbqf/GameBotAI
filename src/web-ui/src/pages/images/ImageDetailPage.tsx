import React, { useEffect, useMemo, useState } from 'react';
import { ApiError } from '../../lib/api';
import { deleteImage, detectImage, getImageBlob, getImageMetadata, overwriteImage, uploadImage, ImageMetadata, DetectMatch, DetectResponse } from '../../services/images';

const MAX_SIZE_LABEL = '10 MB';
const DEFAULT_THRESHOLD = 0.86;
const DEFAULT_OVERLAP = 0.1;
const DEFAULT_MAX_RESULTS = 1;

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
  const [detectThreshold, setDetectThreshold] = useState(DEFAULT_THRESHOLD);
  const [detectOverlap, setDetectOverlap] = useState(DEFAULT_OVERLAP);
  const [detectMaxResults, setDetectMaxResults] = useState(DEFAULT_MAX_RESULTS);
  const [detecting, setDetecting] = useState(false);
  const [detectError, setDetectError] = useState<string | null>(null);
  const [detectResult, setDetectResult] = useState<DetectResponse | null>(null);

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

  useEffect(() => {
    setDetectResult(null);
    setDetectError(null);
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

  const handleDetect = async () => {
    setDetecting(true);
    setDetectError(null);
    setMessage(null);
    try {
      const resp = await detectImage(id, { threshold: detectThreshold, maxResults: detectMaxResults, overlap: detectOverlap });
      setDetectResult(resp);
      if (resp.limitsHit) {
        setMessage('Detection reached max results; more matches may exist.');
      } else {
        setMessage('Detection completed.');
      }
    } catch (e: any) {
      const msg = e instanceof ApiError ? e.message : e?.message ?? 'Detection failed';
      setDetectError(msg);
      setDetectResult(null);
    } finally {
      setDetecting(false);
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

      <div className="image-detect">
        <h3>Detect</h3>
        <p className="form-hint">Run detection against the current screen using this image.</p>
        <div className="grid three-cols">
          <label>
            Max Results
            <input
              type="number"
              min={1}
              max={100}
              value={detectMaxResults}
              onChange={(e) => setDetectMaxResults(Math.max(1, Math.min(100, Number(e.target.value) || DEFAULT_MAX_RESULTS)))}
            />
          </label>
          <label>
            Threshold
            <input
              type="number"
              step="0.01"
              min={0}
              max={1}
              value={detectThreshold}
              onChange={(e) => setDetectThreshold(Math.min(1, Math.max(0, Number(e.target.value) || DEFAULT_THRESHOLD)))}
            />
          </label>
          <label>
            Overlap
            <input
              type="number"
              step="0.01"
              min={0}
              max={1}
              value={detectOverlap}
              onChange={(e) => setDetectOverlap(Math.min(1, Math.max(0, Number(e.target.value) || DEFAULT_OVERLAP)))}
            />
          </label>
        </div>
        <button type="button" disabled={detecting || loading || notFound} onClick={() => void handleDetect()}>Run Detect</button>
        {detectError && <div className="form-error" role="alert">{detectError}</div>}
        {detectResult && (
          <div className="detect-results">
            {detectResult.limitsHit && <div className="form-hint">Max results reached; more matches may exist.</div>}
            {detectResult.matches.length === 0 && <div>No matches found.</div>}
            {detectResult.matches.length > 0 && (
              <table aria-label="Detection results">
                <thead>
                  <tr>
                    <th>Template ID</th>
                    <th>Score</th>
                    <th>X</th>
                    <th>Y</th>
                    <th>Width</th>
                    <th>Height</th>
                    <th>Overlap</th>
                  </tr>
                </thead>
                <tbody>
                  {detectResult.matches.map((m: DetectMatch, idx: number) => (
                    <tr key={`${m.templateId}-${idx}`}>
                      <td>{m.templateId}</td>
                      <td>{m.score.toFixed(2)}</td>
                      <td>{m.x.toFixed(2)}</td>
                      <td>{m.y.toFixed(2)}</td>
                      <td>{m.width.toFixed(2)}</td>
                      <td>{m.height.toFixed(2)}</td>
                      <td>{m.overlap.toFixed(2)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}
      </div>
    </section>
  );
};
